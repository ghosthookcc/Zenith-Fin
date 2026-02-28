namespace ZenithFin.PostgreSQL.Models.Dtos
{
    public sealed class SessionCreated
    {
        public Guid SessionId { get; set; }
        public long UserId { get; set; }
        public string RawJwtSecret { get; set; } = null!;
        public DateTimeOffset ExpiresAt { get; set; }
    }

    public sealed class ActiveSession
    {
        public string JwtSecretEncrypted { get; set; } = null!;
        public DateTimeOffset ExpiresAt { get; set; }
    }

    public sealed class PendingBankSession
    {
        public string AspspName { get; set; } = null!;
        public string AspspCountry { get; set; } = null!;
        public string PsuType { get; set; } = null!;
        public DateTimeOffset AuthExpiresAt { get; set; }
    }
}
