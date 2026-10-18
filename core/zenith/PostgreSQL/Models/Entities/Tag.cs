namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class CategorizationCandidate
    {
        public long Id { get; set; }
        public long? TagId { get; set; }
        public string? TagSource { get; set; }
        public string? CounterpartyName { get; set; }
        public string[] RemittanceInformation { get; set; } = Array.Empty<string>();
        public string? BankTransactionCode { get; set; }
        public string? CounterpartyAccount { get; set; }
        public string CreditDebitIndicator { get; set; } = "";
        public string? OriginalCurrency { get; set; }
    }

    public sealed class AiCandidateRow
    {
        public long Id { get; set; }
        public string? CounterpartyName { get; set; }
        public string[] RemittanceInformation { get; set; } = Array.Empty<string>();
        public string? BankTransactionCode { get; set; }
        public string CreditDebitIndicator { get; set; } = "";
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
    }
}
