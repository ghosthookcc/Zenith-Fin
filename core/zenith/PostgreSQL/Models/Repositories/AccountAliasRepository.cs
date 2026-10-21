using Dapper;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class AccountAliasRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public AccountAliasRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<bool> SaveAliasAsync(long userId,
                                               long accountId,
                                               string alias)
        {
            const string sql = """
                               INSERT INTO "AccountAlias"
                               (account_id, bank_name_alias)
                               SELECT a.id, @Alias
                               FROM "Account" a
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE a.id = @AccountId
                                 AND bc.user_id = @UserId
                               ON CONFLICT (account_id)
                               DO UPDATE SET bank_name_alias = EXCLUDED.bank_name_alias
                               RETURNING account_id;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleOrDefaultAsync<long?>(sql, new
            {
                UserId = userId,
                AccountId = accountId,
                Alias = alias
            }) != null;
        }

        public async Task<bool> SetRoleAsync(long userId,
                                             long accountId,
                                             string? role)
        {
            const string sql = """
                               UPDATE "Account" a
                               SET role = CAST(@Role AS "AccountRole")
                               FROM "BankConnection" bc
                               WHERE a.id = @AccountId
                                 AND bc.id = a.bank_connection_id
                                 AND bc.user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new { UserId = userId, AccountId = accountId, Role = role }) > 0;
        }

        public async Task<bool> ClearAliasAsync(long userId,
                                                long accountId)
        {
            const string sql = """
                               WITH owned AS (
                                   SELECT a.id
                                   FROM "Account" a
                                   INNER JOIN "BankConnection" bc
                                       ON bc.id = a.bank_connection_id
                                   WHERE a.id = @AccountId
                                     AND bc.user_id = @UserId
                               ),
                               cleared AS (
                                   DELETE FROM "AccountAlias"
                                   WHERE account_id IN (SELECT id FROM owned)
                               )
                               SELECT count(*) FROM owned;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleAsync<long>(sql, new { UserId = userId, AccountId = accountId }) > 0;
        }
    }
}
