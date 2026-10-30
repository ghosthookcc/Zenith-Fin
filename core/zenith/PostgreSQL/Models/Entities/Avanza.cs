namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class AvanzaStatusRow
    {
        public DateTime? UpdatedAt { get; set; }
        public string? KeyFingerprint { get; set; }
        public string? Status { get; set; }
        public string? ErrorClass { get; set; }
        public DateTime? LastAttemptAt { get; set; }
        public DateTime? LastSuccessAt { get; set; }
    }

    public sealed class AvanzaSnapshotRow
    {
        public string AccountId { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string? AccountType { get; set; }
        public decimal TotalValue { get; set; }
        public string Currency { get; set; } = "";
        public DateTime FetchedAt { get; set; }
    }
}
