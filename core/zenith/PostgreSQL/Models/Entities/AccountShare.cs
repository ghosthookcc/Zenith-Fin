namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class AccountShareRow
    {
        public long Id { get; set; }
        public long? AccountId { get; set; }
        public long? VirtualAccountId { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string? PersonalNumberEncrypted { get; set; }
    }
}
