using Dapper;

using ZenithFin.Categorization;
using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class CorrectionRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public CorrectionRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<TagCorrectionRow[]> GetCorrectionsAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   id AS "Id",
                                   credit_debit_indicator AS "CreditDebitIndicator",
                                   own_transfer AS "OwnTransfer",
                                   signature AS "Signature",
                                   description AS "Description",
                                   example AS "Example",
                                   tag_id AS "TagId",
                                   replaced_tag_id AS "ReplacedTagId",
                                   updated_at AS "UpdatedAt"
                               FROM "TagCorrection"
                               WHERE user_id = @UserId
                               ORDER BY updated_at DESC, id DESC;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<TagCorrectionRow> corrections = await connection.QueryAsync<TagCorrectionRow>(sql, new { UserId = userId });

            return corrections.ToArray();
        }

        public async Task<long> SaveCorrectionAsync(long userId,
                                                    CorrectionKey key,
                                                    string description,
                                                    string example,
                                                    long tagId,
                                                    long? replacedTagId,
                                                    DateTime savedAt)
        {
            if (savedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("Save time must be UTC.", nameof(savedAt));

            const string sql = """
                               INSERT INTO "TagCorrection"
                               (
                                   user_id,
                                   credit_debit_indicator,
                                   own_transfer,
                                   signature,
                                   description,
                                   example,
                                   tag_id,
                                   replaced_tag_id,
                                   created_at,
                                   updated_at
                               )
                               VALUES
                               (
                                   @UserId,
                                   @CreditDebitIndicator,
                                   @OwnTransfer,
                                   @Signature,
                                   @Description,
                                   @Example,
                                   @TagId,
                                   NULLIF(@ReplacedTagId, @TagId),
                                   @SavedAt,
                                   @SavedAt
                               )
                               ON CONFLICT (user_id, credit_debit_indicator, own_transfer, signature)
                               DO UPDATE SET
                                   description = EXCLUDED.description,
                                   example = EXCLUDED.example,
                                   tag_id = EXCLUDED.tag_id,
                                   replaced_tag_id = NULLIF(COALESCE(EXCLUDED.replaced_tag_id, "TagCorrection".replaced_tag_id), EXCLUDED.tag_id),
                                   updated_at = EXCLUDED.updated_at
                               RETURNING id;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleAsync<long>(sql, new
            {
                UserId = userId,
                key.CreditDebitIndicator,
                key.OwnTransfer,
                key.Signature,
                Description = description,
                Example = example,
                TagId = tagId,
                ReplacedTagId = replacedTagId,
                SavedAt = savedAt
            });
        }

        public async Task<bool> DeleteCorrectionAsync(long userId,
                                                      long correctionId)
        {
            const string sql = """
                               DELETE FROM "TagCorrection"
                               WHERE id = @CorrectionId
                                 AND user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new { UserId = userId, CorrectionId = correctionId }) > 0;
        }
    }
}
