namespace ZenithFin.Api.Models.Dtos
{
    public static class CreditAccountDto
    {
        public sealed class CreditAccount
        {
            public long Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public long AccountId { get; set; }
            public string Counting { get; set; } = string.Empty;
            public decimal? CreditLimit { get; set; }
        }

        public sealed class SaveCreditAccountRequest
        {
            public string? Name { get; set; }
            public long? AccountId { get; set; }
            public string? Counting { get; set; }
            public decimal? CreditLimit { get; set; }
        }

        public sealed class CreditAccountsResponse
        {
            public IReadOnlyList<CreditAccount> CreditAccounts { get; set; } = Array.Empty<CreditAccount>();
        }
    }
}
