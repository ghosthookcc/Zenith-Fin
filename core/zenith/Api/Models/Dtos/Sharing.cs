namespace ZenithFin.Api.Models.Dtos
{
    public static class SharingDto
    {
        public sealed class Person
        {
            public long Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public sealed class Partnership
        {
            public long Id { get; set; }
            public string Status { get; set; } = string.Empty;
            public Person Partner { get; set; } = new();
            public bool SentByMe { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? RespondedAt { get; set; }
        }

        public sealed class SplitRule
        {
            public long Id { get; set; }
            public decimal MyShare { get; set; }
            public decimal PartnerShare { get; set; }
            public string ValidFrom { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public bool ProposedByMe { get; set; }
        }

        public sealed class ShareableAccount
        {
            public long AccountId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string BankName { get; set; } = string.Empty;
            public string? Iban { get; set; }
            public long? SharingId { get; set; }
            public string? Status { get; set; }
            public bool PartnerHasIt { get; set; }
        }

        public sealed class SharedAccount
        {
            public long SharingId { get; set; }
            public long AccountId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string BankName { get; set; } = string.Empty;
            public string? Iban { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        public sealed class InboxItem
        {
            public string Kind { get; set; } = string.Empty;
            public long Id { get; set; }
            public string Text { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
        }

        public sealed class Notification
        {
            public long Id { get; set; }
            public string Text { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public bool Read { get; set; }
        }

        public sealed class State
        {
            public Person Me { get; set; } = new();
            public Partnership? Partnership { get; set; }
            public IReadOnlyList<SplitRule> SplitRules { get; set; } = Array.Empty<SplitRule>();
            public IReadOnlyList<ShareableAccount> MyAccounts { get; set; } = Array.Empty<ShareableAccount>();
            public IReadOnlyList<SharedAccount> PartnerAccounts { get; set; } = Array.Empty<SharedAccount>();
            public IReadOnlyList<InboxItem> Inbox { get; set; } = Array.Empty<InboxItem>();
            public IReadOnlyList<Notification> Notifications { get; set; } = Array.Empty<Notification>();
            public int Unread { get; set; }
        }

        public sealed class InvitePartnerRequest
        {
            public string? Email { get; set; }
            public decimal? MyShare { get; set; }
        }

        public sealed class RespondRequest
        {
            public bool Accept { get; set; }
        }

        public sealed class ProposeSplitRequest
        {
            public decimal? MyShare { get; set; }
            public string? ValidFrom { get; set; }
        }

        public sealed class InboxCount
        {
            public int Count { get; set; }
        }
    }
}
