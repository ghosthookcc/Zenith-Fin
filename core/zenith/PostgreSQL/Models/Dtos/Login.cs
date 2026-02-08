namespace ZenithFin.PostgreSQL.Models.Dtos
{
    public sealed class LoginAttempt
    {
        public long UserId { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
    }
}
