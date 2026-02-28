using Dapper;

using ZenithFin.Api.Models.Dtos;

using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Dtos;

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

        public async Task<LoginAttempt?> ValidateLoginAsync(LoginDto.LoginRequest user)
        {
            const string sql = """
            SELECT id AS "UserId", 
                   password_hash AS "PasswordHash"
            FROM "User"
            WHERE email = @Email
            """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleOrDefaultAsync<LoginAttempt?>(sql, new { user.Email });
        }

        public async Task InsertUserSessionAsync(SessionEntity session)
        {
            const string sql = """
            INSERT INTO "Session"
            (session_id, user_id, jwt_secret_encrypted, issued_at, expires_at, revoked_at)
            VALUES
            (@SessionId, @UserId, @JwtSecretEncrypted, @IssuedAt, @ExpiresAt, NULL);
            """;

            using var connection = _connectionFactory.Create();
            await connection.ExecuteAsync(sql, session);
        }

        public async Task<ActiveSession?> GetSessionById(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid sessionGuid))
            {
                Console.WriteLine($"Invalid session ID format: {sessionId}");
                return null;
            }

            const string sql = """
            SELECT jwt_secret_encrypted AS "JwtSecretEncrypted", 
                   expires_at AS "ExpiresAt"
            FROM "Session"
            WHERE session_id = @SessionId
            """;

            using var connection = _connectionFactory.Create();
            return await connection.QueryFirstOrDefaultAsync<ActiveSession?>(sql, new { SessionId = sessionGuid });
        }
    }
}
