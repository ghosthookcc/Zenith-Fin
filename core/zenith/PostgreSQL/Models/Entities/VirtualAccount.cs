namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class VirtualBalanceTransactionRow
    {
        public string CounterpartyAccount { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime FirstSeenAt { get; set; }
    }
}
