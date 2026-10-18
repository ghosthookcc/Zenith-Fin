namespace ZenithFin.Categorization
{
    public sealed record TransferLeg(long Id,
                                     long AccountId,
                                     DateOnly OccurredOn,
                                     decimal Amount,
                                     string Currency,
                                     string CreditDebitIndicator,
                                     string? BankTransactionCode,
                                     string Status,
                                     bool OwnTransfer);

    public static class TransferPairs
    {
        private const int MaxDaysApart = 4;

        private static readonly string[] VoidStatuses = { "CNCL", "RJCT" };

        private static readonly KeywordPattern[] ExcludedCodes = new[] { "SWISH", "KORTKOP", "RANTA", "SKATT", "AVGIFT" }
                                                                 .Select(code => new KeywordPattern(code))
                                                                 .ToArray();

        public static HashSet<long> Find(IReadOnlyList<TransferLeg> legs)
        {
            HashSet<long> paired = new();

            foreach (var group in legs.Where(IsEligible).GroupBy(leg => (leg.Currency, Amount: Math.Abs(leg.Amount))))
            {
                TransferLeg[] incoming = group.Where(leg => leg.CreditDebitIndicator == "CRDT").ToArray();

                var candidates = group.Where(leg => leg.CreditDebitIndicator == "DBIT")
                                      .SelectMany(outgoing => incoming.Where(leg => leg.AccountId != outgoing.AccountId &&
                                                                                   (outgoing.OwnTransfer || leg.OwnTransfer))
                                                                      .Select(leg => (Outgoing: outgoing,
                                                                                      Incoming: leg,
                                                                                      Days: Math.Abs(outgoing.OccurredOn.DayNumber - leg.OccurredOn.DayNumber))))
                                      .Where(pair => pair.Days <= MaxDaysApart)
                                      .OrderBy(pair => pair.Days)
                                      .ThenByDescending(pair => pair.Outgoing.OwnTransfer && pair.Incoming.OwnTransfer)
                                      .ThenBy(pair => pair.Outgoing.Id)
                                      .ThenBy(pair => pair.Incoming.Id);

                foreach ((TransferLeg outgoing, TransferLeg leg, int _) in candidates)
                {
                    if (paired.Contains(outgoing.Id) || paired.Contains(leg.Id)) continue;

                    paired.Add(outgoing.Id);
                    paired.Add(leg.Id);
                }
            }

            return paired;
        }

        private static bool IsEligible(TransferLeg leg)
        {
            if (leg.Amount == 0 || VoidStatuses.Contains(leg.Status)) return false;

            string[] code = TextTokens.Tokenize(leg.BankTransactionCode);

            return !ExcludedCodes.Any(pattern => pattern.Matches(code));
        }
    }
}
