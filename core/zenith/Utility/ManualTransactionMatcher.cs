using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.Utility
{
    public static class ManualTransactionMatcher
    {
        public const int MaxDayDistance = 5;

        public static Dictionary<long, long> Match(IReadOnlyCollection<ManualTransactionDto.ManualTransaction> manualTransactions,
                                                   IReadOnlyCollection<ManualMatchCandidate> candidates)
        {
            Dictionary<long, ManualTransactionDto.ManualTransaction> manualById = manualTransactions.ToDictionary(transaction => transaction.Id);
            Dictionary<long, long> matches = new();
            HashSet<long> claimed = new();

            IEnumerable<ManualMatchCandidate> ranked = candidates.Where(candidate => manualById.ContainsKey(candidate.ManualTransactionId))
                                                                 .OrderByDescending(candidate => SameCounterparty(manualById[candidate.ManualTransactionId], candidate))
                                                                 .ThenBy(candidate => candidate.DayDistance)
                                                                 .ThenBy(candidate => candidate.ManualTransactionId)
                                                                 .ThenBy(candidate => candidate.TransactionId);

            foreach (ManualMatchCandidate candidate in ranked)
            {
                if (matches.ContainsKey(candidate.ManualTransactionId) || !claimed.Add(candidate.TransactionId)) continue;

                matches[candidate.ManualTransactionId] = candidate.TransactionId;
            }

            return matches;
        }

        private static bool SameCounterparty(ManualTransactionDto.ManualTransaction manualTransaction,
                                             ManualMatchCandidate candidate)
        {
            string? otherSide = candidate.FromSide ? manualTransaction.ToAccountNumber : manualTransaction.FromAccountNumber;

            if (string.IsNullOrWhiteSpace(otherSide)) return false;

            string[] keys = VirtualAccountMatcher.Keys(otherSide, null, char.IsAsciiLetter(otherSide.TrimStart()[0]) ? otherSide : null);

            return VirtualAccountMatcher.Matches(candidate.CounterpartyAccount, keys);
        }
    }
}
