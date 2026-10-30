using Microsoft.AspNetCore.Mvc;

using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/recurring")]
    public class RecurringPayments : ControllerBase
    {
        private readonly RecurringPaymentsService _recurringPaymentsService;

        [HttpGet]
        [ResourceGuard]
        public async Task<IActionResult> GetRecurringPayments()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            RecurringDto.Overview? overview = await _recurringPaymentsService.GetOverviewAsync(sessionId);

            if (overview == null) return Unauthorized("No user found for session");

            return Ok(overview);
        }

        public RecurringPayments(RecurringPaymentsService recurringPaymentsService)
        {
            _recurringPaymentsService = recurringPaymentsService;
        }
    }
}
