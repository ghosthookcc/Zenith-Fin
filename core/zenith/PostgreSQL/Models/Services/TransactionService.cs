using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.Categorization;
using ZenithFin.EnableBanking;
using ZenithFin.Ledger;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record CountingResult(int StatusCode,
                                        string? Message,
                                        string? Counting = null);

    public sealed record TypeResult(int StatusCode,
                                    string? Message,
                                    string? Type = null);

    public sealed class TransactionService
    {
        private readonly TransactionRepository _transactionRepository;
        private readonly UserRepository _userRepository;
        private readonly TagRepository _tagRepository;
        private readonly CreditAccountRepository _creditAccountRepository;
        private readonly ExpectedSalaryRepository _expectedSalaryRepository;
        private readonly VirtualAccountRepository _virtualAccountRepository;

        private readonly ManualTransactionService _manualTransactionService;
        private readonly LedgerService _ledgerService;
        private readonly EconomyService _economyService;
        private readonly CategorizationService _categorizationService;
        private readonly AiCategorizationService _aiCategorizationService;
        private readonly RecurringService _recurringService;

        private readonly EnableBankingWorkspace _workspace;

        public TransactionService(TransactionRepository transactionRepository,
                                  UserRepository userRepository,
                                  TagRepository tagRepository,
                                  CreditAccountRepository creditAccountRepository,
                                  ExpectedSalaryRepository expectedSalaryRepository,
                                  VirtualAccountRepository virtualAccountRepository,
                                  ManualTransactionService manualTransactionService,
                                  LedgerService ledgerService,
                                  EconomyService economyService,
                                  CategorizationService categorizationService,
                                  AiCategorizationService aiCategorizationService,
                                  RecurringService recurringService,
                                  EnableBankingWorkspace workspace)
        {
            _transactionRepository = transactionRepository;
            _userRepository = userRepository;
            _tagRepository = tagRepository;
            _creditAccountRepository = creditAccountRepository;
            _expectedSalaryRepository = expectedSalaryRepository;
            _virtualAccountRepository = virtualAccountRepository;

            _manualTransactionService = manualTransactionService;
            _ledgerService = ledgerService;
            _economyService = economyService;
            _categorizationService = categorizationService;
            _aiCategorizationService = aiCategorizationService;
            _recurringService = recurringService;

            _workspace = workspace;
        }

        public async Task<TransactionDto.TransactionsResponse?> GetTransactionsAsync(string sessionId,
                                                                                    DateOnly from,
                                                                                    DateOnly to)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            await CategorizeAsync(userId.Value, reviewRuleTags: false);

            TransactionAccountRow[] ownAccounts = await _transactionRepository.GetAccountsForUserAsync(userId.Value);
            TransactionAccountRow[] sharedAccounts = await _transactionRepository.GetSharedAccountsForUserAsync(userId.Value);
            TagDto.Tag[] tags = await _tagRepository.GetTagsForUserAsync(userId.Value);
            TransactionDto.Transaction[] transactions = await _transactionRepository.GetTransactionsForUserAsync(userId.Value,
                                                                                                                from,
                                                                                                                to,
                                                                                                                sharedAccounts.Select(account => account.Id).ToArray());

            OwnerProfile owner = await _categorizationService.GetOwnerAsync(userId.Value);
            Dictionary<CorrectionKey, RecurringSeries> recurring = await _recurringService.DetectAsync(userId.Value, owner);

            foreach (TransactionDto.Transaction transaction in transactions)
            {
                transaction.Description = TransactionText.Describe(transaction.CounterpartyName, transaction.RemittanceInformation);

                if (recurring.Count == 0 || transaction.Status is "CNCL" or "RJCT") continue;

                CorrectionKey? key = CorrectionKey.From(new CategorizationInput(transaction.CounterpartyName,
                                                                                transaction.RemittanceInformation,
                                                                                transaction.BankTransactionCode,
                                                                                transaction.CounterpartyAccount,
                                                                                transaction.CreditDebitIndicator,
                                                                                transaction.OriginalCurrency),
                                                        owner);

                if (key != null && recurring.TryGetValue(key.Value, out RecurringSeries? series))
                {
                    transaction.Recurring = new TransactionDto.RecurringInfo
                    {
                        Frequency = series.Frequency,
                        Label = series.Label,
                        Occurrences = series.Occurrences,
                        Next = series.Next.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Manual = series.Manual
                    };
                }
            }

            LedgerResult ledger = await _ledgerService.ClassifyAsync(userId.Value, from, to);
            Dictionary<long, LedgerEntry> entries = ledger.Entries.ToDictionary(entry => entry.TransactionId);

            Dictionary<string, long> ownByKey = ownAccounts.GroupBy(account => LedgerService.PhysicalKey(account.Id, account.IdentificationHash, account.Iban))
                                                           .ToDictionary(group => group.Key, group => group.Min(account => account.Id));
            Dictionary<long, TransactionAccountRow> sharedById = sharedAccounts.ToDictionary(account => account.Id);

            transactions = transactions.Where(transaction => !ledger.DuplicateOf.ContainsKey(transaction.Id)).ToArray();

            foreach (TransactionDto.Transaction transaction in transactions)
            {
                if (!sharedById.TryGetValue(transaction.AccountId, out TransactionAccountRow? shared)) continue;

                transaction.Editable = false;
                transaction.SharedBy = shared.SharedBy;

                if (ownByKey.TryGetValue(LedgerService.PhysicalKey(shared.Id, shared.IdentificationHash, shared.Iban), out long ownAccountId))
                    transaction.AccountId = ownAccountId;
            }

            Dictionary<long, EconomyDto.SharedStatus> sharedStatuses = await _economyService.GetSharedStatusesAsync(userId.Value, from, to);

            foreach (TransactionDto.Transaction transaction in transactions)
                transaction.Shared = sharedStatuses.GetValueOrDefault(transaction.Id);

            TransactionAccountRow[] accounts = ownAccounts.Concat(sharedAccounts.Where(account => !ownByKey.ContainsKey(LedgerService.PhysicalKey(account.Id,
                                                                                                                                                    account.IdentificationHash,
                                                                                                                                                    account.Iban))))
                                                          .ToArray();

            foreach (TransactionDto.Transaction transaction in transactions)
            {
                if (!entries.TryGetValue(transaction.Id, out LedgerEntry? entry)) continue;

                transaction.Type = entry.Type;
                transaction.Subtype = entry.Subtype;
                transaction.TypeSource = entry.TypeSource;
                transaction.TypeReason = entry.Reason;
                transaction.Effect = entry.Effect;
                transaction.EffectSource = entry.EffectSource;
                transaction.CountedAmount = entry.CountedAmount;
                transaction.Counted = entry.Counted;
                transaction.MatchedTransactionId = entry.CounterTransactionId;
                transaction.AutomaticType = entry.AutomaticType;
                transaction.AutomaticSubtype = entry.AutomaticSubtype;
                transaction.AutomaticReason = entry.AutomaticReason;
                transaction.AutomaticEffect = entry.AutomaticEffect;
                transaction.TypeEffect = entry.TypeEffect;
            }

            DateTime now = DateTime.UtcNow;

            string first = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string last = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            ManualTransactionDto.ManualTransaction[] manualTransactions = (await _manualTransactionService.GetManualTransactionsAsync(userId.Value))
                .Where(transaction => string.CompareOrdinal(transaction.Date, first) >= 0 &&
                                      string.CompareOrdinal(transaction.Date, last) <= 0)
                .ToArray();

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
                    IsStale = account.IsActive && IsDue(account.TransactionsSyncedAt, now, SyncInterval),
                    Role = account.Role ?? AccountRoles.FromBankType(account.BankAccountType),
                    RoleSource = account.Role != null ? "USER" : account.BankAccountType != null ? "BANK" : "DEFAULT",
                    CreditName = account.CreditName,
                    CreditMode = account.CreditMode,
                    SharedBy = account.SharedBy
                }).ToArray(),
                Tags = tags,
                CreditAccounts = await _creditAccountRepository.GetCreditAccountsForUserAsync(userId.Value),
                ExpectedSalaries = await _expectedSalaryRepository.GetExpectedSalariesForUserAsync(userId.Value),
                VirtualAccounts = await _virtualAccountRepository.GetVirtualAccountsForUserAsync(userId.Value),
                ManualTransactions = manualTransactions,
                Transactions = transactions
            };
        }

        public async Task<TransactionDto.SyncResponse?> SyncTransactionsAsync(string sessionId,
                                                                              bool force)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            TransactionDto.SyncResult[] results = await SyncUserAccountsAsync(userId.Value, force ? ForcedSyncInterval : SyncInterval);

            await CategorizeAsync(userId.Value, reviewRuleTags: true);
            await _aiCategorizationService.CategorizeAsync(userId.Value);

            return new TransactionDto.SyncResponse
            {
                Accounts = results
            };
        }

        public async Task<int> SyncInBackgroundAsync(TimeSpan interval)
        {
            int synced = 0;

            foreach (long userId in await _transactionRepository.GetUserIdsWithActiveAccountsAsync())
            {
                try
                {
                    int userSynced = (await SyncUserAccountsAsync(userId, interval)).Count(result => result.Status == TransactionDto.SyncStatus.Synced);

                    if (userSynced == 0) continue;

                    await CategorizeAsync(userId, reviewRuleTags: true);
                    await _aiCategorizationService.CategorizeAsync(userId);

                    synced += userSynced;
                }
                catch (Exception errno)
                {
                    Console.WriteLine($"Background sync failed for user {userId}: {errno.Message}");
                }
            }

            return synced;
        }

        private async Task<TransactionDto.SyncResult[]> SyncUserAccountsAsync(long userId,
                                                                               TimeSpan interval)
        {
            TransactionAccountRow[] accounts = (await _transactionRepository.GetAccountsForUserAsync(userId))
                                               .Where(account => account.IsActive)
                                               .ToArray();

            if (accounts.Length == 0) return Array.Empty<TransactionDto.SyncResult>();

            _workspace.Authenticator.GetToken();

            TransactionDto.SyncResult[][] results = await Task.WhenAll(
                accounts.GroupBy(account => account.BankConnectionId)
                        .Select(async connection =>
                        {
                            List<TransactionDto.SyncResult> connectionResults = new();

                            foreach (TransactionAccountRow account in connection)
                                connectionResults.Add(await SyncAccountAsync(userId, account, interval));

                            return connectionResults.ToArray();
                        }));

            return results.SelectMany(result => result)
                          .OrderBy(result => result.AccountId)
                          .ToArray();
        }

        private async Task<TransactionDto.SyncResult> SyncAccountAsync(long userId,
                                                                       TransactionAccountRow account,
                                                                       TimeSpan interval)
        {
            int lockIndex = (int)((ulong)account.Id % (ulong)SyncLocks.Length);

            SemaphoreSlim syncLock = SyncLocks[lockIndex];

            await syncLock.WaitAsync();

            DateTime? syncedAt = account.TransactionsSyncedAt;

            try
            {
                syncedAt = await _transactionRepository.GetTransactionsSyncedAtAsync(userId, account.Id);

                DateTime now = DateTime.UtcNow;

                if (!IsDue(syncedAt, now, interval))
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

                await StoreTransactionsAsync(userId, account.Id, transactions, now);

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

        private async Task<IReadOnlyList<FetchedTransaction>> FetchTransactionsAsync(
            TransactionAccountRow account,
            DateTime? syncedAt,
            DateOnly today)
        {
            if (syncedAt != null)
            {
                DateOnly from = DateOnly.FromDateTime(syncedAt.Value)
                                        .AddDays(-SyncOverlapDays);

                return await _workspace.Authenticator.FetchAccountTransactions(
                    account.EnableBankingUid,
                    from,
                    "default");
            }

            try
            {
                return await _workspace.Authenticator.FetchAccountTransactions(
                    account.EnableBankingUid,
                    today.AddDays(-InitialHistoryDays),
                    "longest");
            }
            catch (Exception error) when (
                error is EnableBankingException ||
                error is Authenticator.TransactionPaginationException)
            {
                DateOnly recentFrom = today.AddDays(-FallbackHistoryDays);

                Console.WriteLine(
                    $"Initial history fetch failed for account {account.Id}: " +
                    $"{error.Message}. Fetching recent transactions from " +
                    $"{recentFrom:yyyy-MM-dd}; older history remains incomplete.");

                return await _workspace.Authenticator.FetchAccountTransactions(
                    account.EnableBankingUid,
                    recentFrom,
                    "default");
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

        private async Task StoreTransactionsAsync(long userId,
                                                  long accountId,
                                                  IReadOnlyList<TransactionEntity> transactions,
                                                  DateTime now)
        {
            ReleasedTransactionRow[] released = await _transactionRepository.ReplaceTransactionsAsync(userId, accountId, transactions, now);
            (string ExternalId, ReleasedTransactionRow Released)[] successors = FindSuccessors(released, transactions);

            foreach (string source in new[] { CategorizationService.UserSource, CategorizationService.AiSource })
            {
                (string ExternalId, long TagId)[] tags = successors.Where(entry => entry.Released.TagSource == source &&
                                                                                   entry.Released.TagId != null)
                                                                   .Select(entry => (entry.ExternalId, entry.Released.TagId!.Value))
                                                                   .ToArray();

                if (tags.Length > 0) await _transactionRepository.RestoreTagsAsync(userId, accountId, tags, source, now);
            }

            (string ExternalId, string Counting)[] countings = successors.Where(entry => entry.Released.Counting != null)
                                                                         .Select(entry => (entry.ExternalId, entry.Released.Counting!))
                                                                         .ToArray();

            if (countings.Length > 0) await _transactionRepository.RestoreCountingAsync(userId, accountId, countings);

            (string ExternalId, string Type)[] types = successors.Where(entry => entry.Released.TypeOverride != null)
                                                                 .Select(entry => (entry.ExternalId, entry.Released.TypeOverride!))
                                                                 .ToArray();

            if (types.Length > 0) await _transactionRepository.RestoreTypesAsync(userId, accountId, types);
        }

        private static (string ExternalId, ReleasedTransactionRow Released)[] FindSuccessors(IReadOnlyList<ReleasedTransactionRow> released,
                                                                                             IReadOnlyList<TransactionEntity> transactions)
        {
            List<(string ExternalId, ReleasedTransactionRow Released)> restored = new();

            foreach (ReleasedTransactionRow row in released)
            {
                string? signature = CorrectionKey.SignatureOf(TransactionText.Describe(row.CounterpartyName, row.RemittanceInformation));
                DateOnly releasedOn = DateOnly.ParseExact(row.OccurredOn, "yyyy-MM-dd", CultureInfo.InvariantCulture);

                TransactionEntity? successor = transactions.Where(item => item.Status == BookedStatus &&
                                                                          item.CreditDebitIndicator == row.CreditDebitIndicator &&
                                                                          item.Amount == row.Amount &&
                                                                          item.Currency == row.Currency &&
                                                                          Math.Abs(item.OccurredOn.DayNumber - releasedOn.DayNumber) <= SuccessorMaxDays &&
                                                                          restored.All(entry => entry.ExternalId != item.ExternalId) &&
                                                                          CorrectionKey.SignatureOf(TransactionText.Describe(item.CounterpartyName,
                                                                                                                             item.RemittanceInformation)) == signature)
                                                           .MinBy(item => Math.Abs(item.OccurredOn.DayNumber - releasedOn.DayNumber));

                if (successor != null) restored.Add((successor.ExternalId, row));
            }

            return restored.ToArray();
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

        private async Task CategorizeAsync(long userId,
                                           bool reviewRuleTags)
        {
            try
            {
                int tagged = (await _categorizationService.CategorizeAsync(userId, reviewRuleTags)).Count;

                if (tagged > 0) Console.WriteLine($"Categorization: user={userId}, tagged={tagged}");
            }
            catch (Exception errno)
            {
                Console.WriteLine($"Categorization failed for user {userId}: {errno}");
            }
        }

        public async Task<CountingResult> SetCountingAsync(string sessionId,
                                                           long transactionId,
                                                           TransactionDto.SetCountingRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return new CountingResult(StatusCodes.Status401Unauthorized, "No user found for session");

            string? counting = string.IsNullOrWhiteSpace(request.Counting) ? null : request.Counting.Trim().ToUpperInvariant();

            if (counting != null && !ManualCountings.Contains(counting))
                return new CountingResult(StatusCodes.Status400BadRequest, "Choose neutral, money in or money out");

            if (!await _transactionRepository.SetCountingOverrideAsync(userId.Value, transactionId, counting))
                return new CountingResult(StatusCodes.Status404NotFound, "Transaction not found");

            return new CountingResult(StatusCodes.Status200OK, null, counting);
        }

        public async Task<TypeResult> SetTypeAsync(string sessionId,
                                                   long transactionId,
                                                   TransactionDto.SetTypeRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return new TypeResult(StatusCodes.Status401Unauthorized, "No user found for session");

            string? type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim().ToUpperInvariant();

            if (type != null && !TransactionTypes.All.Contains(type))
                return new TypeResult(StatusCodes.Status400BadRequest, "Choose income, expense, transfer, debt or credit, or investment");

            if (!await _transactionRepository.SetTypeOverrideAsync(userId.Value, transactionId, type))
                return new TypeResult(StatusCodes.Status404NotFound, "Transaction not found");

            return new TypeResult(StatusCodes.Status200OK, null, type);
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
        private const int SuccessorMaxDays = 7;

        private static readonly string[] ManualCountings = { "NEUTRAL", "IN", "OUT" };

        private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan ForcedSyncInterval = TimeSpan.FromMinutes(1);

        private static readonly SemaphoreSlim[] SyncLocks = Enumerable.Range(0, 64)
                                                                      .Select(_ => new SemaphoreSlim(1, 1))
                                                                      .ToArray();
    }
}
