using Dapper;

using ZenithFin.Categorization;
using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class RecurringRuleRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public RecurringRuleRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<RecurringRuleRow[]> GetRulesAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   credit_debit_indicator AS "CreditDebitIndicator",
                                   own_transfer AS "OwnTransfer",
                                   signature AS "Signature",
                                   recurring AS "Recurring"
                               FROM "RecurringRule"
                               WHERE user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<RecurringRuleRow> rules = await connection.QueryAsync<RecurringRuleRow>(sql, new { UserId = userId });

            return rules.ToArray();
        }

        public async Task SaveRuleAsync(long userId,
                                        CorrectionKey key,
                                        bool recurring,
                                        DateTime savedAt)
        {
            if (savedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("Save time must be UTC.", nameof(savedAt));

            const string sql = """
                               INSERT INTO "RecurringRule"
                               (user_id, credit_debit_indicator, own_transfer, signature, recurring, updated_at)
                               VALUES
                               (@UserId, @CreditDebitIndicator, @OwnTransfer, @Signature, @Recurring, @SavedAt)
                               ON CONFLICT (user_id, credit_debit_indicator, own_transfer, signature)
                               DO UPDATE SET
                                   recurring = EXCLUDED.recurring,
                                   updated_at = EXCLUDED.updated_at;
                               """;

            using var connection = _connectionFactory.Create();
            await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                key.CreditDebitIndicator,
                key.OwnTransfer,
                key.Signature,
                Recurring = recurring,
                SavedAt = savedAt
            });
        }

        public async Task ClearRuleAsync(long userId,
                                         CorrectionKey key)
        {
            const string sql = """
                               DELETE FROM "RecurringRule"
                               WHERE user_id = @UserId
                                 AND credit_debit_indicator = @CreditDebitIndicator
                                 AND own_transfer = @OwnTransfer
                                 AND signature = @Signature;
                               """;

            using var connection = _connectionFactory.Create();
            await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                key.CreditDebitIndicator,
                key.OwnTransfer,
                key.Signature
            });
        }
    }
}
