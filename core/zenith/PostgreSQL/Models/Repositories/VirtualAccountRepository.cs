using Dapper;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;

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
                                   counting::text AS "Counting",
                                   balance AS "Balance",
                                   balance_set_at AS "BalanceSetAt"
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
                               (user_id, name, account_number, clearing_number, iban, counting, balance, balance_set_at)
                               VALUES
                               (@UserId, @Name, @AccountNumber, @ClearingNumber, @Iban, @Counting::"CreditCounting",
                                @Balance, CASE WHEN @Balance IS NULL THEN NULL ELSE now() END)
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
                account.Counting,
                account.Balance
            });
        }

        public async Task<bool> UpdateVirtualAccountAsync(long userId,
                                                          long virtualAccountId,
                                                          VirtualAccountDto.VirtualAccount account,
                                                          bool setBalance)
        {
            const string sql = """
                               UPDATE "VirtualAccount"
                               SET name = @Name,
                                   account_number = @AccountNumber,
                                   clearing_number = @ClearingNumber,
                                   iban = @Iban,
                                   counting = @Counting::"CreditCounting",
                                   balance = CASE WHEN @SetBalance THEN @Balance ELSE balance END,
                                   balance_set_at = CASE
                                       WHEN NOT @SetBalance THEN balance_set_at
                                       WHEN @Balance IS NULL THEN NULL
                                       ELSE now()
                                   END
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
                account.Counting,
                account.Balance,
                SetBalance = setBalance
            }) > 0;
        }

        public async Task<VirtualBalanceTransactionRow[]> GetTransactionsSinceAsync(long userId,
                                                                                    DateTime since)
        {
            const string sql = """
                               SELECT
                                   t.id AS "Id",
                                   t.counterparty_account AS "CounterpartyAccount",
                                   t.amount AS "Amount",
                                   t.first_seen_at AS "FirstSeenAt"
                               FROM "Transaction" t
                               INNER JOIN "Account" a
                                   ON a.id = t.account_id
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE bc.user_id = @UserId
                                 AND t.counterparty_account IS NOT NULL
                                 AND t.first_seen_at > @Since
                                 AND t.status NOT IN ('CNCL', 'RJCT', 'SCHD');
                               """;

            using var connection = _connectionFactory.Create();
            return (await connection.QueryAsync<VirtualBalanceTransactionRow>(sql, new { UserId = userId, Since = since })).ToArray();
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
