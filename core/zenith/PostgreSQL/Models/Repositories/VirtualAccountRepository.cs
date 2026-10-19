using Dapper;

using ZenithFin.Api.Models.Dtos;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class VirtualAccountRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public VirtualAccountRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<VirtualAccountDto.VirtualAccount[]> GetVirtualAccountsForUserAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   id AS "Id",
                                   name AS "Name",
                                   account_number AS "AccountNumber",
                                   clearing_number AS "ClearingNumber",
                                   iban AS "Iban",
                                   counting::text AS "Counting"
                               FROM "VirtualAccount"
                               WHERE user_id = @UserId
                               ORDER BY id;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<VirtualAccountDto.VirtualAccount> accounts = await connection.QueryAsync<VirtualAccountDto.VirtualAccount>(sql, new { UserId = userId });

            return accounts.ToArray();
        }

        public async Task<long> InsertVirtualAccountAsync(long userId,
                                                          VirtualAccountDto.VirtualAccount account)
        {
            const string sql = """
                               INSERT INTO "VirtualAccount"
                               (user_id, name, account_number, clearing_number, iban, counting)
                               VALUES
                               (@UserId, @Name, @AccountNumber, @ClearingNumber, @Iban, @Counting::"CreditCounting")
                               RETURNING id;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleAsync<long>(sql, new
            {
                UserId = userId,
                account.Name,
                account.AccountNumber,
                account.ClearingNumber,
                account.Iban,
                account.Counting
            });
        }

        public async Task<bool> UpdateVirtualAccountAsync(long userId,
                                                          long virtualAccountId,
                                                          VirtualAccountDto.VirtualAccount account)
        {
            const string sql = """
                               UPDATE "VirtualAccount"
                               SET name = @Name,
                                   account_number = @AccountNumber,
                                   clearing_number = @ClearingNumber,
                                   iban = @Iban,
                                   counting = @Counting::"CreditCounting"
                               WHERE id = @VirtualAccountId
                                 AND user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                VirtualAccountId = virtualAccountId,
                account.Name,
                account.AccountNumber,
                account.ClearingNumber,
                account.Iban,
                account.Counting
            }) > 0;
        }

        public async Task<bool> DeleteVirtualAccountAsync(long userId,
                                                          long virtualAccountId)
        {
            const string sql = """
                               DELETE FROM "VirtualAccount"
                               WHERE id = @VirtualAccountId
                                 AND user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new { UserId = userId, VirtualAccountId = virtualAccountId }) > 0;
        }
    }
}
