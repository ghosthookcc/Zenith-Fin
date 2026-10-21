using Microsoft.AspNetCore.Mvc;

using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/sharing")]
    public class Sharing : ControllerBase
    {
        private readonly SharingService _sharingService;

        [HttpGet]
        [ResourceGuard]
        public async Task<IActionResult> GetState()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            SharingDto.State? state = await _sharingService.GetStateAsync(sessionId);

            return state == null ? Unauthorized("No user found for session") : Ok(state);
        }

        [HttpGet]
        [ResourceGuard]
        [Route("inbox-count")]
        public async Task<IActionResult> GetInboxCount()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            int? count = await _sharingService.CountInboxAsync(sessionId);

            return count == null ? Unauthorized("No user found for session") : Ok(new SharingDto.InboxCount { Count = count.Value });
        }

        [HttpPost]
        [ResourceGuard]
        [Route("partner")]
        public Task<IActionResult> InvitePartner([FromBody] SharingDto.InvitePartnerRequest request) =>
            Run(sessionId => _sharingService.InvitePartnerAsync(sessionId, request));

        [HttpPost]
        [ResourceGuard]
        [Route("partner/{id:long}/respond")]
        public Task<IActionResult> RespondPartner(long id, [FromBody] SharingDto.RespondRequest request) =>
            Run(sessionId => _sharingService.RespondPartnershipAsync(sessionId, id, request.Accept));

        [HttpPost]
        [ResourceGuard]
        [Route("partner/{id:long}/end")]
        public Task<IActionResult> EndPartner(long id) =>
            Run(sessionId => _sharingService.EndPartnershipAsync(sessionId, id));

        [HttpPost]
        [ResourceGuard]
        [Route("accounts/{id:long}/share")]
        public Task<IActionResult> ShareAccount(long id) =>
            Run(sessionId => _sharingService.ShareAccountAsync(sessionId, id));

        [HttpPost]
        [ResourceGuard]
        [Route("shares/{id:long}/respond")]
        public Task<IActionResult> RespondShare(long id, [FromBody] SharingDto.RespondRequest request) =>
            Run(sessionId => _sharingService.RespondShareAsync(sessionId, id, request.Accept));

        [HttpPost]
        [ResourceGuard]
        [Route("shares/{id:long}/end")]
        public Task<IActionResult> EndShare(long id) =>
            Run(sessionId => _sharingService.EndShareAsync(sessionId, id));

        [HttpPost]
        [ResourceGuard]
        [Route("split")]
        public Task<IActionResult> ProposeSplit([FromBody] SharingDto.ProposeSplitRequest request) =>
            Run(sessionId => _sharingService.ProposeSplitAsync(sessionId, request));

        [HttpPost]
        [ResourceGuard]
        [Route("split/{id:long}/respond")]
        public Task<IActionResult> RespondSplit(long id, [FromBody] SharingDto.RespondRequest request) =>
            Run(sessionId => _sharingService.RespondSplitAsync(sessionId, id, request.Accept));

        [HttpPost]
        [ResourceGuard]
        [Route("notifications/read")]
        public Task<IActionResult> MarkRead() =>
            Run(sessionId => _sharingService.MarkReadAsync(sessionId));

        private async Task<IActionResult> Run(Func<string, Task<SharingResult>> action)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            SharingResult result = await action(sessionId);

            return result.StatusCode == StatusCodes.Status200OK
                ? Ok(new { success = true })
                : StatusCode(result.StatusCode, new { message = result.Message, success = false });
        }

        public Sharing(SharingService sharingService)
        {
            _sharingService = sharingService;
        }
    }
}
