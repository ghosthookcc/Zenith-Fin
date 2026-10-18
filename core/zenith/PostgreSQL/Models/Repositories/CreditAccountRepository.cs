using Dapper;

using ZenithFin.Api.Models.Dtos;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class CreditAccountRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public CreditAccountRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<CreditAccountDto.CreditAccount[]> GetCreditAccountsForUserAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   id AS "Id",
                                   name AS "Name",
                                   account_id AS "AccountId",
                                   counting::text AS "Counting"
                               FROM "CreditAccount"
                               WHERE user_id = @UserId
                               ORDER BY id;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<CreditAccountDto.CreditAccount> creditAccounts = await connection.QueryAsync<CreditAccountDto.CreditAccount>(sql, new { UserId = userId });

            return creditAccounts.ToArray();
        }

        public async Task<long?> InsertCreditAccountAsync(long userId,
                                                          string name,
                                                          long accountId,
                                                          string counting)
        {
            const string sql = """
                               INSERT INTO "CreditAccount"
                               (user_id, account_id, name, counting)
                               SELECT @UserId, a.id, @Name, @Counting::"CreditCounting"
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
                Name = name,
                Counting = counting
            });
        }

        public async Task<bool> UpdateCreditAccountAsync(long userId,
                                                         long creditAccountId,
                                                         string name,
                                                         long accountId,
                                                         string counting)
        {
            const string sql = """
                               UPDATE "CreditAccount"
                               SET name = @Name,
                                   account_id = @AccountId,
                                   counting = @Counting::"CreditCounting"
                               WHERE id = @CreditAccountId
                                 AND user_id = @UserId
                                 AND EXISTS (
                                     SELECT 1
                                     FROM "Account" a
                                     INNER JOIN "BankConnection" bc
                                         ON bc.id = a.bank_connection_id
                                     WHERE a.id = @AccountId
                                       AND bc.user_id = @UserId
                                 );
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                CreditAccountId = creditAccountId,
                AccountId = accountId,
                Name = name,
                Counting = counting
            }) > 0;
        }

        public async Task<bool> DeleteCreditAccountAsync(long userId,
                                                         long creditAccountId)
        {
            const string sql = """
                               DELETE FROM "CreditAccount"
                               WHERE id = @CreditAccountId
                                 AND user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new { UserId = userId, CreditAccountId = creditAccountId }) > 0;
        }
    }
}
