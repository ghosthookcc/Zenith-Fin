using System.Globalization;

using Microsoft.AspNetCore.Mvc;

using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/economy")]
    public class Economy : ControllerBase
    {
        private readonly EconomyService _economyService;

        [HttpGet]
        [ResourceGuard]
        public async Task<IActionResult> GetSummary([FromQuery] string? from,
                                                    [FromQuery] string? to)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly start) ||
                !DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly end) ||
                end < start ||
                end.DayNumber - start.DayNumber > 400)
                return BadRequest(new { message = "Choose a period of at most about a year", success = false });

            EconomyDto.Summary? summary = await _economyService.GetSummaryAsync(sessionId, start, end);

            return summary == null ? Unauthorized("No user found for session") : Ok(summary);
        }

        public Economy(EconomyService economyService)
        {
            _economyService = economyService;
        }
    }
}
