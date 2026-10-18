using System.Globalization;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Repositories;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record ExpectedSalaryResult(int StatusCode,
                                              string? Message,
                                              ExpectedSalaryDto.ExpectedSalary? ExpectedSalary = null);

    public sealed class ExpectedSalaryService
    {
        private readonly ExpectedSalaryRepository _expectedSalaryRepository;
        private readonly UserRepository _userRepository;

        private const decimal MaxAmount = 1_000_000_000m;

        private static readonly ExpectedSalaryResult Unauthorized = new(StatusCodes.Status401Unauthorized, "No user found for session");

        public ExpectedSalaryService(ExpectedSalaryRepository expectedSalaryRepository,
                                     UserRepository userRepository)
        {
            _expectedSalaryRepository = expectedSalaryRepository;
            _userRepository = userRepository;
        }

        public async Task<ExpectedSalaryResult> SaveAsync(string sessionId,
                                                          string month,
                                                          ExpectedSalaryDto.SaveExpectedSalaryRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            if (ParseMonth(month) is not DateOnly start) return UnknownMonth();

            if (request.Amount is not decimal amount || amount < 0 || amount > MaxAmount)
                return new ExpectedSalaryResult(StatusCodes.Status400BadRequest, "The expected salary must be between 0 and 1 000 000 000 kr");

            decimal rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

            await _expectedSalaryRepository.SaveExpectedSalaryAsync(userId.Value, start, rounded, DateTime.UtcNow);

            return new ExpectedSalaryResult(StatusCodes.Status200OK, null, new ExpectedSalaryDto.ExpectedSalary
            {
                Month = start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Amount = rounded
            });
        }

        public async Task<ExpectedSalaryResult> DeleteAsync(string sessionId,
                                                            string month)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            if (ParseMonth(month) is not DateOnly start) return UnknownMonth();

            return await _expectedSalaryRepository.DeleteExpectedSalaryAsync(userId.Value, start)
                ? new ExpectedSalaryResult(StatusCodes.Status200OK, null)
                : new ExpectedSalaryResult(StatusCodes.Status404NotFound, "No expected salary starts that month");
        }

        private static DateOnly? ParseMonth(string month)
        {
            return DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly start)
                ? start
                : null;
        }

        private static ExpectedSalaryResult UnknownMonth()
        {
            return new ExpectedSalaryResult(StatusCodes.Status400BadRequest, "Months must look like 2026-10");
        }

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }
    }
}
