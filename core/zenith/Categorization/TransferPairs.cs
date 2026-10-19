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
                                     bool OwnTransfer,
                                     bool KnownCounterparty,
                                     bool TaggedByUser);

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
                TransferLeg[] outgoing = group.Where(leg => leg.CreditDebitIndicator == "DBIT").ToArray();
                TransferLeg[] incoming = group.Where(leg => leg.CreditDebitIndicator == "CRDT").ToArray();

                PairMarked(outgoing, incoming, paired);
                PairUnmarked(outgoing, incoming, paired);
            }

            return paired;
        }

        private static void PairMarked(IReadOnlyList<TransferLeg> outgoing,
                                       IReadOnlyList<TransferLeg> incoming,
                                       HashSet<long> paired)
        {
            var candidates = outgoing.SelectMany(leg => incoming.Where(candidate => (leg.OwnTransfer || candidate.OwnTransfer) &&
                                                                                    AreClose(leg, candidate))
                                                                .Select(candidate => (Outgoing: leg,
                                                                                      Incoming: candidate,
                                                                                      Days: DaysApart(leg, candidate))))
                                     .OrderBy(pair => pair.Days)
                                     .ThenByDescending(pair => pair.Outgoing.OwnTransfer && pair.Incoming.OwnTransfer)
                                     .ThenBy(pair => pair.Outgoing.Id)
                                     .ThenBy(pair => pair.Incoming.Id);

            foreach ((TransferLeg leg, TransferLeg candidate, int _) in candidates)
            {
                if (paired.Contains(leg.Id) || paired.Contains(candidate.Id)) continue;

                paired.Add(leg.Id);
                paired.Add(candidate.Id);
            }
        }

        private static void PairUnmarked(IReadOnlyList<TransferLeg> outgoing,
                                         IReadOnlyList<TransferLeg> incoming,
                                         HashSet<long> paired)
        {
            TransferLeg[] openOutgoing = outgoing.Where(leg => IsUnmarkedCandidate(leg, paired)).ToArray();
            TransferLeg[] openIncoming = incoming.Where(leg => IsUnmarkedCandidate(leg, paired)).ToArray();

            foreach (TransferLeg leg in openOutgoing)
            {
                TransferLeg[] matches = openIncoming.Where(candidate => AreClose(leg, candidate)).ToArray();

                if (matches.Length != 1 || openOutgoing.Count(other => AreClose(other, matches[0])) != 1) continue;

                paired.Add(leg.Id);
                paired.Add(matches[0].Id);
            }
        }

        private static bool IsUnmarkedCandidate(TransferLeg leg,
                                                HashSet<long> paired)
        {
            return !paired.Contains(leg.Id) && !leg.KnownCounterparty && !leg.TaggedByUser;
        }

        private static bool AreClose(TransferLeg outgoing,
                                     TransferLeg incoming)
        {
            return outgoing.AccountId != incoming.AccountId && DaysApart(outgoing, incoming) <= MaxDaysApart;
        }

        private static int DaysApart(TransferLeg outgoing,
                                     TransferLeg incoming)
        {
            return Math.Abs(outgoing.OccurredOn.DayNumber - incoming.OccurredOn.DayNumber);
        }

        private static bool IsEligible(TransferLeg leg)
        {
            if (leg.Amount == 0 || VoidStatuses.Contains(leg.Status)) return false;

            string[] code = TextTokens.Tokenize(leg.BankTransactionCode);

            return !ExcludedCodes.Any(pattern => pattern.Matches(code));
        }
    }
}
