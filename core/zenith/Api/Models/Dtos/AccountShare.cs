namespace ZenithFin.Api.Models.Dtos
{
    public static class AccountShareDto
    {
        public sealed class AccountShare
        {
            public long Id { get; set; }
            public long AccountId { get; set; }
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public bool HasPersonalNumber { get; set; }
        }

        public sealed class SaveAccountShareRequest
        {
            public long? AccountId { get; set; }
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string? PersonalNumber { get; set; }
            public bool RemovePersonalNumber { get; set; }
        }

        public sealed class AccountSharesResponse
        {
            public IReadOnlyList<AccountShare> Shares { get; set; } = Array.Empty<AccountShare>();
        }

        public sealed class PersonalNumberResponse
        {
            public string PersonalNumber { get; set; } = string.Empty;
        }
    }
}
