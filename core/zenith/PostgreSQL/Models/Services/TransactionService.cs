using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.EnableBanking;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed class TransactionService
    {
        private readonly TransactionRepository _transactionRepository;
        private readonly UserRepository _userRepository;

        private readonly EnableBankingWorkspace _workspace;

        public TransactionService(TransactionRepository transactionRepository,
                                  UserRepository userRepository,
                                  EnableBankingWorkspace workspace)
        {
            _transactionRepository = transactionRepository;
            _userRepository = userRepository;

            _workspace = workspace;
        }

        public async Task<TransactionDto.TransactionsResponse?> GetTransactionsAsync(string sessionId,
                                                                                    DateOnly from,
                                                                                    DateOnly to)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            TransactionAccountRow[] accounts = await _transactionRepository.GetAccountsForUserAsync(userId.Value);
            TransactionDto.Transaction[] transactions = await _transactionRepository.GetTransactionsForUserAsync(userId.Value, from, to);

            foreach (TransactionDto.Transaction transaction in transactions)
                transaction.Description = TransactionText.Describe(transaction.CounterpartyName, transaction.RemittanceInformation);

            DateTime now = DateTime.UtcNow;

            return new TransactionDto.TransactionsResponse
            {
                From = from,
                To = to,
                Accounts = accounts.Select(account => new TransactionDto.Account
                {
                    Id = account.Id,
                    Name = account.Name,
                    BankName = account.BankName,
                    Iban = account.Iban,
                    Currency = account.Currency,
                    IsActive = account.IsActive,
                    TransactionsSyncedAt = account.TransactionsSyncedAt,
                    IsStale = account.IsActive && IsDue(account.TransactionsSyncedAt, now, SyncInterval)
                }).ToArray(),
                Transactions = transactions
            };
        }

        public async Task<TransactionDto.SyncResponse?> SyncTransactionsAsync(string sessionId,
                                                                              bool force)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            TransactionAccountRow[] accounts = (await _transactionRepository.GetAccountsForUserAsync(userId.Value))
                                               .Where(account => account.IsActive)
                                               .ToArray();

            _workspace.Authenticator.GetToken();

            TransactionDto.SyncResult[][] results = await Task.WhenAll(
                accounts.GroupBy(account => account.BankConnectionId)
                        .Select(async connection =>
                        {
                            List<TransactionDto.SyncResult> connectionResults = new();

                            foreach (TransactionAccountRow account in connection)
                                connectionResults.Add(await SyncAccountAsync(userId.Value, account, force));

                            return connectionResults.ToArray();
                        }));

            return new TransactionDto.SyncResponse
            {
                Accounts = results.SelectMany(result => result)
                                  .OrderBy(result => result.AccountId)
                                  .ToArray()
            };
        }

        private async Task<TransactionDto.SyncResult> SyncAccountAsync(long userId,
                                                                       TransactionAccountRow account,
                                                                       bool force)
        {
            int lockIndex = (int)((ulong)account.Id % (ulong)SyncLocks.Length);

            SemaphoreSlim syncLock = SyncLocks[lockIndex];

            await syncLock.WaitAsync();

            DateTime? syncedAt = account.TransactionsSyncedAt;

            try
            {
                syncedAt = await _transactionRepository.GetTransactionsSyncedAtAsync(userId, account.Id);

                DateTime now = DateTime.UtcNow;

                if (!IsDue(syncedAt, now, force ? ForcedSyncInterval : SyncInterval))
                {
                    return new TransactionDto.SyncResult
                    {
                        AccountId = account.Id,
                        Status = TransactionDto.SyncStatus.Skipped,
                        SyncedAt = syncedAt
                    };
                }

                DateOnly today = DateOnly.FromDateTime(now);

                IReadOnlyList<FetchedTransaction> fetched = await FetchTransactionsAsync(account, syncedAt, today);
                TransactionEntity[] transactions = MapTransactions(fetched, account.Currency, today);

                await _transactionRepository.ReplaceTransactionsAsync(userId, account.Id, transactions, now);

                Console.WriteLine($"Transaction sync: account={account.Id}, " +
                                  $"fetched={fetched.Count}, " +
                                  $"stored={transactions.Length}");

                return new TransactionDto.SyncResult
                {
                    AccountId = account.Id,
                    Status = TransactionDto.SyncStatus.Synced,
                    Fetched = transactions.Length,
                    SyncedAt = now
                };
            }
            catch (Exception errno)
            {
                Console.WriteLine($"Transaction sync failed for account {account.Id}: {errno}");

                return new TransactionDto.SyncResult
                {
                    AccountId = account.Id,
                    Status = TransactionDto.SyncStatus.Failed,
                    Error = DescribeSyncError(errno),
                    SyncedAt = syncedAt
                };
            }
            finally
            {
                syncLock.Release();
            }
        }

        private async Task<IReadOnlyList<FetchedTransaction>> FetchTransactionsAsync(TransactionAccountRow account,
                                                                                     DateTime? syncedAt,
                                                                                     DateOnly today)
        {
            if (syncedAt != null)
            {
                DateOnly from = DateOnly.FromDateTime(syncedAt.Value).AddDays(-SyncOverlapDays);

                return await _workspace.Authenticator.FetchAccountTransactions(account.EnableBankingUid, from);
            }

            try
            {
                return await _workspace.Authenticator.FetchAccountTransactions(account.EnableBankingUid,
                                                                               today.AddDays(-InitialHistoryDays),
                                                                               "longest");
            }
            catch (EnableBankingException errno)
            {
                Console.WriteLine($"Full transaction history failed for account {account.Id} " +
                                  $"({errno.ErrorCode}: {errno.Message}), retrying with the last {FallbackHistoryDays} days");

                return await _workspace.Authenticator.FetchAccountTransactions(account.EnableBankingUid,
                                                                               today.AddDays(-FallbackHistoryDays));
            }
        }

        private static TransactionEntity[] MapTransactions(IReadOnlyList<FetchedTransaction> fetched,
                                                           string accountCurrency,
                                                           DateOnly today)
        {
            List<(TransactionEntity Entity, string BaseKey, string ContentHash)> mapped = new();

            foreach (FetchedTransaction item in fetched)
            {
                TransactionEntity? entity = MapTransaction(item, accountCurrency, today);

                if (entity == null) continue;

                string contentHash = HashContent(entity);

                string baseKey = entity.Status == BookedStatus && entity.EntryReference != null
                    ? $"er:{entity.EntryReference}"
                    : $"h:{contentHash}";

                mapped.Add((entity, baseKey, contentHash));
            }

            List<TransactionEntity> result = new(mapped.Count);

            foreach (var group in mapped.GroupBy(item => item.BaseKey))
            {
                if (group.Key.StartsWith("er:", StringComparison.Ordinal))
                {
                    var distinct = group.GroupBy(item => item.ContentHash)
                                        .Select(copies => copies.Last())
                                        .ToArray();

                    if (distinct.Length == 1)
                        result.Add(distinct[0].Entity with { ExternalId = group.Key });
                    else
                        result.AddRange(distinct.Select(item => item.Entity with { ExternalId = $"{group.Key}:{item.ContentHash[..12]}" }));
                }
                else
                {
                    result.AddRange(group.Select((item, index) => item.Entity with { ExternalId = $"{group.Key}:{index + 1}" }));
                }
            }

            return result.ToArray();
        }

        private static TransactionEntity? MapTransaction(FetchedTransaction item,
                                                         string accountCurrency,
                                                         DateOnly today)
        {
            EnableBankingEntities.Transaction transaction = item.transaction;

            if (!TryParseAmount(transaction.transactionAmount?.amount, out decimal amount))
            {
                Console.WriteLine("Skipping a transaction without a readable amount");
                return null;
            }

            string? indicator = NormalizeCode(transaction.creditDebitIndicator);
            string creditDebitIndicator = indicator is CreditIndicator or DebitIndicator
                ? indicator
                : amount < 0 ? DebitIndicator : CreditIndicator;

            bool outgoing = creditDebitIndicator == DebitIndicator;
            string currency = NormalizeCurrency(transaction.transactionAmount?.currency) ?? accountCurrency;

            DateOnly? bookingDate = ParseDate(transaction.bookingDate);
            DateOnly? valueDate = ParseDate(transaction.valueDate);
            DateOnly? transactionDate = ParseDate(transaction.transactionDate);

            EnableBankingEntities.PartyIdentification? counterparty = outgoing ? transaction.creditor : transaction.debtor;
            EnableBankingEntities.AccountIdentification? counterpartyAccount = outgoing ? transaction.creditorAccount : transaction.debtorAccount;

            EnableBankingEntities.AmountType? instructed = transaction.exchangeRate?.instructedAmount;
            string? originalCurrency = NormalizeCurrency(instructed?.currency);
            decimal? originalAmount = null;

            if (originalCurrency != null && originalCurrency != currency && TryParseAmount(instructed?.amount, out decimal parsedOriginal))
                originalAmount = outgoing ? -Math.Abs(parsedOriginal) : Math.Abs(parsedOriginal);
            else
                originalCurrency = null;

            return new TransactionEntity
            {
                EntryReference = Clean(transaction.entryReference),

                Status = NormalizeCode(transaction.status) ?? OtherStatus,
                CreditDebitIndicator = creditDebitIndicator,

                Amount = outgoing ? -Math.Abs(amount) : Math.Abs(amount),
                Currency = currency,
                OriginalAmount = originalAmount,
                OriginalCurrency = originalCurrency,

                BookingDate = bookingDate,
                ValueDate = valueDate,
                TransactionDate = transactionDate,
                OccurredOn = transactionDate ?? bookingDate ?? valueDate ?? today,

                CounterpartyName = Clean(counterparty?.name),
                CounterpartyAccount = Clean(counterpartyAccount?.iban) ?? Clean(counterpartyAccount?.other?.identification),
                RemittanceInformation = (transaction.remittanceInformation ?? Array.Empty<string?>())
                                        .Select(Clean)
                                        .OfType<string>()
                                        .ToArray(),
                MerchantCategoryCode = Clean(transaction.merchantCategoryCode),
                BankTransactionCode = Clean(transaction.bankTransactionCode?.description) ?? Clean(transaction.bankTransactionCode?.code),
                ReferenceNumber = Clean(transaction.referenceNumber),
                Note = Clean(transaction.note),

                Raw = item.raw
            };
        }

        private static string HashContent(TransactionEntity entity)
        {
            string content = string.Join('\u001f',
                                         entity.Status,
                                         entity.CreditDebitIndicator,
                                         entity.Amount.ToString("0.########", CultureInfo.InvariantCulture),
                                         entity.Currency,
                                         FormatDate(entity.BookingDate),
                                         FormatDate(entity.ValueDate),
                                         FormatDate(entity.TransactionDate),
                                         entity.CounterpartyName,
                                         entity.CounterpartyAccount,
                                         entity.ReferenceNumber,
                                         string.Join('\u001e', entity.RemittanceInformation));

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..32].ToLowerInvariant();
        }

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }

        private static bool IsDue(DateTime? syncedAt,
                                  DateTime now,
                                  TimeSpan interval)
        {
            return syncedAt == null ||
                   syncedAt.Value > now ||
                   now - syncedAt.Value >= interval;
        }

        private static string DescribeSyncError(Exception errno)
        {
            return errno switch
            {
                EnableBankingException bankError => bankError.Message,
                TaskCanceledException => "The bank did not respond in time.",
                HttpRequestException => "Could not reach Enable Banking.",
                _ => "Unexpected error while syncing transactions."
            };
        }

        private static bool TryParseAmount(string? value, out decimal amount)
        {
            return decimal.TryParse(value,
                                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                    CultureInfo.InvariantCulture,
                                    out amount);
        }

        private static string? Clean(string? value) => TransactionText.Clean(value);

        private static string? NormalizeCode(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
        }

        private static string? NormalizeCurrency(string? value)
        {
            string? code = NormalizeCode(value);

            return code != null && code.Length == 3 && code.All(char.IsAsciiLetter) ? code : null;
        }

        private static DateOnly? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 10) return null;

            return DateOnly.TryParseExact(value[..10],
                                          "yyyy-MM-dd",
                                          CultureInfo.InvariantCulture,
                                          DateTimeStyles.None,
                                          out DateOnly date)
                ? date
                : null;
        }

        private static string? FormatDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private const string BookedStatus = "BOOK";
        private const string OtherStatus = "OTHR";
        private const string CreditIndicator = "CRDT";
        private const string DebitIndicator = "DBIT";

        private const int InitialHistoryDays = 365;
        private const int FallbackHistoryDays = 89;
        private const int SyncOverlapDays = 14;

        private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan ForcedSyncInterval = TimeSpan.FromMinutes(1);

        private static readonly SemaphoreSlim[] SyncLocks = Enumerable.Range(0, 64)
                                                                      .Select(_ => new SemaphoreSlim(1, 1))
                                                                      .ToArray();
    }
}
