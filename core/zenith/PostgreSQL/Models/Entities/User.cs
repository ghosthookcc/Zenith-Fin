namespace ZenithFin.PostgreSQL.Models.Entities
{
    public class UserEntity
    {
        public Guid? Id { get; init; }
        public required string FullName { get; init; } = null!;
        public required string Email { get; init; } = null!;
        public required string PasswordHash { get; init; } = null!;
        public required string Phone { get; init; } = null!;
        public DateTime? CreatedAt { get; init; }
    }
}
