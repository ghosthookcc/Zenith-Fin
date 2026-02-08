namespace ZenithFin.PostgreSQL.Models.Dtos
{
    public sealed class SessionCreated
    {
        public Guid SessionId { get; set; }
        public long UserId { get; set; }
        public string RawJwtSecret { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
    }

    public sealed class ActiveSession
    {
        public string JwtSecretEncrypted { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
    }
}
