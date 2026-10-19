namespace ZenithFin.Api.Models.Dtos
{
    public static class TransactionDto
    {
        public sealed class Transaction
        {
            public long Id { get; set; }
            public long AccountId { get; set; }

            public string Date { get; set; } = string.Empty;
            public string? BookingDate { get; set; }
            public string? ValueDate { get; set; }
            public string? TransactionDate { get; set; }

            public decimal Amount { get; set; }
            public string Currency { get; set; } = string.Empty;
            public decimal? OriginalAmount { get; set; }
            public string? OriginalCurrency { get; set; }

            public string Status { get; set; } = string.Empty;
            public string CreditDebitIndicator { get; set; } = string.Empty;

            public string? Description { get; set; }
            public string? CounterpartyName { get; set; }
            public string? CounterpartyAccount { get; set; }
            public string[] RemittanceInformation { get; set; } = Array.Empty<string>();
            public string? MerchantCategoryCode { get; set; }
            public string? BankTransactionCode { get; set; }
            public string? ReferenceNumber { get; set; }
            public string? Note { get; set; }

            public long? TagId { get; set; }
            public string? TagSource { get; set; }
            public string? Counting { get; set; }

            public RecurringInfo? Recurring { get; set; }
        }

        public sealed class RecurringInfo
        {
            public string Frequency { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
            public int Occurrences { get; set; }
            public string Next { get; set; } = string.Empty;
            public bool Manual { get; set; }
        }

        public sealed class SetRecurringRequest
        {
            public bool? Recurring { get; set; }
        }

        public sealed class SetCountingRequest
        {
            public string? Counting { get; set; }
        }

        public sealed class CorrectTagRequest
        {
            public long? TagId { get; set; }
            public bool Remember { get; set; }
        }

        public sealed class TransactionTag
        {
            public long Id { get; set; }
            public long? TagId { get; set; }
            public string? TagSource { get; set; }
        }

        public sealed class CorrectTagResponse
        {
            public IReadOnlyList<TransactionTag> Transactions { get; set; } = Array.Empty<TransactionTag>();
            public bool Remembered { get; set; }
            public int Moved { get; set; }
        }

        public sealed class Account
        {
            public long Id { get; set; }
            public string? Name { get; set; }
            public string BankName { get; set; } = string.Empty;
            public string? Iban { get; set; }
            public string Currency { get; set; } = string.Empty;
            public bool IsActive { get; set; }
            public DateTime? TransactionsSyncedAt { get; set; }
            public bool IsStale { get; set; }
        }

        public sealed class TransactionsResponse
        {
            public DateOnly From { get; set; }
            public DateOnly To { get; set; }
            public IReadOnlyList<Account> Accounts { get; set; } = Array.Empty<Account>();
            public IReadOnlyList<TagDto.Tag> Tags { get; set; } = Array.Empty<TagDto.Tag>();
            public IReadOnlyList<CreditAccountDto.CreditAccount> CreditAccounts { get; set; } = Array.Empty<CreditAccountDto.CreditAccount>();
            public IReadOnlyList<ExpectedSalaryDto.ExpectedSalary> ExpectedSalaries { get; set; } = Array.Empty<ExpectedSalaryDto.ExpectedSalary>();
            public IReadOnlyList<VirtualAccountDto.VirtualAccount> VirtualAccounts { get; set; } = Array.Empty<VirtualAccountDto.VirtualAccount>();
            public IReadOnlyList<Transaction> Transactions { get; set; } = Array.Empty<Transaction>();
        }

        public static class SyncStatus
        {
            public const string Synced = "synced";
            public const string Skipped = "skipped";
            public const string Failed = "failed";
        }

        public sealed class SyncResult
        {
            public long AccountId { get; set; }
            public string Status { get; set; } = string.Empty;
            public int Fetched { get; set; }
            public string? Error { get; set; }
            public DateTime? SyncedAt { get; set; }
        }

        public sealed class SyncResponse
        {
            public IReadOnlyList<SyncResult> Accounts { get; set; } = Array.Empty<SyncResult>();
        }
    }
}
