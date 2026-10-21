namespace ZenithFin.Api.Models.Dtos
{
    public static class EconomyDto
    {
        public sealed class SharedView
        {
            public decimal Total { get; set; }
            public decimal MyShare { get; set; }
            public decimal PartnerShare { get; set; }
            public decimal PaidFromShared { get; set; }
            public decimal PaidByMe { get; set; }
            public decimal PaidByPartner { get; set; }
            public decimal MyFunding { get; set; }
            public decimal PartnerFunding { get; set; }
            public decimal MyShareOfSharedAccounts { get; set; }
            public decimal PartnerShareOfSharedAccounts { get; set; }
            public decimal UnattributedIncome { get; set; }
            public decimal SettledByMe { get; set; }
            public decimal SettledByPartner { get; set; }
            public decimal OwedToMe { get; set; }
        }

        public sealed class PrivateView
        {
            public decimal Income { get; set; }
            public decimal Spending { get; set; }
            public decimal MyShareOfShared { get; set; }
            public decimal Balance { get; set; }
        }

        public sealed class CombinedView
        {
            public decimal Income { get; set; }
            public decimal Spending { get; set; }
            public decimal Net { get; set; }
        }

        public sealed class SharedTransaction
        {
            public long Id { get; set; }
            public string Date { get; set; } = string.Empty;
            public string? Description { get; set; }
            public decimal Amount { get; set; }
            public string PaidBy { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public decimal MyShare { get; set; }
            public decimal PartnerShare { get; set; }
            public decimal MySharePercent { get; set; }
        }

        public sealed class Arrangement
        {
            public long Id { get; set; }
            public string Label { get; set; } = string.Empty;
            public string AccountName { get; set; } = string.Empty;
            public string ValidFrom { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public bool PaidByMe { get; set; }
            public bool ProposedByMe { get; set; }
        }

        public sealed class JointAccount
        {
            public string Name { get; set; } = string.Empty;
            public decimal? Balance { get; set; }
        }

        public sealed class Summary
        {
            public string From { get; set; } = string.Empty;
            public string To { get; set; } = string.Empty;
            public string? PartnerName { get; set; }
            public decimal? MySharePercent { get; set; }
            public SharedView Shared { get; set; } = new();
            public PrivateView Private { get; set; } = new();
            public CombinedView Combined { get; set; } = new();
            public IReadOnlyList<SharedTransaction> Transactions { get; set; } = Array.Empty<SharedTransaction>();
            public IReadOnlyList<Arrangement> Arrangements { get; set; } = Array.Empty<Arrangement>();
            public IReadOnlyList<JointAccount> JointAccounts { get; set; } = Array.Empty<JointAccount>();
        }

        public sealed class SharedStatus
        {
            public string Status { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public decimal? MyShare { get; set; }
            public decimal? PartnerShare { get; set; }
            public string? PaidBy { get; set; }
        }

        public sealed class ProposeArrangementRequest
        {
            public long TransactionId { get; set; }
        }
    }
}
