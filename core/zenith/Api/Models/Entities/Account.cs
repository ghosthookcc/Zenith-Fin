using System.Text.Json.Serialization;

using ZenithFin.EnableBanking;

namespace ZenithFin.Api.Models.Entities
{
    public static class AccountEntity
    {
        public sealed class BalanceExpanded
        {
            public string BankName { get; set; } = string.Empty;
            public string? Iban { get; set; }
            public string? Name { get; set; }
            public string Currency { get; set; } = string.Empty;
            public IReadOnlyList<EnableBankingEntities.Balance> Balances { get; set; } = Array.Empty<EnableBankingEntities.Balance>();
        }
    }
}