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
                                   t.note AS "Note",
                                   t.tag_id AS "TagId",
                                   t.tag_source::text AS "TagSource"
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

        public async Task<CategorizationCandidate[]> GetCategorizationCandidatesAsync(long userId,
                                                                                    bool includeRuleTagged)
        {
            const string sql = """
                               SELECT
                                   t.id AS "Id",
                                   t.tag_id AS "TagId",
                                   t.tag_source::text AS "TagSource",
                                   t.counterparty_name AS "CounterpartyName",
                                   COALESCE(t.remittance_information, ARRAY[]::text[]) AS "RemittanceInformation",
                                   t.bank_transaction_code AS "BankTransactionCode",
                                   t.counterparty_account AS "CounterpartyAccount",
                                   t.credit_debit_indicator AS "CreditDebitIndicator",
                                   t.original_currency AS "OriginalCurrency"
                               FROM "Transaction" t
                               INNER JOIN "Account" a
                                   ON a.id = t.account_id
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE bc.user_id = @UserId
                                 AND (t.tag_source IS NULL
                                      OR (@IncludeRuleTagged AND t.tag_source IN ('RULE'::"TagSource", 'AI'::"TagSource")));
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<CategorizationCandidate> candidates = await connection.QueryAsync<CategorizationCandidate>(sql, new
            {
                UserId = userId,
                IncludeRuleTagged = includeRuleTagged
            });

            return candidates.ToArray();
        }

        public async Task ApplyRuleTagsAsync(long userId,
                                             IReadOnlyList<(long TransactionId, long TagId)> tags,
                                             DateTime taggedAt)
        {
            if (taggedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("Tag time must be UTC.", nameof(taggedAt));

            const string sql = """
                               UPDATE "Transaction" AS t
                               SET tag_id = assigned.tag_id,
                                   tag_source = 'RULE'::"TagSource",
                                   tagged_at = @TaggedAt
                               FROM unnest(@TransactionIds, @TagIds) AS assigned(transaction_id, tag_id),
                                    "Account" a,
                                    "BankConnection" bc
                               WHERE t.id = assigned.transaction_id
                                 AND a.id = t.account_id
                                 AND bc.id = a.bank_connection_id
                                 AND bc.user_id = @UserId
                                 AND t.tag_source IS DISTINCT FROM 'USER'::"TagSource";
                               """;

            using var connection = _connectionFactory.Create();
            await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                TransactionIds = tags.Select(tag => tag.TransactionId).ToArray(),
                TagIds = tags.Select(tag => tag.TagId).ToArray(),
                TaggedAt = taggedAt
            });
        }

        public async Task<AiCandidateRow[]> GetAiCandidatesAsync(long userId,
                                                                 long otherTagId)
        {
            const string sql = """
                               SELECT
                                   t.id AS "Id",
                                   t.counterparty_name AS "CounterpartyName",
                                   COALESCE(t.remittance_information, ARRAY[]::text[]) AS "RemittanceInformation",
                                   t.bank_transaction_code AS "BankTransactionCode",
                                   t.credit_debit_indicator AS "CreditDebitIndicator",
                                   t.amount AS "Amount",
                                   t.currency AS "Currency"
                               FROM "Transaction" t
                               INNER JOIN "Account" a
                                   ON a.id = t.account_id
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE bc.user_id = @UserId
                                 AND t.tag_source = 'RULE'::"TagSource"
                                 AND t.tag_id = @OtherTagId
                               ORDER BY t.occurred_on DESC, t.id DESC;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<AiCandidateRow> candidates = await connection.QueryAsync<AiCandidateRow>(sql, new
            {
                UserId = userId,
                OtherTagId = otherTagId
            });

            return candidates.ToArray();
        }

        public async Task<int> ApplyAiTagsAsync(long userId,
                                                IReadOnlyList<(long TransactionId, long TagId)> tags,
                                                long otherTagId,
                                                DateTime taggedAt)
        {
            if (taggedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("Tag time must be UTC.", nameof(taggedAt));

            const string sql = """
                               UPDATE "Transaction" AS t
                               SET tag_id = assigned.tag_id,
                                   tag_source = 'AI'::"TagSource",
                                   tagged_at = @TaggedAt
                               FROM unnest(@TransactionIds, @TagIds) AS assigned(transaction_id, tag_id),
                                    "Account" a,
                                    "BankConnection" bc
                               WHERE t.id = assigned.transaction_id
                                 AND a.id = t.account_id
                                 AND bc.id = a.bank_connection_id
                                 AND bc.user_id = @UserId
                                 AND t.tag_source = 'RULE'::"TagSource"
                                 AND t.tag_id = @OtherTagId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                TransactionIds = tags.Select(tag => tag.TransactionId).ToArray(),
                TagIds = tags.Select(tag => tag.TagId).ToArray(),
                OtherTagId = otherTagId,
                TaggedAt = taggedAt
            });
        }

        public async Task<int> ReopenAiOtherAsync(long userId,
                                                  long otherTagId)
        {
            const string sql = """
                               UPDATE "Transaction" AS t
                               SET tag_source = 'RULE'::"TagSource"
                               FROM "Account" a,
                                    "BankConnection" bc
                               WHERE a.id = t.account_id
                                 AND bc.id = a.bank_connection_id
                                 AND bc.user_id = @UserId
                                 AND t.tag_source = 'AI'::"TagSource"
                                 AND t.tag_id = @OtherTagId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new { UserId = userId, OtherTagId = otherTagId });
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
