using ZenithFin.EnableBanking;

namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed record CachedAccountBalances(DateTime FetchedAt, IReadOnlyList<EnableBankingEntities.Balance> Balances);
    public sealed class CachedBalanceRow
    {
        public DateTime? FetchedAt { get; set; }
        public long? BalanceId { get; set; }
        public string? Name { get; set; }
        public string Currency { get; set; } = "";
        public string Amount { get; set; } = "";
        public string BalanceType { get; set; } = "";
        public DateTime? LastChangeDateTime { get; set; }
        public string? ReferenceDate { get; set; }
        public string? LastCommittedTransaction { get; set; }
    }
}