namespace ZenithFin.Ledger
{
    public static class SharedReasons
    {
        public const string SharedAccount = "SHARED_ACCOUNT";
        public const string Arrangement = "ARRANGEMENT";
    }

    public sealed record EconomyArrangement(long Id,
                                            string PhysicalKey,
                                            string Signature,
                                            string CreditDebitIndicator,
                                            DateOnly ValidFrom,
                                            DateOnly? ValidTo,
                                            string Label,
                                            long PayerId);

    public sealed record EconomyTransactionText(string? Signature,
                                                string CreditDebitIndicator,
                                                string? Description);

    public sealed record EconomyInput(IReadOnlyList<LedgerAccount> Accounts,
                                      IReadOnlyList<LedgerEntry> Entries,
                                      IReadOnlyDictionary<long, EconomyTransactionText> Texts,
                                      IReadOnlyList<EconomyArrangement> Arrangements,
                                      long ViewerId,
                                      long PartnerId,
                                      long InviterId,
                                      Func<DateOnly, int> InviterShareOn,
                                      Func<string, DateOnly, bool> IsJoint,
                                      DateOnly From,
                                      DateOnly To);

    public sealed record SharedItem(long TransactionId,
                                    DateOnly Date,
                                    string? Description,
                                    long AmountMinor,
                                    string Payer,
                                    string Reason,
                                    long? ArrangementId,
                                    string? ArrangementLabel,
                                    string PhysicalKey,
                                    long ViewerShareMinor,
                                    long PartnerShareMinor,
                                    int ViewerShareBasisPoints);

    public sealed record FundingItem(long TransactionId,
                                     DateOnly Date,
                                     long UserId,
                                     long AmountMinor,
                                     string PhysicalKey);

    public sealed record SettlementItem(long TransactionId,
                                        DateOnly Date,
                                        long FromUserId,
                                        long AmountMinor);

    public sealed class EconomyResult
    {
        public long TotalShared { get; init; }
        public long PaidJoint { get; init; }
        public long UnattributedJointIncome { get; init; }
        public Dictionary<long, long> Share { get; init; } = new();
        public Dictionary<long, long> PaidPrivately { get; init; } = new();
        public Dictionary<long, long> Funding { get; init; } = new();
        public Dictionary<long, long> JointShare { get; init; } = new();
        public Dictionary<long, long> SettlementsPaid { get; init; } = new();
        public long OwedToViewer { get; init; }
        public IReadOnlyList<SharedItem> Items { get; init; } = Array.Empty<SharedItem>();
        public IReadOnlyList<FundingItem> FundingItems { get; init; } = Array.Empty<FundingItem>();
        public IReadOnlyList<SettlementItem> Settlements { get; init; } = Array.Empty<SettlementItem>();
        public Dictionary<long, long> PrivateCardSpending { get; init; } = new();
    }

    public static class EconomyEngine
    {
        public const string JointPayer = "JOINT";

        public static long ToMinor(decimal amount)
        {
            return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        }

        public static long ShareOf(long amountMinor,
                                   int basisPoints)
        {
            long magnitude = (Math.Abs(amountMinor) * basisPoints + 5000) / 10000;

            return amountMinor < 0 ? -magnitude : magnitude;
        }

        public static EconomyResult Compute(EconomyInput input)
        {
            Dictionary<string, long[]> owners = input.Accounts.GroupBy(account => account.PhysicalKey)
                                                              .ToDictionary(group => group.Key,
                                                                            group => group.Select(account => account.OwnerUserId).Distinct().ToArray());

            long? PrivateOwner(string? key) => key != null && owners.TryGetValue(key, out long[]? ids) && ids.Length == 1 ? ids[0] : null;

            long viewer = input.ViewerId;
            long partner = input.PartnerId;

            List<SharedItem> items = new();
            List<FundingItem> funding = new();
            List<SettlementItem> settlements = new();
            Dictionary<long, long> privateCard = new() { [viewer] = 0, [partner] = 0 };
            long unattributed = 0;

            foreach (LedgerEntry entry in input.Entries.Where(entry => entry.Date >= input.From && entry.Date <= input.To && entry.Counted))
            {
                long amount = ToMinor(entry.Amount);
                bool joint = input.IsJoint(entry.PhysicalKey, entry.Date);
                input.Texts.TryGetValue(entry.TransactionId, out EconomyTransactionText? text);

                if (joint)
                {
                    bool otherJoint = entry.CounterPhysicalKey != null && input.IsJoint(entry.CounterPhysicalKey, entry.Date);
                    long? person = otherJoint ? null : PrivateOwner(entry.CounterPhysicalKey);

                    if (entry.Type == TransactionTypes.Expense &&
                        entry.Subtype is Subtypes.CardPurchase or Subtypes.Refund &&
                        person != null &&
                        (person == viewer || person == partner))
                    {
                        privateCard[person.Value] += amount;
                        continue;
                    }

                    if (person != null && (person == viewer || person == partner))
                        funding.Add(new FundingItem(entry.TransactionId, entry.Date, person.Value, amount, entry.PhysicalKey));
                    else if (entry.Type == TransactionTypes.Income && amount > 0)
                        unattributed += amount;

                    if (entry.Type == TransactionTypes.Expense && entry.CountedAmount != 0)
                        items.Add(Item(entry, text, -ToMinor(entry.CountedAmount), JointPayer, SharedReasons.SharedAccount, null, input));

                    continue;
                }

                long? ownerId = PrivateOwner(entry.PhysicalKey);

                if (ownerId == null || (ownerId != viewer && ownerId != partner)) continue;

                if (entry.Subtype == Subtypes.Settlement && amount < 0)
                {
                    long? receiver = PrivateOwner(entry.CounterPhysicalKey);

                    if (receiver != null && receiver != ownerId && (receiver == viewer || receiver == partner))
                        settlements.Add(new SettlementItem(entry.TransactionId, entry.Date, ownerId.Value, -amount));

                    continue;
                }

                if (entry.Type != TransactionTypes.Expense || entry.CountedAmount == 0 || text?.Signature == null) continue;

                EconomyArrangement? arrangement = input.Arrangements.FirstOrDefault(candidate => candidate.PayerId == ownerId &&
                                                                                                candidate.PhysicalKey == entry.PhysicalKey &&
                                                                                                candidate.Signature == text.Signature &&
                                                                                                candidate.CreditDebitIndicator == text.CreditDebitIndicator &&
                                                                                                entry.Date >= candidate.ValidFrom &&
                                                                                                (candidate.ValidTo == null || entry.Date <= candidate.ValidTo));

                if (arrangement != null)
                    items.Add(Item(entry, text, -ToMinor(entry.CountedAmount), ownerId == viewer ? "VIEWER" : "PARTNER", SharedReasons.Arrangement, arrangement, input));
            }

            Dictionary<long, long> share = new() { [viewer] = items.Sum(item => item.ViewerShareMinor), [partner] = items.Sum(item => item.PartnerShareMinor) };
            Dictionary<long, long> paid = new()
            {
                [viewer] = items.Where(item => item.Payer == "VIEWER").Sum(item => item.AmountMinor),
                [partner] = items.Where(item => item.Payer == "PARTNER").Sum(item => item.AmountMinor)
            };
            Dictionary<long, long> jointShare = new()
            {
                [viewer] = items.Where(item => item.Payer == JointPayer).Sum(item => item.ViewerShareMinor),
                [partner] = items.Where(item => item.Payer == JointPayer).Sum(item => item.PartnerShareMinor)
            };
            Dictionary<long, long> funded = new()
            {
                [viewer] = funding.Where(item => item.UserId == viewer).Sum(item => item.AmountMinor),
                [partner] = funding.Where(item => item.UserId == partner).Sum(item => item.AmountMinor)
            };
            Dictionary<long, long> settled = new()
            {
                [viewer] = settlements.Where(item => item.FromUserId == viewer).Sum(item => item.AmountMinor),
                [partner] = settlements.Where(item => item.FromUserId == partner).Sum(item => item.AmountMinor)
            };

            long owedToViewer = items.Where(item => item.Payer == "VIEWER").Sum(item => item.PartnerShareMinor)
                              - items.Where(item => item.Payer == "PARTNER").Sum(item => item.ViewerShareMinor)
                              - settled[partner]
                              + settled[viewer];

            return new EconomyResult
            {
                TotalShared = items.Sum(item => item.AmountMinor),
                PaidJoint = items.Where(item => item.Payer == JointPayer).Sum(item => item.AmountMinor),
                UnattributedJointIncome = unattributed,
                Share = share,
                PaidPrivately = paid,
                Funding = funded,
                JointShare = jointShare,
                SettlementsPaid = settled,
                OwedToViewer = owedToViewer,
                Items = items.OrderBy(item => item.Date).ThenBy(item => item.TransactionId).ToArray(),
                PrivateCardSpending = privateCard,
                FundingItems = funding,
                Settlements = settlements
            };
        }

        private static SharedItem Item(LedgerEntry entry,
                                       EconomyTransactionText? text,
                                       long amountMinor,
                                       string payer,
                                       string reason,
                                       EconomyArrangement? arrangement,
                                       EconomyInput input)
        {
            int inviterShare = input.InviterShareOn(entry.Date);
            int viewerBasisPoints = input.ViewerId == input.InviterId ? inviterShare : 10000 - inviterShare;
            long viewerShare = ShareOf(amountMinor, viewerBasisPoints);

            return new SharedItem(entry.TransactionId,
                                  entry.Date,
                                  text?.Description,
                                  amountMinor,
                                  payer,
                                  reason,
                                  arrangement?.Id,
                                  arrangement?.Label,
                                  entry.PhysicalKey,
                                  viewerShare,
                                  amountMinor - viewerShare,
                                  viewerBasisPoints);
        }
    }
}
