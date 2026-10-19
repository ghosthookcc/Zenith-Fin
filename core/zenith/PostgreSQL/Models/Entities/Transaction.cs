namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class TransactionAccountRow
    {
        public long Id { get; set; }
        public Guid EnableBankingUid { get; set; }
        public string? Name { get; set; }
        public string? Iban { get; set; }
        public string Currency { get; set; } = "";
        public long BankConnectionId { get; set; }
        public string BankName { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime? TransactionsSyncedAt { get; set; }
    }

    public sealed class ReleasedTransactionRow
    {
        public long? TagId { get; set; }
        public string? TagSource { get; set; }
        public string? Counting { get; set; }
        public string CreditDebitIndicator { get; set; } = "";
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
        public string? CounterpartyName { get; set; }
        public string[] RemittanceInformation { get; set; } = Array.Empty<string>();
        public string OccurredOn { get; set; } = "";
    }

    public sealed record TransactionEntity
    {
        public string ExternalId { get; init; } = "";
        public string? EntryReference { get; init; }

        public string Status { get; init; } = "";
        public string CreditDebitIndicator { get; init; } = "";

        public decimal Amount { get; init; }
        public string Currency { get; init; } = "";
        public decimal? OriginalAmount { get; init; }
        public string? OriginalCurrency { get; init; }

        public DateOnly? BookingDate { get; init; }
        public DateOnly? ValueDate { get; init; }
        public DateOnly? TransactionDate { get; init; }
        public DateOnly OccurredOn { get; init; }

        public string? CounterpartyName { get; init; }
        public string? CounterpartyAccount { get; init; }
        public string[] RemittanceInformation { get; init; } = Array.Empty<string>();
        public string? MerchantCategoryCode { get; init; }
        public string? BankTransactionCode { get; init; }
        public string? ReferenceNumber { get; init; }
        public string? Note { get; init; }

        public string Raw { get; init; } = "{}";
    }
}
