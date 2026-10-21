using System.Globalization;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.Ledger;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed class LedgerService
    {
        private readonly LedgerRepository _ledgerRepository;
        private readonly VirtualAccountRepository _virtualAccountRepository;

        private const int PairingMarginDays = 7;

        public LedgerService(LedgerRepository ledgerRepository,
                             VirtualAccountRepository virtualAccountRepository)
        {
            _ledgerRepository = ledgerRepository;
            _virtualAccountRepository = virtualAccountRepository;
        }

        public async Task<LedgerResult> ClassifyAsync(long viewerUserId,
                                                      DateOnly from,
                                                      DateOnly to)
        {
            LedgerAccount[] accounts = (await _ledgerRepository.GetAccountsAsync(new[] { viewerUserId })).Select(ToAccount).ToArray();

            if (accounts.Length == 0) return new LedgerResult(Array.Empty<LedgerEntry>(), new Dictionary<long, long>());

            LedgerTransaction[] transactions = (await _ledgerRepository.GetTransactionsAsync(accounts.Select(account => account.Id).ToArray(),
                                                                                             from.AddDays(-PairingMarginDays),
                                                                                             to.AddDays(PairingMarginDays)))
                                               .Select(ToTransaction)
                                               .ToArray();

            VirtualAccountDto.VirtualAccount[] virtualAccounts = await _virtualAccountRepository.GetVirtualAccountsForUserAsync(viewerUserId);

            LedgerResult result = LedgerEngine.Classify(new LedgerInput(accounts,
                                                                        transactions,
                                                                        virtualAccounts.Select(ToVirtualAccount).ToArray(),
                                                                        new LedgerScope { ViewerUserId = viewerUserId }));

            return new LedgerResult(result.Entries.Where(entry => entry.Date >= from && entry.Date <= to).ToArray(),
                                    result.DuplicateOf);
        }

        public static LedgerAccount ToAccount(LedgerAccountRow row)
        {
            string compactIban = new string((row.Iban ?? "").Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

            string physicalKey = !string.IsNullOrEmpty(row.IdentificationHash)
                ? $"hash:{row.IdentificationHash}"
                : compactIban.Length > 0
                    ? $"iban:{compactIban}"
                    : $"row:{row.Id}";

            return new LedgerAccount(row.Id,
                                     row.OwnerUserId,
                                     physicalKey,
                                     row.Iban,
                                     row.Name,
                                     row.Currency,
                                     row.Role ?? AccountRoles.FromBankType(row.BankAccountType),
                                     row.CreditAccountId,
                                     row.CreditName,
                                     row.CreditMode);
        }

        public static LedgerTransaction ToTransaction(LedgerTransactionRow row)
        {
            return new LedgerTransaction(row.Id,
                                         row.AccountId,
                                         DateOnly.ParseExact(row.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                                         row.Amount,
                                         row.Currency,
                                         row.Status,
                                         row.CounterpartyName,
                                         row.CounterpartyAccount,
                                         row.BankTransactionCode,
                                         row.ExternalId,
                                         row.TagKind,
                                         row.TagKey,
                                         row.TagName,
                                         row.TagSource,
                                         row.CountingOverride,
                                         row.TypeOverride);
        }

        public static LedgerVirtualAccount ToVirtualAccount(VirtualAccountDto.VirtualAccount account)
        {
            return new LedgerVirtualAccount(account.Id,
                                            account.Name,
                                            VirtualAccountMatcher.Keys(account.AccountNumber, account.ClearingNumber, account.Iban),
                                            account.Counting);
        }
    }
}
