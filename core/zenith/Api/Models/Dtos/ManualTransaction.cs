namespace ZenithFin.Api.Models.Dtos
{
    public static class ManualTransactionDto
    {
        public static class AccountKind
        {
            public const string Connected = "connected";
            public const string Virtual = "virtual";
        }

        public sealed class ManualTransaction
        {
            public long Id { get; set; }
            public string Date { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public string Currency { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string Counting { get; set; } = string.Empty;

            public long? FromAccountId { get; set; }
            public long? FromVirtualAccountId { get; set; }
            public string? FromAccountNumber { get; set; }
            public string? FromName { get; set; }

            public long? ToAccountId { get; set; }
            public long? ToVirtualAccountId { get; set; }
            public string? ToAccountNumber { get; set; }
            public string? ToName { get; set; }

            public DateTime CreatedAt { get; set; }
            public long? MatchedTransactionId { get; set; }
        }

        public sealed class CreateManualTransactionRequest
        {
            public string? Date { get; set; }
            public decimal? Amount { get; set; }
            public string? Description { get; set; }
            public string? Counting { get; set; }

            public long? FromAccountId { get; set; }
            public long? FromVirtualAccountId { get; set; }
            public string? FromAccountNumber { get; set; }

            public long? ToAccountId { get; set; }
            public long? ToVirtualAccountId { get; set; }
            public string? ToAccountNumber { get; set; }
            public string? ToName { get; set; }
        }

        public sealed class LinkableAccount
        {
            public long Id { get; set; }
            public string Kind { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? BankName { get; set; }
            public string? AccountNumber { get; set; }
            public string Currency { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }

        public sealed class LinkableAccountsResponse
        {
            public IReadOnlyList<LinkableAccount> Accounts { get; set; } = Array.Empty<LinkableAccount>();
            public IReadOnlyList<LinkableAccount> VirtualAccounts { get; set; } = Array.Empty<LinkableAccount>();
        }
    }
}
