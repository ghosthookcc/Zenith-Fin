using Dapper;

using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class UserRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public UserRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task InsertUserAsync(UserEntity user)
        {
            const string sql = """
            INSERT INTO "User"
            (full_name, email, phone, password_hash)
            VALUES
            (@FullName, @Email, @Phone, @PasswordHash)
            """;

            using var connection = _connectionFactory.Create();
            await connection.ExecuteAsync(sql, user);
        }
    }
}
