namespace ZenithFin.PostgreSQL.Models.Entities
{
    public class UserEntity
    {
        public Guid? Id { get; init; }
        public string FullName { get; init; } = null!;
        public string Email { get; init; } = null!;
        public string PasswordHash { get; init; } = null!;
        public string Phone { get; init; } = null!;
        public DateTime? CreatedAt { get; init; }
    }
}
