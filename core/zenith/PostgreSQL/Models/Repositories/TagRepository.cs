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
                                   user_id IS NULL AS "IsSystem"
                               FROM "Tag"
                               WHERE user_id IS NULL
                                  OR user_id = @UserId
                               ORDER BY user_id NULLS FIRST, id;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<TagDto.Tag> tags = await connection.QueryAsync<TagDto.Tag>(sql, new { UserId = userId });

            return tags.ToArray();
        }
    }
}
