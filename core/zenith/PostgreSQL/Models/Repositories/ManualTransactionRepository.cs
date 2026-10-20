using Dapper;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class ManualTransactionRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public ManualTransactionRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<ManualTransactionDto.ManualTransaction[]> GetManualTransactionsForUserAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   id AS "Id",
                                   to_char(occurred_on, 'YYYY-MM-DD') AS "Date",
                                   amount AS "Amount",
                                   currency AS "Currency",
                                   description AS "Description",
                                   counting::text AS "Counting",
                                   from_account_id AS "FromAccountId",
                                   from_virtual_account_id AS "FromVirtualAccountId",
                                   from_account_number AS "FromAccountNumber",
                                   from_name AS "FromName",
                                   to_account_id AS "ToAccountId",
                                   to_virtual_account_id AS "ToVirtualAccountId",
                                   to_account_number AS "ToAccountNumber",
                                   to_name AS "ToName",
                                   created_at AS "CreatedAt"
                               FROM "ManualTransaction"
                               WHERE user_id = @UserId
                               ORDER BY occurred_on DESC, id DESC;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<ManualTransactionDto.ManualTransaction> transactions = await connection.QueryAsync<ManualTransactionDto.ManualTransaction>(sql, new { UserId = userId });

            return transactions.ToArray();
        }

        public async Task<ManualMatchCandidate[]> GetMatchCandidatesAsync(long userId,
                                                                          int maxDayDistance)
        {
            const string sql = """
                               SELECT
                                   m.id AS "ManualTransactionId",
                                   t.id AS "TransactionId",
                                   abs(t.occurred_on - m.occurred_on) AS "DayDistance",
                                   t.counterparty_account AS "CounterpartyAccount",
                                   (t.account_id = m.from_account_id) AS "FromSide"
                               FROM "ManualTransaction" m
                               INNER JOIN "Transaction" t
                                   ON (t.account_id = m.from_account_id AND t.amount = -m.amount)
                                   OR (t.account_id = m.to_account_id AND t.amount = m.amount)
                               WHERE m.user_id = @UserId
                                 AND t.occurred_on BETWEEN m.occurred_on - @MaxDayDistance AND m.occurred_on + @MaxDayDistance
                                 AND t.status NOT IN ('CNCL', 'RJCT', 'SCHD');
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<ManualMatchCandidate> candidates = await connection.QueryAsync<ManualMatchCandidate>(sql, new
            {
                UserId = userId,
                MaxDayDistance = maxDayDistance
            });

            return candidates.ToArray();
        }

        public async Task<long> InsertManualTransactionAsync(long userId,
                                                             ManualTransactionDto.ManualTransaction transaction)
        {
            const string sql = """
                               INSERT INTO "ManualTransaction"
                               (user_id, occurred_on, amount, currency, description, counting,
                                from_account_id, from_virtual_account_id, from_account_number, from_name,
                                to_account_id, to_virtual_account_id, to_account_number, to_name)
                               VALUES
                               (@UserId, CAST(@Date AS date), @Amount, @Currency, @Description, @Counting::"CreditCounting",
                                @FromAccountId, @FromVirtualAccountId, @FromAccountNumber, @FromName,
                                @ToAccountId, @ToVirtualAccountId, @ToAccountNumber, @ToName)
                               RETURNING id;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleAsync<long>(sql, new
            {
                UserId = userId,
                transaction.Date,
                transaction.Amount,
                transaction.Currency,
                transaction.Description,
                transaction.Counting,
                transaction.FromAccountId,
                transaction.FromVirtualAccountId,
                transaction.FromAccountNumber,
                transaction.FromName,
                transaction.ToAccountId,
                transaction.ToVirtualAccountId,
                transaction.ToAccountNumber,
                transaction.ToName
            });
        }

        public async Task<bool> DeleteManualTransactionAsync(long userId,
                                                             long manualTransactionId)
        {
            const string sql = """
                               DELETE FROM "ManualTransaction"
                               WHERE id = @ManualTransactionId
                                 AND user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new { UserId = userId, ManualTransactionId = manualTransactionId }) > 0;
        }
    }
}
