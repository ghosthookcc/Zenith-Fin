using Microsoft.AspNetCore.Mvc;

using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/virtual-accounts")]
    public class VirtualAccounts : ControllerBase
    {
        private readonly VirtualAccountService _virtualAccountService;

        [HttpGet]
        [ResourceGuard]
        public async Task<IActionResult> GetVirtualAccounts()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            VirtualAccountDto.VirtualAccountsResponse? virtualAccounts = await _virtualAccountService.GetVirtualAccountsAsync(sessionId);

            if (virtualAccounts == null) return Unauthorized("No user found for session");

            return Ok(virtualAccounts);
        }

        [HttpPost]
        [ResourceGuard]
        public async Task<IActionResult> CreateVirtualAccount([FromBody] VirtualAccountDto.SaveVirtualAccountRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _virtualAccountService.CreateAsync(sessionId, request));
        }

        [HttpPut]
        [ResourceGuard]
        [Route("{id:long}")]
        public async Task<IActionResult> UpdateVirtualAccount(long id,
                                                             [FromBody] VirtualAccountDto.SaveVirtualAccountRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _virtualAccountService.UpdateAsync(sessionId, id, request));
        }

        [HttpDelete]
        [ResourceGuard]
        [Route("{id:long}")]
        public async Task<IActionResult> DeleteVirtualAccount(long id)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _virtualAccountService.DeleteAsync(sessionId, id));
        }

        private IActionResult ToResponse(VirtualAccountResult result)
        {
            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return result.VirtualAccount == null ? Ok(new { success = true }) : Ok(result.VirtualAccount);
        }

        public VirtualAccounts(VirtualAccountService virtualAccountService)
        {
            _virtualAccountService = virtualAccountService;
        }
    }
}
