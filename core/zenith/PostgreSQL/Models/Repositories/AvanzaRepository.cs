using Dapper;

using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class AvanzaRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public AvanzaRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task SaveCredentialsAsync(long userId,
                                               byte[] ciphertext,
                                               string keyFingerprint)
        {
            const string sql = """
                               INSERT INTO avanza_credentials (user_id, ciphertext, key_fingerprint, updated_at)
                               VALUES (@UserId, @Ciphertext, @KeyFingerprint, now())
                               ON CONFLICT (user_id) DO UPDATE
                               SET ciphertext = EXCLUDED.ciphertext,
                                   key_fingerprint = EXCLUDED.key_fingerprint,
                                   updated_at = EXCLUDED.updated_at;
                               """;

            using var connection = _connectionFactory.Create();
            await connection.ExecuteAsync(sql, new { UserId = userId, Ciphertext = ciphertext, KeyFingerprint = keyFingerprint });
        }

        public async Task<bool> DeleteCredentialsAsync(long userId)
        {
            const string sql = """
                               DELETE FROM avanza_credentials
                               WHERE user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new { UserId = userId }) > 0;
        }

        public async Task<AvanzaStatusRow> GetStatusAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   c.updated_at AS "UpdatedAt",
                                   c.key_fingerprint AS "KeyFingerprint",
                                   s.status::text AS "Status",
                                   s.error_class AS "ErrorClass",
                                   s.last_attempt_at AS "LastAttemptAt",
                                   s.last_success_at AS "LastSuccessAt"
                               FROM (SELECT @UserId::bigint AS user_id) u
                               LEFT JOIN avanza_credentials c
                                   ON c.user_id = u.user_id
                               LEFT JOIN avanza_sync_status s
                                   ON s.user_id = u.user_id;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleAsync<AvanzaStatusRow>(sql, new { UserId = userId });
        }

        public async Task<AvanzaSnapshotRow[]> GetSnapshotsAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   account_id AS "AccountId",
                                   account_name AS "AccountName",
                                   account_type AS "AccountType",
                                   total_value AS "TotalValue",
                                   currency AS "Currency",
                                   fetched_at AS "FetchedAt"
                               FROM avanza_balance_snapshots
                               WHERE user_id = @UserId
                               ORDER BY fetched_at, account_id;
                               """;

            using var connection = _connectionFactory.Create();
            return (await connection.QueryAsync<AvanzaSnapshotRow>(sql, new { UserId = userId })).ToArray();
        }
    }
}
