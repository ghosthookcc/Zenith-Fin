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
        private readonly SharingRepository _sharingRepository;

        private const int PairingMarginDays = 7;

        public LedgerService(LedgerRepository ledgerRepository,
                             VirtualAccountRepository virtualAccountRepository,
                             SharingRepository sharingRepository)
        {
            _ledgerRepository = ledgerRepository;
            _virtualAccountRepository = virtualAccountRepository;
            _sharingRepository = sharingRepository;
        }

        public sealed record SharingScope(long? PartnerId,
                                          bool Active,
                                          IReadOnlyList<AccountSharingRow> Sharings);

        public async Task<SharingScope> GetSharingScopeAsync(long viewerUserId)
        {
            PartnershipRow? partnership = await _sharingRepository.GetPartnershipAsync(viewerUserId, includeEnded: true);

            if (partnership == null || partnership.Status is not ("ACCEPTED" or "ENDED"))
                return new SharingScope(null, false, Array.Empty<AccountSharingRow>());

            return new SharingScope(partnership.PartnerOf(viewerUserId),
                                    partnership.Status == "ACCEPTED",
                                    await _sharingRepository.GetAccountSharingsAsync(partnership.Id));
        }

        public static Func<string, DateOnly, bool> JointPeriods(IEnumerable<LedgerAccount> accounts,
                                                                IEnumerable<AccountSharingRow> sharings)
        {
            Dictionary<long, string> keys = accounts.ToDictionary(account => account.Id, account => account.PhysicalKey);

            Dictionary<string, DateOnly?[]> periods = sharings.Where(sharing => sharing.Status is "ACCEPTED" or "ENDED" && keys.ContainsKey(sharing.AccountId))
                                                              .GroupBy(sharing => keys[sharing.AccountId])
                                                              .ToDictionary(group => group.Key,
                                                                            group => group.Select(sharing => sharing.Status == "ENDED" && sharing.EndedAt is DateTime ended
                                                                                                                 ? DateOnly.FromDateTime(ended)
                                                                                                                 : (DateOnly?)null)
                                                                                          .ToArray());

            return (key, date) => periods.TryGetValue(key, out DateOnly?[]? ends) && ends.Any(end => end == null || date <= end);
        }

        public async Task<LedgerResult> ClassifyAsync(long viewerUserId,
                                                      DateOnly from,
                                                      DateOnly to)
        {
            SharingScope sharing = await GetSharingScopeAsync(viewerUserId);

            LedgerAccount[] accounts = (await _ledgerRepository.GetAccountsAsync(sharing.PartnerId == null
                                                                                     ? new[] { viewerUserId }
                                                                                     : new[] { viewerUserId, sharing.PartnerId.Value }))
                                       .Select(ToAccount)
                                       .ToArray();

            HashSet<long> sharedIn = sharing.Active
                ? sharing.Sharings.Where(row => row.Status == "ACCEPTED" && row.OwnerId != viewerUserId).Select(row => row.AccountId).ToHashSet()
                : new HashSet<long>();

            long[] visible = accounts.Where(account => account.OwnerUserId == viewerUserId || sharedIn.Contains(account.Id))
                                     .Select(account => account.Id)
                                     .ToArray();

            if (visible.Length == 0) return new LedgerResult(Array.Empty<LedgerEntry>(), new Dictionary<long, long>());

            LedgerTransaction[] transactions = (await _ledgerRepository.GetTransactionsAsync(visible,
                                                                                             from.AddDays(-PairingMarginDays),
                                                                                             to.AddDays(PairingMarginDays)))
                                               .Select(ToTransaction)
                                               .ToArray();

            VirtualAccountDto.VirtualAccount[] virtualAccounts = await _virtualAccountRepository.GetVirtualAccountsForUserAsync(viewerUserId);

            LedgerResult result = LedgerEngine.Classify(new LedgerInput(accounts,
                                                                        transactions,
                                                                        virtualAccounts.Select(ToVirtualAccount).ToArray(),
                                                                        new LedgerScope
                                                                        {
                                                                            ViewerUserId = viewerUserId,
                                                                            PartnerUserId = sharing.PartnerId,
                                                                            IsJoint = JointPeriods(accounts, sharing.Sharings)
                                                                        }));

            return new LedgerResult(result.Entries.Where(entry => entry.Date >= from && entry.Date <= to).ToArray(),
                                    result.DuplicateOf);
        }

        public static LedgerAccount ToAccount(LedgerAccountRow row)
        {
            return new LedgerAccount(row.Id,
                                     row.OwnerUserId,
                                     PhysicalKey(row.Id, row.IdentificationHash, row.Iban),
                                     row.Iban,
                                     row.Name,
                                     row.Currency,
                                     row.Role ?? AccountRoles.FromBankType(row.BankAccountType),
                                     row.CreditAccountId,
                                     row.CreditName,
                                     row.CreditMode);
        }

        public static string PhysicalKey(long id,
                                         string? identificationHash,
                                         string? iban)
        {
            string compactIban = new string((iban ?? "").Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

            return !string.IsNullOrEmpty(identificationHash)
                ? $"hash:{identificationHash}"
                : compactIban.Length > 0
                    ? $"iban:{compactIban}"
                    : $"row:{id}";
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
