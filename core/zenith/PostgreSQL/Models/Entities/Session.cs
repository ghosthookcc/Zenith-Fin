namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class SessionEntity
    {
        public Guid SessionId { get; set; }
        public long UserId { get; set; }
        public string JwtSecretEncrypted { get; set; } = null!;
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
