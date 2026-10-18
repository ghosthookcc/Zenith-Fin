using ZenithFin.Categorization;

namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class CategorizationCandidate
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public string OccurredOn { get; set; } = "";
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
        public string Status { get; set; } = "";
        public long? TagId { get; set; }
        public string? TagSource { get; set; }
        public string? CounterpartyName { get; set; }
        public string[] RemittanceInformation { get; set; } = Array.Empty<string>();
        public string? BankTransactionCode { get; set; }
        public string? CounterpartyAccount { get; set; }
        public string CreditDebitIndicator { get; set; } = "";
        public string? OriginalCurrency { get; set; }
    }

    public sealed record TagChange(long TransactionId,
                                   long TagId,
                                   string Source,
                                   long? PreviousTagId,
                                   string? PreviousSource);

    public sealed class TagCorrectionRow
    {
        public long Id { get; set; }
        public string CreditDebitIndicator { get; set; } = "";
        public bool OwnTransfer { get; set; }
        public string Signature { get; set; } = "";
        public string Description { get; set; } = "";
        public string Example { get; set; } = "";
        public long TagId { get; set; }
        public long? ReplacedTagId { get; set; }
        public DateTime UpdatedAt { get; set; }

        public CorrectionKey Key => new(CreditDebitIndicator, OwnTransfer, Signature);
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
