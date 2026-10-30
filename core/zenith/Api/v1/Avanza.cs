using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/avanza")]
    public class Avanza : ControllerBase
    {
        private readonly AvanzaService _avanzaService;

        [HttpPut]
        [ResourceGuard]
        [EnableRateLimiting(AvanzaService.CredentialsRateLimit)]
        [Route("credentials")]
        public async Task<IActionResult> SaveCredentials([FromBody] AvanzaDto.SaveCredentialsRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _avanzaService.SaveCredentialsAsync(sessionId, request));
        }

        [HttpDelete]
        [ResourceGuard]
        [EnableRateLimiting(AvanzaService.CredentialsRateLimit)]
        [Route("credentials")]
        public async Task<IActionResult> DeleteCredentials()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _avanzaService.DeleteCredentialsAsync(sessionId));
        }

        [HttpGet]
        [ResourceGuard]
        [Route("status")]
        public async Task<IActionResult> GetStatus()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            AvanzaDto.SyncStatus? status = await _avanzaService.GetStatusAsync(sessionId);

            if (status == null) return Unauthorized("No user found for session");

            Response.Headers.CacheControl = "no-store";

            return Ok(status);
        }

        [HttpGet]
        [ResourceGuard]
        [Route("balances")]
        public async Task<IActionResult> GetBalances()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            AvanzaDto.Balances? balances = await _avanzaService.GetBalancesAsync(sessionId);

            if (balances == null) return Unauthorized("No user found for session");

            Response.Headers.CacheControl = "no-store";

            return Ok(balances);
        }

        private IActionResult ToResponse(AvanzaResult result)
        {
            Response.Headers.CacheControl = "no-store";

            return result.StatusCode == StatusCodes.Status204NoContent
                ? NoContent()
                : StatusCode(result.StatusCode, new { message = result.Message, success = false });
        }

        public Avanza(AvanzaService avanzaService)
        {
            _avanzaService = avanzaService;
        }
    }
}
