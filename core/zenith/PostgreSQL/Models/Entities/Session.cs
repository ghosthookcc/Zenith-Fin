namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class SessionEntity
    {
        public required Guid SessionId { get; set; }
        public required long UserId { get; set; }
        public required string JwtSecretEncrypted { get; set; } = null!;
        public required DateTime IssuedAt { get; set; }
        public required DateTime ExpiresAt { get; set; }
    }
}
