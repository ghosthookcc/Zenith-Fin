namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class LedgerAccountRow
    {
        public long Id { get; set; }
        public long OwnerUserId { get; set; }
        public string? IdentificationHash { get; set; }
        public string? Iban { get; set; }
        public string Name { get; set; } = "";
        public string Currency { get; set; } = "";
        public string? Role { get; set; }
        public string? BankAccountType { get; set; }
        public long? CreditAccountId { get; set; }
        public string? CreditName { get; set; }
        public string? CreditMode { get; set; }
    }

    public sealed class LedgerTransactionRow
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public string Date { get; set; } = "";
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
        public string Status { get; set; } = "";
        public string? CounterpartyName { get; set; }
        public string? CounterpartyAccount { get; set; }
        public string? BankTransactionCode { get; set; }
        public string ExternalId { get; set; } = "";
        public string? TagKind { get; set; }
        public string? TagKey { get; set; }
        public string? TagName { get; set; }
        public string? TagSource { get; set; }
        public string? CountingOverride { get; set; }
        public string? TypeOverride { get; set; }
    }
}
