using System.Data;
using System.Globalization;

using Dapper;

using ZenithFin.Api.Models.Dtos;

using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class TransactionRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public TransactionRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<TransactionAccountRow[]> GetAccountsForUserAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   a.id AS "Id",
                                   a.enable_banking_uid AS "EnableBankingUid",
                                   a.name AS "Name",
                                   a.iban AS "Iban",
                                   a.currency AS "Currency",
                                   a.bank_connection_id AS "BankConnectionId",
                                   bc.aspsp_name AS "BankName",
                                   bc.status = 'ACTIVE'::"BankStatus" AS "IsActive",
                                   a.transactions_synced_at AS "TransactionsSyncedAt"
                               FROM "Account" a
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE bc.user_id = @UserId
                               ORDER BY a.id;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<TransactionAccountRow> accounts = await connection.QueryAsync<TransactionAccountRow>(sql, new { UserId = userId });

            return accounts.ToArray();
        }

        public async Task<DateTime?> GetTransactionsSyncedAtAsync(long userId,
                                                                  long accountId)
        {
            const string sql = """
                               SELECT a.transactions_synced_at
                               FROM "Account" a
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE a.id = @AccountId
                                 AND bc.user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.QueryFirstOrDefaultAsync<DateTime?>(sql, new { UserId = userId, AccountId = accountId });
        }

        public async Task<TransactionDto.Transaction[]> GetTransactionsForUserAsync(long userId,
                                                                                   DateOnly from,
                                                                                   DateOnly to)
        {
            const string sql = """
                               SELECT
                                   t.id AS "Id",
                                   t.account_id AS "AccountId",
                                   to_char(t.occurred_on, 'YYYY-MM-DD') AS "Date",
                                   to_char(t.booking_date, 'YYYY-MM-DD') AS "BookingDate",
                                   to_char(t.value_date, 'YYYY-MM-DD') AS "ValueDate",
                                   to_char(t.transaction_date, 'YYYY-MM-DD') AS "TransactionDate",
                                   t.amount AS "Amount",
                                   t.currency AS "Currency",
                                   t.original_amount AS "OriginalAmount",
                                   t.original_currency AS "OriginalCurrency",
                                   t.status AS "Status",
                                   t.credit_debit_indicator AS "CreditDebitIndicator",
                                   t.counterparty_name AS "CounterpartyName",
                                   t.counterparty_account AS "CounterpartyAccount",
                                   COALESCE(t.remittance_information, ARRAY[]::text[]) AS "RemittanceInformation",
                                   t.merchant_category_code AS "MerchantCategoryCode",
                                   t.bank_transaction_code AS "BankTransactionCode",
                                   t.reference_number AS "ReferenceNumber",
                                   t.note AS "Note"
                               FROM "Transaction" t
                               INNER JOIN "Account" a
                                   ON a.id = t.account_id
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE bc.user_id = @UserId
                                 AND t.occurred_on BETWEEN CAST(@From AS date) AND CAST(@To AS date)
                               ORDER BY t.occurred_on DESC, (t.status = 'BOOK'), t.id;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<TransactionDto.Transaction> transactions = await connection.QueryAsync<TransactionDto.Transaction>(sql, new
            {
                UserId = userId,
                From = FormatDate(from),
                To = FormatDate(to)
            });

            return transactions.ToArray();
        }

        public async Task ReplaceTransactionsAsync(long userId,
                                                   long accountId,
                                                   IReadOnlyList<TransactionEntity> transactions,
                                                   DateTime syncedAt)
        {
            if (syncedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("Sync time must be UTC.", nameof(syncedAt));

            using var connection = _connectionFactory.Create();

            if (connection.State != ConnectionState.Open) connection.Open();

            using var transaction = connection.BeginTransaction();

            const string lockSql = """
                                   SELECT a.id
                                   FROM "Account" a
                                   INNER JOIN "BankConnection" bc
                                       ON bc.id = a.bank_connection_id
                                   WHERE a.id = @AccountId
                                     AND bc.user_id = @UserId
                                     AND bc.status = 'ACTIVE'::"BankStatus"
                                   FOR UPDATE OF a;
                                   """;

            var ownedAccount = await connection.QuerySingleOrDefaultAsync<long?>(
                lockSql,
                new { UserId = userId, AccountId = accountId },
                transaction);

            if (ownedAccount == null)
                throw new InvalidOperationException(
                    "The account is not available for this user.");

            const string deleteSql = """
                                     DELETE FROM "Transaction"
                                     WHERE account_id = @AccountId
                                       AND status <> 'BOOK';
                                     """;

            await connection.ExecuteAsync(deleteSql, new { AccountId = accountId }, transaction);

            const string upsertSql = """
                                     INSERT INTO "Transaction"
                                     (
                                         account_id,
                                         external_id,
                                         entry_reference,
                                         status,
                                         credit_debit_indicator,
                                         amount,
                                         currency,
                                         original_amount,
                                         original_currency,
                                         booking_date,
                                         value_date,
                                         transaction_date,
                                         occurred_on,
                                         counterparty_name,
                                         counterparty_account,
                                         remittance_information,
                                         merchant_category_code,
                                         bank_transaction_code,
                                         reference_number,
                                         note,
                                         raw,
                                         first_seen_at,
                                         updated_at
                                     )
                                     VALUES
                                     (
                                         @AccountId,
                                         @ExternalId,
                                         @EntryReference,
                                         @Status,
                                         @CreditDebitIndicator,
                                         @Amount,
                                         @Currency,
                                         @OriginalAmount,
                                         @OriginalCurrency,
                                         CAST(@BookingDate AS date),
                                         CAST(@ValueDate AS date),
                                         CAST(@TransactionDate AS date),
                                         CAST(@OccurredOn AS date),
                                         @CounterpartyName,
                                         @CounterpartyAccount,
                                         @RemittanceInformation,
                                         @MerchantCategoryCode,
                                         @BankTransactionCode,
                                         @ReferenceNumber,
                                         @Note,
                                         CAST(@Raw AS jsonb),
                                         @SyncedAt,
                                         @SyncedAt
                                     )
                                     ON CONFLICT (account_id, external_id)
                                     DO UPDATE SET
                                         entry_reference = EXCLUDED.entry_reference,
                                         status = EXCLUDED.status,
                                         credit_debit_indicator = EXCLUDED.credit_debit_indicator,
                                         amount = EXCLUDED.amount,
                                         currency = EXCLUDED.currency,
                                         original_amount = EXCLUDED.original_amount,
                                         original_currency = EXCLUDED.original_currency,
                                         booking_date = EXCLUDED.booking_date,
                                         value_date = EXCLUDED.value_date,
                                         transaction_date = EXCLUDED.transaction_date,
                                         occurred_on = EXCLUDED.occurred_on,
                                         counterparty_name = EXCLUDED.counterparty_name,
                                         counterparty_account = EXCLUDED.counterparty_account,
                                         remittance_information = EXCLUDED.remittance_information,
                                         merchant_category_code = EXCLUDED.merchant_category_code,
                                         bank_transaction_code = EXCLUDED.bank_transaction_code,
                                         reference_number = EXCLUDED.reference_number,
                                         note = EXCLUDED.note,
                                         raw = EXCLUDED.raw,
                                         updated_at = EXCLUDED.updated_at;
                                     """;

            await connection.ExecuteAsync(
                upsertSql,
                transactions.Select(item => new
                {
                    AccountId = accountId,

                    item.ExternalId,
                    item.EntryReference,

                    item.Status,
                    item.CreditDebitIndicator,

                    item.Amount,
                    item.Currency,
                    item.OriginalAmount,
                    item.OriginalCurrency,

                    BookingDate = FormatDate(item.BookingDate),
                    ValueDate = FormatDate(item.ValueDate),
                    TransactionDate = FormatDate(item.TransactionDate),
                    OccurredOn = FormatDate(item.OccurredOn),

                    item.CounterpartyName,
                    item.CounterpartyAccount,
                    item.RemittanceInformation,
                    item.MerchantCategoryCode,
                    item.BankTransactionCode,
                    item.ReferenceNumber,
                    item.Note,

                    item.Raw,
                    SyncedAt = syncedAt
                }),
                transaction);

            const string timestampSql = """
                                        UPDATE "Account"
                                        SET transactions_synced_at = @SyncedAt
                                        WHERE id = @AccountId;
                                        """;

            await connection.ExecuteAsync(timestampSql,
                                    new { AccountId = accountId, SyncedAt = syncedAt },
                                          transaction);
            transaction.Commit();
        }

        private static string? FormatDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
