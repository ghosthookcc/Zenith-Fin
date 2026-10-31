using System.Data;

using Dapper;

using ZenithFin.Ledger;
using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class RecurringPlanRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        private const string ItemColumns = """
                                           id AS "Id",
                                           direction::text AS "Direction",
                                           name AS "Name",
                                           amount AS "Amount",
                                           every AS "Every",
                                           unit::text AS "Unit",
                                           to_char(last_date, 'YYYY-MM-DD') AS "LastDate",
                                           to_char(end_date, 'YYYY-MM-DD') AS "EndDate",
                                           series_key AS "SeriesKey"
                                           """;

        private const string InsertPlanSql = """
                                             INSERT INTO "RecurringPlan" (user_id)
                                             VALUES (@UserId)
                                             ON CONFLICT (user_id) DO NOTHING;
                                             """;

        private const string InsertItemSql = """
                                             INSERT INTO "RecurringPlanItem"
                                             (user_id, direction, name, amount, every, unit, last_date, end_date, series_key)
                                             VALUES
                                             (@UserId, @Direction::"PlanDirection", @Name, @Amount, @Every, @Unit::"PlanUnit",
                                              CAST(@LastDate AS date), CAST(@EndDate AS date), @SeriesKey)
                                             ON CONFLICT (user_id, series_key) DO NOTHING
                                             RETURNING id;
                                             """;

        private const string InsertPartSql = """
                                             INSERT INTO "RecurringPlanItemPart" (item_id, name, amount, position)
                                             VALUES (@ItemId, @Name, @Amount, @Position);
                                             """;

        private const string DeletePartsSql = """
                                              DELETE FROM "RecurringPlanItemPart"
                                              WHERE item_id = @ItemId;
                                              """;

        public RecurringPlanRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<bool> PlanExistsAsync(long userId)
        {
            const string sql = """
                               SELECT EXISTS (
                                   SELECT 1
                                   FROM "RecurringPlan"
                                   WHERE user_id = @UserId
                               );
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteScalarAsync<bool>(sql, new { UserId = userId });
        }

        public async Task<bool> CreatePlanAsync(long userId,
                                                RecurringPlanItemRow? seed)
        {
            using var connection = _connectionFactory.Create();

            if (connection.State != ConnectionState.Open) connection.Open();

            using var transaction = connection.BeginTransaction();

            bool created = await connection.ExecuteAsync(InsertPlanSql, new { UserId = userId }, transaction) > 0;

            if (created && seed != null)
                await connection.QuerySingleOrDefaultAsync<long?>(InsertItemSql, ItemParameters(userId, seed), transaction);

            transaction.Commit();

            return created;
        }

        public async Task<RecurringPlanItemRow[]> GetItemsAsync(long userId)
        {
            string sql = $"""
                          SELECT
                          {ItemColumns}
                          FROM "RecurringPlanItem"
                          WHERE user_id = @UserId
                          ORDER BY id;
                          """;

            const string partsSql = """
                                    SELECT
                                        p.id AS "Id",
                                        p.item_id AS "ItemId",
                                        p.name AS "Name",
                                        p.amount AS "Amount"
                                    FROM "RecurringPlanItemPart" p
                                    INNER JOIN "RecurringPlanItem" i
                                        ON i.id = p.item_id
                                    WHERE i.user_id = @UserId
                                    ORDER BY p.item_id, p.position, p.id;
                                    """;

            using var connection = _connectionFactory.Create();
            RecurringPlanItemRow[] items = (await connection.QueryAsync<RecurringPlanItemRow>(sql, new { UserId = userId })).ToArray();
            ILookup<long, RecurringPlanPartRow> parts = (await connection.QueryAsync<RecurringPlanPartRow>(partsSql, new { UserId = userId }))
                                                        .ToLookup(part => part.ItemId);

            foreach (RecurringPlanItemRow item in items)
                item.Parts = parts[item.Id].ToList();

            return items;
        }

        public async Task<RecurringPlanItemRow?> GetItemAsync(long userId,
                                                              long itemId)
        {
            string sql = $"""
                          SELECT
                          {ItemColumns}
                          FROM "RecurringPlanItem"
                          WHERE id = @ItemId
                            AND user_id = @UserId;
                          """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleOrDefaultAsync<RecurringPlanItemRow>(sql, new { UserId = userId, ItemId = itemId });
        }

        public async Task<int> CountItemsAsync(long userId)
        {
            const string sql = """
                               SELECT count(*)::int
                               FROM "RecurringPlanItem"
                               WHERE user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteScalarAsync<int>(sql, new { UserId = userId });
        }

        public async Task<long?> InsertItemAsync(long userId,
                                                 RecurringPlanItemRow item)
        {
            using var connection = _connectionFactory.Create();

            if (connection.State != ConnectionState.Open) connection.Open();

            using var transaction = connection.BeginTransaction();

            await connection.ExecuteAsync(InsertPlanSql, new { UserId = userId }, transaction);

            long? id = await connection.QuerySingleOrDefaultAsync<long?>(InsertItemSql, ItemParameters(userId, item), transaction);

            if (id is long itemId && item.Parts.Count > 0)
                await connection.ExecuteAsync(InsertPartSql, PartParameters(itemId, item.Parts), transaction);

            transaction.Commit();

            return id;
        }

        public async Task<bool> UpdateItemAsync(long userId,
                                                RecurringPlanItemRow item,
                                                bool replaceParts)
        {
            const string sql = """
                               UPDATE "RecurringPlanItem"
                               SET direction = @Direction::"PlanDirection",
                                   name = @Name,
                                   amount = @Amount,
                                   every = @Every,
                                   unit = @Unit::"PlanUnit",
                                   last_date = CAST(@LastDate AS date),
                                   end_date = CAST(@EndDate AS date),
                                   updated_at = now()
                               WHERE id = @ItemId
                                 AND user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();

            if (connection.State != ConnectionState.Open) connection.Open();

            using var transaction = connection.BeginTransaction();

            bool updated = await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                ItemId = item.Id,
                item.Direction,
                item.Name,
                item.Amount,
                item.Every,
                item.Unit,
                item.LastDate,
                item.EndDate
            }, transaction) > 0;

            if (!updated) return false;

            bool moneyIn = item.Direction == Effects.In;

            if (replaceParts || moneyIn)
                await connection.ExecuteAsync(DeletePartsSql, new { ItemId = item.Id }, transaction);

            if (replaceParts && !moneyIn && item.Parts.Count > 0)
                await connection.ExecuteAsync(InsertPartSql, PartParameters(item.Id, item.Parts), transaction);

            transaction.Commit();

            return true;
        }

        public async Task<bool> DeleteItemAsync(long userId,
                                                long itemId)
        {
            const string sql = """
                               DELETE FROM "RecurringPlanItem"
                               WHERE id = @ItemId
                                 AND user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new { UserId = userId, ItemId = itemId }) > 0;
        }

        private static IEnumerable<object> PartParameters(long itemId,
                                                         IReadOnlyList<RecurringPlanPartRow> parts)
        {
            return parts.Select((part, position) => new
            {
                ItemId = itemId,
                part.Name,
                part.Amount,
                Position = position
            });
        }

        private static object ItemParameters(long userId,
                                             RecurringPlanItemRow item)
        {
            return new
            {
                UserId = userId,
                item.Direction,
                item.Name,
                item.Amount,
                item.Every,
                item.Unit,
                item.LastDate,
                item.EndDate,
                item.SeriesKey
            };
        }
    }
}
