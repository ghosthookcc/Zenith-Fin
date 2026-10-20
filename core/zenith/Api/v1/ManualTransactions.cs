using Microsoft.AspNetCore.Mvc;

using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/manual-transactions")]
    public class ManualTransactions : ControllerBase
    {
        private readonly ManualTransactionService _manualTransactionService;

        [HttpGet]
        [ResourceGuard]
        [Route("accounts")]
        public async Task<IActionResult> GetLinkableAccounts()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            ManualTransactionDto.LinkableAccountsResponse? accounts = await _manualTransactionService.GetLinkableAccountsAsync(sessionId);

            if (accounts == null) return Unauthorized("No user found for session");

            return Ok(accounts);
        }

        [HttpPost]
        [ResourceGuard]
        public async Task<IActionResult> CreateManualTransaction([FromBody] ManualTransactionDto.CreateManualTransactionRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _manualTransactionService.CreateAsync(sessionId, request));
        }

        [HttpDelete]
        [ResourceGuard]
        [Route("{id:long}")]
        public async Task<IActionResult> DeleteManualTransaction(long id)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _manualTransactionService.DeleteAsync(sessionId, id));
        }

        private IActionResult ToResponse(ManualTransactionResult result)
        {
            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return result.ManualTransaction == null ? Ok(new { success = true }) : Ok(result.ManualTransaction);
        }

        public ManualTransactions(ManualTransactionService manualTransactionService)
        {
            _manualTransactionService = manualTransactionService;
        }
    }
}
