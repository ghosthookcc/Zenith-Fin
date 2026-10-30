using Microsoft.AspNetCore.Mvc;

using ZenithFin.Api.Auth;
using ZenithFin.PostgreSQL.Models.Services;
using ZenithFin.Utility;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/exports")]
    public class Exports : ControllerBase
    {
        private readonly ExportService _exportService;

        private const int MaxExportYears = 10;

        [HttpGet]
        [ResourceGuard]
        [Route("accounts")]
        public async Task<IActionResult> ExportAccounts([FromQuery] string? format)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            CsvDialect? dialect = CsvDialect.FromFormat(format);

            if (dialect == null)
                return BadRequest(new { message = "The format must be 'excel' or 'standard'", success = false });

            ExportFile? file = await _exportService.ExportAccountsAsync(sessionId, dialect);

            if (file == null) return Unauthorized("No user found for session");

            Response.Headers.CacheControl = "no-store";

            return File(file.Content, "text/csv; charset=utf-8", file.FileName);
        }

        [HttpGet]
        [ResourceGuard]
        [Route("transactions")]
        public async Task<IActionResult> ExportTransactions([FromQuery] DateOnly? from,
                                                            [FromQuery] DateOnly? to,
                                                            [FromQuery] string? format)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            CsvDialect? dialect = CsvDialect.FromFormat(format);

            if (dialect == null)
                return BadRequest(new { message = "The format must be 'excel' or 'standard'", success = false });

            if (from == null || to == null)
                return BadRequest(new { message = "Choose both a start and an end date", success = false });

            if (to < from)
                return BadRequest(new { message = "The end date must not be before the start date", success = false });

            if (from.Value.AddYears(MaxExportYears) <= to.Value)
                return BadRequest(new { message = $"An export can span at most {MaxExportYears} years", success = false });

            ExportFile? file = await _exportService.ExportTransactionsAsync(sessionId, from.Value, to.Value, dialect);

            if (file == null) return Unauthorized("No user found for session");

            Response.Headers.CacheControl = "no-store";

            return File(file.Content, "text/csv; charset=utf-8", file.FileName);
        }

        public Exports(ExportService exportService)
        {
            _exportService = exportService;
        }
    }
}
