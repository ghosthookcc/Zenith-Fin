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
            public string? StoredRole { get; set; }
            public string? BankAccountType { get; set; }
            public string Role => StoredRole ?? Ledger.AccountRoles.FromBankType(BankAccountType);
            public string RoleSource => StoredRole != null ? "USER" : BankAccountType != null ? "BANK" : "DEFAULT";
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
            public string Role { get; set; } = string.Empty;
            public string RoleSource { get; set; } = string.Empty;
            public IReadOnlyList<EnableBankingEntities.Balance> Balances { get; set; } = Array.Empty<EnableBankingEntities.Balance>();
        }

        public sealed class RenameAccountRequest
        {
            public string? Alias { get; set; }
        }

        public sealed class SetAccountRoleRequest
        {
            public string? Role { get; set; }
        }

        public sealed class AccountAlias
        {
            public long AccountId { get; set; }
            public string? Alias { get; set; }
        }
    }
}