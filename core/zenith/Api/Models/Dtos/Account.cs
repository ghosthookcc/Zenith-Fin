using System.Text.Json.Serialization;

using ZenithFin.EnableBanking;

namespace ZenithFin.Api.Models.Dtos
{
    public static class AccountDto
    {
        public sealed class Account
        {
            public long Id { get; set; }
            public Guid EnableBankingUid { get; set; }
            public string? Iban { get; set; }
            public string? Name { get; set; }
            public string Currency { get; set; } = string.Empty;
            public long BankConnectionId { get; set; }
        }
        
        public sealed class Balance
        {
            public long Id { get; set; }
            public Guid EnableBankingUid { get; set; }
            public string? Iban { get; set; }
            public string? Name { get; set; }
            public string Currency { get; set; } = string.Empty;
            public IReadOnlyList<EnableBankingEntities.Balance> Balances { get; set; } = Array.Empty<EnableBankingEntities.Balance>();
        }
    }
}