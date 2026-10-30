namespace ZenithFin.Api.Models.Dtos
{
    public static class AvanzaDto
    {
        public sealed class SaveCredentialsRequest
        {
            public string? Username { get; set; }
            public string? Password { get; set; }
            public string? TotpSecret { get; set; }
        }

        public sealed class SyncStatus
        {
            public bool Configured { get; set; }
            public bool Exists { get; set; }
            public bool KeyMatches { get; set; }
            public DateTime? UpdatedAt { get; set; }
            public DateTime? LastAttemptAt { get; set; }
            public DateTime? LastSuccessAt { get; set; }
            public string? Status { get; set; }
            public string? ErrorClass { get; set; }
        }

        public sealed class Account
        {
            public string AccountId { get; set; } = string.Empty;
            public string AccountName { get; set; } = string.Empty;
            public string? AccountType { get; set; }
            public decimal TotalValue { get; set; }
            public string Currency { get; set; } = string.Empty;
            public DateTime FetchedAt { get; set; }
        }

        public sealed class HistoryPoint
        {
            public string AccountId { get; set; } = string.Empty;
            public string AccountName { get; set; } = string.Empty;
            public DateTime FetchedAt { get; set; }
            public decimal TotalValue { get; set; }
        }

        public sealed class Balances
        {
            public DateTime? FetchedAt { get; set; }
            public IReadOnlyList<Account> Accounts { get; set; } = Array.Empty<Account>();
            public IReadOnlyList<HistoryPoint> History { get; set; } = Array.Empty<HistoryPoint>();
        }
    }
}
