namespace ZenithFin.Api.Models.Dtos
{
    public static class VirtualAccountDto
    {
        public sealed class VirtualAccount
        {
            public long Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string AccountNumber { get; set; } = string.Empty;
            public string? ClearingNumber { get; set; }
            public string? Iban { get; set; }
            public string Counting { get; set; } = string.Empty;
            public decimal? Balance { get; set; }
            public DateTime? BalanceSetAt { get; set; }
            public decimal? CurrentBalance { get; set; }
        }

        public sealed class SaveVirtualAccountRequest
        {
            public string? Name { get; set; }
            public string? AccountNumber { get; set; }
            public string? ClearingNumber { get; set; }
            public string? Iban { get; set; }
            public string? Counting { get; set; }
            public decimal? Balance { get; set; }
            public bool SetBalance { get; set; }
        }

        public sealed class VirtualAccountsResponse
        {
            public IReadOnlyList<VirtualAccount> VirtualAccounts { get; set; } = Array.Empty<VirtualAccount>();
        }
    }
}
