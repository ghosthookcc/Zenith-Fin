using System.Globalization;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.Categorization;
using ZenithFin.Ledger;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed class EconomyService
    {
        private readonly LedgerRepository _ledgerRepository;
        private readonly SharingRepository _sharingRepository;
        private readonly VirtualAccountRepository _virtualAccountRepository;
        private readonly UserRepository _userRepository;

        private const int PairingMarginDays = 7;
        private const int DefaultInviterShare = 5000;

        public sealed record Computation(long ViewerId,
                                         long? PartnerId,
                                         bool Active,
                                         LedgerAccount[] Accounts,
                                         LedgerResult Ledger,
                                         EconomyResult? Economy,
                                         Func<string, DateOnly, bool> IsJoint,
                                         SplitRuleRow[] Rules,
                                         ArrangementRow[] Arrangements,
                                         PartnershipRow? Partnership);

        public EconomyService(LedgerRepository ledgerRepository,
                              SharingRepository sharingRepository,
                              VirtualAccountRepository virtualAccountRepository,
                              UserRepository userRepository)
        {
            _ledgerRepository = ledgerRepository;
            _sharingRepository = sharingRepository;
            _virtualAccountRepository = virtualAccountRepository;
            _userRepository = userRepository;
        }

        public async Task<Computation> ComputeAsync(long viewerId,
                                                    DateOnly from,
                                                    DateOnly to)
        {
            PartnershipRow? partnership = await _sharingRepository.GetPartnershipAsync(viewerId, includeEnded: true);
            bool hasPartner = partnership is { Status: "ACCEPTED" or "ENDED" };
            long? partnerId = hasPartner ? partnership!.PartnerOf(viewerId) : null;

            LedgerAccount[] accounts = (await _ledgerRepository.GetAccountsAsync(partnerId == null ? new[] { viewerId } : new[] { viewerId, partnerId.Value }))
                                       .Select(LedgerService.ToAccount)
                                       .ToArray();

            AccountSharingRow[] sharings = hasPartner ? await _sharingRepository.GetAccountSharingsAsync(partnership!.Id) : Array.Empty<AccountSharingRow>();
            Func<string, DateOnly, bool> isJoint = LedgerService.JointPeriods(accounts, sharings);

            LedgerTransactionRow[] rows = accounts.Length == 0
                ? Array.Empty<LedgerTransactionRow>()
                : await _ledgerRepository.GetTransactionsAsync(accounts.Select(account => account.Id).ToArray(),
                                                                from.AddDays(-PairingMarginDays),
                                                                to.AddDays(PairingMarginDays));

            LedgerVirtualAccount[] virtualAccounts = (await _virtualAccountRepository.GetVirtualAccountsForUserAsync(viewerId))
                                                     .Select(LedgerService.ToVirtualAccount)
                                                     .ToArray();

            LedgerResult ledger = LedgerEngine.Classify(new LedgerInput(accounts,
                                                                        rows.Select(LedgerService.ToTransaction).ToArray(),
                                                                        virtualAccounts,
                                                                        new LedgerScope { ViewerUserId = viewerId, PartnerUserId = partnerId, IsJoint = isJoint }));

            if (!hasPartner)
                return new Computation(viewerId, null, false, accounts, ledger, null, isJoint, Array.Empty<SplitRuleRow>(), Array.Empty<ArrangementRow>(), partnership);

            SplitRuleRow[] rules = (await _sharingRepository.GetSplitRulesAsync(partnership!.Id)).Where(rule => rule.Status == "ACCEPTED").ToArray();
            ArrangementRow[] arrangements = await _sharingRepository.GetArrangementsAsync(partnership.Id);
            Dictionary<long, string> keys = accounts.ToDictionary(account => account.Id, account => account.PhysicalKey);

            Dictionary<long, EconomyTransactionText> texts = rows.ToDictionary(row => row.Id, row =>
            {
                string? description = TransactionText.Describe(row.CounterpartyName, row.RemittanceInformation);

                return new EconomyTransactionText(CorrectionKey.SignatureOf(description), row.CreditDebitIndicator, description);
            });

            EconomyArrangement[] active = arrangements.Where(arrangement => arrangement.Status is "ACCEPTED" or "ENDED" && keys.ContainsKey(arrangement.AccountId))
                                                      .Select(arrangement => new EconomyArrangement(arrangement.Id,
                                                                                                    keys[arrangement.AccountId],
                                                                                                    arrangement.Signature,
                                                                                                    arrangement.CreditDebitIndicator,
                                                                                                    ParseDate(arrangement.ValidFrom),
                                                                                                    arrangement.Status == "ENDED" && arrangement.EndedAt is DateTime ended
                                                                                                        ? DateOnly.FromDateTime(ended)
                                                                                                        : null,
                                                                                                    arrangement.Label,
                                                                                                    arrangement.PayerId))
                                                      .ToArray();

            EconomyResult economy = EconomyEngine.Compute(new EconomyInput(accounts,
                                                                           ledger.Entries,
                                                                           texts,
                                                                           active,
                                                                           viewerId,
                                                                           partnerId!.Value,
                                                                           partnership.InviterId,
                                                                           date => InviterShareOn(rules, date),
                                                                           isJoint,
                                                                           from,
                                                                           to));

            return new Computation(viewerId, partnerId, partnership.Status == "ACCEPTED", accounts, ledger, economy, isJoint, rules, arrangements, partnership);
        }

        public async Task<EconomyDto.Summary?> GetSummaryAsync(string sessionId,
                                                               DateOnly from,
                                                               DateOnly to)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            Computation computation = await ComputeAsync(userId.Value, from, to);
            EconomyResult? economy = computation.Economy;
            long me = userId.Value;

            string? partnerName = computation.PartnerId == null ? null : await _sharingRepository.GetUserNameAsync(computation.PartnerId.Value);
            Dictionary<string, LedgerAccount> byKey = computation.Accounts.GroupBy(account => account.PhysicalKey)
                                                                          .ToDictionary(group => group.Key,
                                                                                        group => group.OrderByDescending(account => account.OwnerUserId == me).ThenBy(account => account.Id).First());
            HashSet<long> sharedIds = economy?.Items.Select(item => item.TransactionId).ToHashSet() ?? new HashSet<long>();

            LedgerEntry[] privateEntries = computation.Ledger.Entries.Where(entry => entry.Date >= from &&
                                                                                     entry.Date <= to &&
                                                                                     entry.Counted &&
                                                                                     !computation.IsJoint(entry.PhysicalKey, entry.Date) &&
                                                                                     computation.Accounts.Any(account => account.Id == entry.AccountId && account.OwnerUserId == me))
                                                                     .ToArray();

            long income = privateEntries.Where(entry => entry.Type == TransactionTypes.Income && entry.CountedAmount > 0)
                                        .Sum(entry => EconomyEngine.ToMinor(entry.CountedAmount));
            long spending = -privateEntries.Where(entry => entry.CountedAmount != 0 &&
                                                           entry.Type != TransactionTypes.Income &&
                                                           !sharedIds.Contains(entry.TransactionId))
                                           .Sum(entry => EconomyEngine.ToMinor(entry.CountedAmount));
            long myShare = economy?.Share.GetValueOrDefault(me) ?? 0;

            spending += economy?.PrivateCardSpending.GetValueOrDefault(me) ?? 0;

            string[] jointKeys = byKey.Keys.Where(key => computation.IsJoint(key, to)).ToArray();
            string[] privateKeys = byKey.Keys.Where(key => !computation.IsJoint(key, to) && byKey[key].OwnerUserId == me).ToArray();
            Dictionary<long, decimal> balances = (await _ledgerRepository.GetBalancesAsync(byKey.Values.Select(account => account.Id).ToArray()))
                                                 .ToDictionary(row => row.AccountId, row => row.Amount);

            EconomyDto.Summary summary = new()
            {
                From = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                To = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                PartnerName = partnerName,
                Private = new EconomyDto.PrivateView
                {
                    Income = Kr(income),
                    Spending = Kr(spending),
                    MyShareOfShared = Kr(myShare),
                    Balance = privateKeys.Sum(key => balances.GetValueOrDefault(byKey[key].Id))
                },
                Combined = new EconomyDto.CombinedView
                {
                    Income = Kr(income),
                    Spending = Kr(spending + myShare),
                    Net = Kr(income - spending - myShare)
                },
                JointAccounts = jointKeys.Select(key => new EconomyDto.JointAccount
                {
                    Name = byKey[key].Name,
                    Balance = balances.TryGetValue(byKey[key].Id, out decimal balance) ? balance : null
                }).ToArray()
            };

            if (economy == null || computation.PartnerId == null || computation.Partnership == null) return summary;

            long partner = computation.PartnerId.Value;
            int inviterShare = InviterShareOn(computation.Rules, DateOnly.FromDateTime(DateTime.UtcNow));

            summary.MySharePercent = (computation.Partnership.InviterId == me ? inviterShare : 10000 - inviterShare) / 100m;
            summary.Shared = new EconomyDto.SharedView
            {
                Total = Kr(economy.TotalShared),
                MyShare = Kr(myShare),
                PartnerShare = Kr(economy.Share.GetValueOrDefault(partner)),
                PaidFromShared = Kr(economy.PaidJoint),
                PaidByMe = Kr(economy.PaidPrivately.GetValueOrDefault(me)),
                PaidByPartner = Kr(economy.PaidPrivately.GetValueOrDefault(partner)),
                MyFunding = Kr(economy.Funding.GetValueOrDefault(me)),
                PartnerFunding = Kr(economy.Funding.GetValueOrDefault(partner)),
                MyShareOfSharedAccounts = Kr(economy.JointShare.GetValueOrDefault(me)),
                PartnerShareOfSharedAccounts = Kr(economy.JointShare.GetValueOrDefault(partner)),
                UnattributedIncome = Kr(economy.UnattributedJointIncome),
                SettledByMe = Kr(economy.SettlementsPaid.GetValueOrDefault(me)),
                SettledByPartner = Kr(economy.SettlementsPaid.GetValueOrDefault(partner)),
                OwedToMe = Kr(economy.OwedToViewer)
            };
            summary.Transactions = economy.Items.Select(item => new EconomyDto.SharedTransaction
            {
                Id = item.TransactionId,
                Date = item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Description = item.Description,
                Amount = Kr(item.AmountMinor),
                PaidBy = item.Payer == EconomyEngine.JointPayer ? "Shared account" : item.Payer == "VIEWER" ? "You" : partnerName ?? "Partner",
                Reason = ReasonText(item, byKey),
                MyShare = Kr(item.ViewerShareMinor),
                PartnerShare = Kr(item.PartnerShareMinor),
                MySharePercent = item.ViewerShareBasisPoints / 100m
            }).ToArray();
            summary.Arrangements = computation.Arrangements.Where(arrangement => arrangement.Status is "ACCEPTED" or "PENDING")
                                                           .Select(arrangement => new EconomyDto.Arrangement
                                                           {
                                                               Id = arrangement.Id,
                                                               Label = arrangement.Label,
                                                               AccountName = arrangement.AccountName,
                                                               ValidFrom = arrangement.ValidFrom,
                                                               Status = arrangement.Status,
                                                               PaidByMe = arrangement.PayerId == me,
                                                               ProposedByMe = arrangement.ProposedBy == me
                                                           })
                                                           .ToArray();

            return summary;
        }

        public async Task<Dictionary<long, EconomyDto.SharedStatus>> GetSharedStatusesAsync(long viewerId,
                                                                                         DateOnly from,
                                                                                         DateOnly to)
        {
            Computation computation = await ComputeAsync(viewerId, from, to);
            Dictionary<long, EconomyDto.SharedStatus> statuses = new();

            if (computation.Economy == null) return statuses;

            string partnerName = await _sharingRepository.GetUserNameAsync(computation.PartnerId!.Value);
            Dictionary<string, LedgerAccount> byKey = computation.Accounts.GroupBy(account => account.PhysicalKey)
                                                                          .ToDictionary(group => group.Key, group => group.First());

            foreach (SharedItem item in computation.Economy.Items)
            {
                statuses[item.TransactionId] = new EconomyDto.SharedStatus
                {
                    Status = "SHARED",
                    Reason = ReasonText(item, byKey),
                    MyShare = Kr(item.ViewerShareMinor),
                    PartnerShare = Kr(item.PartnerShareMinor),
                    PaidBy = item.Payer == EconomyEngine.JointPayer ? "a shared account" : item.Payer == "VIEWER" ? "you" : partnerName
                };
            }

            if (!computation.Active) return statuses;

            foreach (LedgerEntry entry in computation.Ledger.Entries.Where(entry => entry.Date >= from &&
                                                                                   entry.Date <= to &&
                                                                                   entry.Type == TransactionTypes.Expense &&
                                                                                   entry.CountedAmount < 0 &&
                                                                                   !statuses.ContainsKey(entry.TransactionId) &&
                                                                                   !computation.IsJoint(entry.PhysicalKey, entry.Date) &&
                                                                                   computation.Accounts.Any(account => account.Id == entry.AccountId && account.OwnerUserId == viewerId)))
            {
                statuses[entry.TransactionId] = new EconomyDto.SharedStatus
                {
                    Status = "NOT_VERIFIED",
                    Reason = $"Not verified as shared: it was paid from your own account and no shared account or rule you and {partnerName} agreed on covers it"
                };
            }

            return statuses;
        }

        public static int InviterShareOn(IReadOnlyList<SplitRuleRow> rules,
                                         DateOnly date)
        {
            string day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return rules.Where(rule => rule.Status == "ACCEPTED" && string.CompareOrdinal(rule.ValidFrom, day) <= 0)
                        .OrderBy(rule => rule.ValidFrom, StringComparer.Ordinal)
                        .ThenBy(rule => rule.Id)
                        .LastOrDefault()?.InviterShare ?? DefaultInviterShare;
        }

        private static string ReasonText(SharedItem item,
                                         IReadOnlyDictionary<string, LedgerAccount> byKey)
        {
            string account = byKey.TryGetValue(item.PhysicalKey, out LedgerAccount? found) ? found.Name : "an account";

            return item.Reason == SharedReasons.Arrangement
                ? $"Shared – rule you both approved: {item.ArrangementLabel} from {account}"
                : $"Shared – shared account {account}";
        }

        private static decimal Kr(long minor) => minor / 100m;

        private static DateOnly ParseDate(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }
    }
}
