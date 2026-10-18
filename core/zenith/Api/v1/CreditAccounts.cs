using Microsoft.AspNetCore.Mvc;

using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/credit-accounts")]
    public class CreditAccounts : ControllerBase
    {
        private readonly CreditAccountService _creditAccountService;

        [HttpGet]
        [ResourceGuard]
        public async Task<IActionResult> GetCreditAccounts()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            CreditAccountDto.CreditAccountsResponse? creditAccounts = await _creditAccountService.GetCreditAccountsAsync(sessionId);

            if (creditAccounts == null) return Unauthorized("No user found for session");

            return Ok(creditAccounts);
        }

        [HttpPost]
        [ResourceGuard]
        public async Task<IActionResult> CreateCreditAccount([FromBody] CreditAccountDto.SaveCreditAccountRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _creditAccountService.CreateAsync(sessionId, request));
        }

        [HttpPut]
        [ResourceGuard]
        [Route("{id:long}")]
        public async Task<IActionResult> UpdateCreditAccount(long id,
                                                             [FromBody] CreditAccountDto.SaveCreditAccountRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _creditAccountService.UpdateAsync(sessionId, id, request));
        }

        [HttpDelete]
        [ResourceGuard]
        [Route("{id:long}")]
        public async Task<IActionResult> DeleteCreditAccount(long id)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _creditAccountService.DeleteAsync(sessionId, id));
        }

        private IActionResult ToResponse(CreditAccountResult result)
        {
            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return result.CreditAccount == null ? Ok(new { success = true }) : Ok(result.CreditAccount);
        }

        public CreditAccounts(CreditAccountService creditAccountService)
        {
            _creditAccountService = creditAccountService;
        }
    }
}
