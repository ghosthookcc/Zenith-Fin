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
                                   s.virtual_account_id AS "VirtualAccountId",
                                   s.first_name AS "FirstName",
                                   s.last_name AS "LastName",
                                   s.personal_number_encrypted AS "PersonalNumberEncrypted"
                               FROM "AccountShare" s
                               LEFT JOIN "Account" a
                                   ON a.id = s.account_id
                               LEFT JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               LEFT JOIN "VirtualAccount" va
                                   ON va.id = s.virtual_account_id
                               WHERE bc.user_id = @UserId
                                  OR va.user_id = @UserId
                               ORDER BY s.account_id NULLS LAST, s.virtual_account_id, s.id;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<AccountShareRow> shares = await connection.QueryAsync<AccountShareRow>(sql, new { UserId = userId });

            return shares.ToArray();
        }

        public async Task<long?> InsertShareAsync(long userId,
                                                  long? accountId,
                                                  long? virtualAccountId,
                                                  string firstName,
                                                  string lastName,
                                                  string? personalNumberEncrypted)
        {
            const string accountSql = """
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

            const string virtualAccountSql = """
                                             INSERT INTO "AccountShare"
                                             (virtual_account_id, first_name, last_name, personal_number_encrypted)
                                             SELECT va.id, @FirstName, @LastName, @PersonalNumberEncrypted
                                             FROM "VirtualAccount" va
                                             WHERE va.id = @VirtualAccountId
                                               AND va.user_id = @UserId
                                             RETURNING id;
                                             """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleOrDefaultAsync<long?>(accountId != null ? accountSql : virtualAccountSql, new
            {
                UserId = userId,
                AccountId = accountId,
                VirtualAccountId = virtualAccountId,
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
                               WHERE s.id = @ShareId
                                 AND (EXISTS (SELECT 1
                                              FROM "Account" a
                                              INNER JOIN "BankConnection" bc
                                                  ON bc.id = a.bank_connection_id
                                              WHERE a.id = s.account_id
                                                AND bc.user_id = @UserId)
                                      OR EXISTS (SELECT 1
                                                 FROM "VirtualAccount" va
                                                 WHERE va.id = s.virtual_account_id
                                                   AND va.user_id = @UserId));
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
                               WHERE s.id = @ShareId
                                 AND (EXISTS (SELECT 1
                                              FROM "Account" a
                                              INNER JOIN "BankConnection" bc
                                                  ON bc.id = a.bank_connection_id
                                              WHERE a.id = s.account_id
                                                AND bc.user_id = @UserId)
                                      OR EXISTS (SELECT 1
                                                 FROM "VirtualAccount" va
                                                 WHERE va.id = s.virtual_account_id
                                                   AND va.user_id = @UserId));
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new { UserId = userId, ShareId = shareId }) > 0;
        }
    }
}
