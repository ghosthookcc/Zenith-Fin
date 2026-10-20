namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class VirtualBalanceTransactionRow
    {
        public long Id { get; set; }
        public string CounterpartyAccount { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime FirstSeenAt { get; set; }
    }
}
