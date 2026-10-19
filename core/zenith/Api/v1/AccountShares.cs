using Microsoft.AspNetCore.Mvc;

using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/account-shares")]
    public class AccountShares : ControllerBase
    {
        private readonly AccountShareService _accountShareService;

        [HttpGet]
        [ResourceGuard]
        public async Task<IActionResult> GetShares()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            AccountShareDto.AccountSharesResponse? shares = await _accountShareService.GetSharesAsync(sessionId);

            if (shares == null) return Unauthorized("No user found for session");

            return Ok(shares);
        }

        [HttpPost]
        [ResourceGuard]
        public async Task<IActionResult> CreateShare([FromBody] AccountShareDto.SaveAccountShareRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _accountShareService.CreateAsync(sessionId, request));
        }

        [HttpPut]
        [ResourceGuard]
        [Route("{id:long}")]
        public async Task<IActionResult> UpdateShare(long id,
                                                     [FromBody] AccountShareDto.SaveAccountShareRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _accountShareService.UpdateAsync(sessionId, id, request));
        }

        [HttpDelete]
        [ResourceGuard]
        [Route("{id:long}")]
        public async Task<IActionResult> DeleteShare(long id)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _accountShareService.DeleteAsync(sessionId, id));
        }

        [HttpGet]
        [ResourceGuard]
        [Route("{id:long}/personal-number")]
        public async Task<IActionResult> RevealPersonalNumber(long id)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            PersonalNumberResult result = await _accountShareService.RevealPersonalNumberAsync(sessionId, id);

            Response.Headers.CacheControl = "no-store";

            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return Ok(new AccountShareDto.PersonalNumberResponse { PersonalNumber = result.PersonalNumber! });
        }

        private IActionResult ToResponse(AccountShareResult result)
        {
            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return result.Share == null ? Ok(new { success = true }) : Ok(result.Share);
        }

        public AccountShares(AccountShareService accountShareService)
        {
            _accountShareService = accountShareService;
        }
    }
}
