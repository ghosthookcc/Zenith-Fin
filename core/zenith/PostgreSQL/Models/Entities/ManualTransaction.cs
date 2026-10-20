namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class ManualMatchCandidate
    {
        public long ManualTransactionId { get; set; }
        public long TransactionId { get; set; }
        public int DayDistance { get; set; }
        public string? CounterpartyAccount { get; set; }
        public bool FromSide { get; set; }
    }
}
