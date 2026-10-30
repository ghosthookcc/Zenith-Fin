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

        public Exports(ExportService exportService)
        {
            _exportService = exportService;
        }
    }
}
