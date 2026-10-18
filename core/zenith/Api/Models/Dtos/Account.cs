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
            public string BankName { get; set; } = string.Empty;
            public string? Iban { get; set; }
            public string? Name { get; set; }
            public string? OriginalName { get; set; }
            public string? Alias { get; set; }
            public string Currency { get; set; } = string.Empty;
            public long BankConnectionId { get; set; }
            public bool InConsent { get; set; }
        }
        
        public sealed class Balance
        {
            public long Id { get; set; }
            public Guid EnableBankingUid { get; set; }
            public string BankName { get; set; } = string.Empty;
            public string? Iban { get; set; }
            public string? Name { get; set; }
            public string? OriginalName { get; set; }
            public string? Alias { get; set; }
            public string Currency { get; set; } = string.Empty;
            public bool InConsent { get; set; }
            public IReadOnlyList<EnableBankingEntities.Balance> Balances { get; set; } = Array.Empty<EnableBankingEntities.Balance>();
        }

        public sealed class RenameAccountRequest
        {
            public string? Alias { get; set; }
        }

        public sealed class AccountAlias
        {
            public long AccountId { get; set; }
            public string? Alias { get; set; }
        }
    }
}