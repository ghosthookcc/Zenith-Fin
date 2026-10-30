using Dapper;

using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class ExportRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public ExportRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<ExportAccountRow[]> GetAccountsAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   a.id AS "Id",
                                   a.identification_hash AS "IdentificationHash",
                                   a.iban AS "Iban",
                                   bc.status::text AS "ConnectionStatus",
                                   bc.consent_expires_at AS "ConsentExpiresAt",
                                   a.balances_fetched_at AS "BalancesFetchedAt",
                                   a.transactions_synced_at AS "TransactionsSyncedAt",
                                   (
                                       SELECT to_char(min(t.occurred_on), 'YYYY-MM-DD')
                                       FROM "Transaction" t
                                       WHERE t.account_id = a.id
                                   ) AS "FirstTransactionOn"
                               FROM "Account" a
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE bc.user_id = @UserId
                               ORDER BY a.id;
                               """;

            using var connection = _connectionFactory.Create();
            return (await connection.QueryAsync<ExportAccountRow>(sql, new { UserId = userId })).ToArray();
        }
    }
}
