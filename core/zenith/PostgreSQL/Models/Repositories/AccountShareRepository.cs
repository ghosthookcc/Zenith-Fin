using Dapper;

using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class AccountShareRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public AccountShareRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<AccountShareRow[]> GetSharesForUserAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   s.id AS "Id",
                                   s.account_id AS "AccountId",
                                   s.first_name AS "FirstName",
                                   s.last_name AS "LastName",
                                   s.personal_number_encrypted AS "PersonalNumberEncrypted"
                               FROM "AccountShare" s
                               INNER JOIN "Account" a
                                   ON a.id = s.account_id
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE bc.user_id = @UserId
                               ORDER BY s.account_id, s.id;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<AccountShareRow> shares = await connection.QueryAsync<AccountShareRow>(sql, new { UserId = userId });

            return shares.ToArray();
        }

        public async Task<long?> InsertShareAsync(long userId,
                                                  long accountId,
                                                  string firstName,
                                                  string lastName,
                                                  string? personalNumberEncrypted)
        {
            const string sql = """
                               INSERT INTO "AccountShare"
                               (account_id, first_name, last_name, personal_number_encrypted)
                               SELECT a.id, @FirstName, @LastName, @PersonalNumberEncrypted
                               FROM "Account" a
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE a.id = @AccountId
                                 AND bc.user_id = @UserId
                               RETURNING id;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleOrDefaultAsync<long?>(sql, new
            {
                UserId = userId,
                AccountId = accountId,
                FirstName = firstName,
                LastName = lastName,
                PersonalNumberEncrypted = personalNumberEncrypted
            });
        }

        public async Task<bool> UpdateShareAsync(long userId,
                                                 long shareId,
                                                 string firstName,
                                                 string lastName,
                                                 bool replacePersonalNumber,
                                                 string? personalNumberEncrypted)
        {
            const string sql = """
                               UPDATE "AccountShare" s
                               SET first_name = @FirstName,
                                   last_name = @LastName,
                                   personal_number_encrypted = CASE WHEN @ReplacePersonalNumber
                                                                    THEN @PersonalNumberEncrypted
                                                                    ELSE s.personal_number_encrypted
                                                               END
                               FROM "Account" a
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE s.id = @ShareId
                                 AND a.id = s.account_id
                                 AND bc.user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                ShareId = shareId,
                FirstName = firstName,
                LastName = lastName,
                ReplacePersonalNumber = replacePersonalNumber,
                PersonalNumberEncrypted = personalNumberEncrypted
            }) > 0;
        }

        public async Task<bool> DeleteShareAsync(long userId,
                                                 long shareId)
        {
            const string sql = """
                               DELETE FROM "AccountShare" s
                               USING "Account" a, "BankConnection" bc
                               WHERE s.id = @ShareId
                                 AND a.id = s.account_id
                                 AND bc.id = a.bank_connection_id
                                 AND bc.user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new { UserId = userId, ShareId = shareId }) > 0;
        }
    }
}
