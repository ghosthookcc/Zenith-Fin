using System.Globalization;

using Dapper;

using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class LedgerRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public LedgerRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<LedgerAccountRow[]> GetAccountsAsync(IReadOnlyCollection<long> userIds)
        {
            const string sql = """
                               SELECT
                                   a.id AS "Id",
                                   bc.user_id AS "OwnerUserId",
                                   a.identification_hash AS "IdentificationHash",
                                   a.iban AS "Iban",
                                   COALESCE(aa.bank_name_alias, a.name, bc.aspsp_name) AS "Name",
                                   bc.aspsp_name AS "BankName",
                                   a.currency AS "Currency",
                                   a.role::text AS "Role",
                                   a.bank_account_type AS "BankAccountType",
                                   ca.id AS "CreditAccountId",
                                   ca.name AS "CreditName",
                                   ca.mode::text AS "CreditMode"
                               FROM "Account" a
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               LEFT JOIN "AccountAlias" aa
                                   ON aa.account_id = a.id
                               LEFT JOIN "CreditAccount" ca
                                   ON ca.account_id = a.id
                               WHERE bc.user_id = ANY(@UserIds)
                               ORDER BY a.id;
                               """;

            using var connection = _connectionFactory.Create();
            return (await connection.QueryAsync<LedgerAccountRow>(sql, new { UserIds = userIds.ToArray() })).ToArray();
        }

        public async Task<BalanceRow[]> GetBalancesAsync(IReadOnlyCollection<long> accountIds)
        {
            const string sql = """
                               SELECT DISTINCT ON (b.account_id)
                                   b.account_id AS "AccountId",
                                   b.amount AS "Amount"
                               FROM "Balance" b
                               WHERE b.account_id = ANY(@AccountIds)
                               ORDER BY b.account_id,
                                        CASE b.balance_type WHEN 'ITAV' THEN 0 WHEN 'CLBD' THEN 1 WHEN 'ITBD' THEN 2 ELSE 3 END,
                                        b.fetched_at DESC;
                               """;

            using var connection = _connectionFactory.Create();
            return (await connection.QueryAsync<BalanceRow>(sql, new { AccountIds = accountIds.ToArray() })).ToArray();
        }

        public async Task<LedgerTransactionRow[]> GetTransactionsAsync(IReadOnlyCollection<long> accountIds,
                                                                      DateOnly from,
                                                                      DateOnly to)
        {
            const string sql = """
                               SELECT
                                   t.id AS "Id",
                                   t.account_id AS "AccountId",
                                   to_char(t.occurred_on, 'YYYY-MM-DD') AS "Date",
                                   t.amount AS "Amount",
                                   t.currency AS "Currency",
                                   t.status AS "Status",
                                   t.counterparty_name AS "CounterpartyName",
                                   t.counterparty_account AS "CounterpartyAccount",
                                   t.bank_transaction_code AS "BankTransactionCode",
                                   t.external_id AS "ExternalId",
                                   tg.kind::text AS "TagKind",
                                   tg.key AS "TagKey",
                                   tg.name AS "TagName",
                                   t.tag_source::text AS "TagSource",
                                   t.counting_override::text AS "CountingOverride",
                                   t.type_override::text AS "TypeOverride",
                                   t.credit_debit_indicator AS "CreditDebitIndicator",
                                   COALESCE(t.remittance_information, ARRAY[]::text[]) AS "RemittanceInformation"
                               FROM "Transaction" t
                               LEFT JOIN "Tag" tg
                                   ON tg.id = t.tag_id
                               WHERE t.account_id = ANY(@AccountIds)
                                 AND t.occurred_on BETWEEN CAST(@From AS date) AND CAST(@To AS date)
                               ORDER BY t.occurred_on, t.id;
                               """;

            using var connection = _connectionFactory.Create();
            return (await connection.QueryAsync<LedgerTransactionRow>(sql, new
            {
                AccountIds = accountIds.ToArray(),
                From = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                To = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            })).ToArray();
        }
    }
}
