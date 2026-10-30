namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class ExportAccountRow
    {
        public long Id { get; set; }
        public string? IdentificationHash { get; set; }
        public string? Iban { get; set; }
        public string ConnectionStatus { get; set; } = "";
        public DateTime ConsentExpiresAt { get; set; }
        public DateTime? BalancesFetchedAt { get; set; }
        public DateTime? TransactionsSyncedAt { get; set; }
        public string? FirstTransactionOn { get; set; }
    }
}
