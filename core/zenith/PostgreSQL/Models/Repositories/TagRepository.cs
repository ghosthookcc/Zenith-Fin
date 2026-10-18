using System.Data;

using Dapper;

using ZenithFin.Api.Models.Dtos;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class TagRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public TagRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<TagDto.Tag[]> GetTagsForUserAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   id AS "Id",
                                   key AS "Key",
                                   name AS "Name",
                                   icon AS "Icon",
                                   color AS "Color",
                                   kind::text AS "Kind",
                                   user_id IS NULL AS "IsSystem",
                                   COALESCE(keywords, ARRAY[]::text[]) AS "Keywords"
                               FROM "Tag"
                               WHERE user_id IS NULL
                                  OR user_id = @UserId
                               ORDER BY user_id NULLS FIRST, id;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<TagDto.Tag> tags = await connection.QueryAsync<TagDto.Tag>(sql, new { UserId = userId });

            return tags.ToArray();
        }

        public async Task<Dictionary<long, int>> GetTagUsageAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   t.tag_id AS "TagId",
                                   count(*)::int AS "Count"
                               FROM "Transaction" t
                               INNER JOIN "Account" a
                                   ON a.id = t.account_id
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE bc.user_id = @UserId
                                 AND t.tag_id IS NOT NULL
                               GROUP BY t.tag_id;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<(long TagId, int Count)> usage = await connection.QueryAsync<(long TagId, int Count)>(sql, new { UserId = userId });

            return usage.ToDictionary(row => row.TagId, row => row.Count);
        }

        public async Task<long> InsertTagAsync(long userId,
                                               string name,
                                               string icon,
                                               string color,
                                               string kind,
                                               string[] keywords)
        {
            const string sql = """
                               INSERT INTO "Tag"
                               (user_id, name, icon, color, kind, keywords)
                               VALUES
                               (@UserId, @Name, @Icon, @Color, @Kind::"TagKind", @Keywords)
                               RETURNING id;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleAsync<long>(sql, new
            {
                UserId = userId,
                Name = name,
                Icon = icon,
                Color = color,
                Kind = kind,
                Keywords = keywords
            });
        }

        public async Task<bool> UpdateTagAsync(long userId,
                                               long tagId,
                                               string name,
                                               string icon,
                                               string color,
                                               string kind,
                                               string[] keywords)
        {
            const string sql = """
                               UPDATE "Tag"
                               SET name = @Name,
                                   icon = @Icon,
                                   color = @Color,
                                   kind = @Kind::"TagKind",
                                   keywords = @Keywords
                               WHERE id = @TagId
                                 AND user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                TagId = tagId,
                Name = name,
                Icon = icon,
                Color = color,
                Kind = kind,
                Keywords = keywords
            }) > 0;
        }

        public async Task<bool> DeleteTagAsync(long userId,
                                               long tagId)
        {
            using var connection = _connectionFactory.Create();

            if (connection.State != ConnectionState.Open) connection.Open();

            using var transaction = connection.BeginTransaction();

            const string lockSql = """
                                   SELECT id
                                   FROM "Tag"
                                   WHERE id = @TagId
                                     AND user_id = @UserId
                                   FOR UPDATE;
                                   """;

            long? ownedTag = await connection.QuerySingleOrDefaultAsync<long?>(lockSql,
                                                                              new { UserId = userId, TagId = tagId },
                                                                              transaction);

            if (ownedTag == null) return false;

            const string releaseSql = """
                                      UPDATE "Transaction"
                                      SET tag_id = NULL,
                                          tag_source = NULL,
                                          tagged_at = NULL
                                      WHERE tag_id = @TagId;
                                      """;

            await connection.ExecuteAsync(releaseSql, new { TagId = tagId }, transaction);

            const string deleteSql = """
                                     DELETE FROM "Tag"
                                     WHERE id = @TagId
                                       AND user_id = @UserId;
                                     """;

            await connection.ExecuteAsync(deleteSql, new { UserId = userId, TagId = tagId }, transaction);

            transaction.Commit();

            return true;
        }
    }
}
