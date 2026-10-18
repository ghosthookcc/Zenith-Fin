using System.Globalization;

using Dapper;

using ZenithFin.Api.Models.Dtos;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class ExpectedSalaryRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public ExpectedSalaryRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<ExpectedSalaryDto.ExpectedSalary[]> GetExpectedSalariesForUserAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   to_char(month, 'YYYY-MM') AS "Month",
                                   amount AS "Amount"
                               FROM "ExpectedSalary"
                               WHERE user_id = @UserId
                               ORDER BY month;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<ExpectedSalaryDto.ExpectedSalary> salaries = await connection.QueryAsync<ExpectedSalaryDto.ExpectedSalary>(sql, new { UserId = userId });

            return salaries.ToArray();
        }

        public async Task SaveExpectedSalaryAsync(long userId,
                                                  DateOnly month,
                                                  decimal amount,
                                                  DateTime savedAt)
        {
            if (savedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("Save time must be UTC.", nameof(savedAt));

            const string sql = """
                               INSERT INTO "ExpectedSalary"
                               (user_id, month, amount, updated_at)
                               VALUES
                               (@UserId, CAST(@Month AS date), @Amount, @SavedAt)
                               ON CONFLICT (user_id, month)
                               DO UPDATE SET
                                   amount = EXCLUDED.amount,
                                   updated_at = EXCLUDED.updated_at;
                               """;

            using var connection = _connectionFactory.Create();
            await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                Month = FormatDate(month),
                Amount = amount,
                SavedAt = savedAt
            });
        }

        public async Task<bool> DeleteExpectedSalaryAsync(long userId,
                                                          DateOnly month)
        {
            const string sql = """
                               DELETE FROM "ExpectedSalary"
                               WHERE user_id = @UserId
                                 AND month = CAST(@Month AS date);
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new { UserId = userId, Month = FormatDate(month) }) > 0;
        }

        private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
