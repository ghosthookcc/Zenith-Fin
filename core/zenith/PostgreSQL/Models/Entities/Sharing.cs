namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class UserNameRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class PartnershipRow
    {
        public long Id { get; set; }
        public long InviterId { get; set; }
        public long InviteeId { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? RespondedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public long PartnerOf(long userId) => userId == InviterId ? InviteeId : InviterId;
    }

    public sealed class PendingSplitRow
    {
        public long PartnershipId { get; set; }
        public long ProposedBy { get; set; }
        public string ValidFrom { get; set; } = "";
    }

    public sealed class SplitRuleRow
    {
        public long Id { get; set; }
        public int InviterShare { get; set; }
        public string ValidFrom { get; set; } = "";
        public string Status { get; set; } = "";
        public long ProposedBy { get; set; }
    }

    public sealed class AccountSharingRow
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public long OwnerId { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public string Name { get; set; } = "";
        public string BankName { get; set; } = "";
        public string? Iban { get; set; }
    }

    public sealed class NotificationRow
    {
        public long Id { get; set; }
        public string Text { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public bool Read { get; set; }
    }
}
