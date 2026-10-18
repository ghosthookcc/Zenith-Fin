using Microsoft.AspNetCore.Mvc;

using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/tags")]
    public class Tags : ControllerBase
    {
        private readonly TagService _tagService;

        [HttpGet]
        [ResourceGuard]
        public async Task<IActionResult> GetTags()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            TagDto.TagsResponse? tags = await _tagService.GetTagsAsync(sessionId);

            if (tags == null) return Unauthorized("No user found for session");

            return Ok(tags);
        }

        [HttpPost]
        [ResourceGuard]
        public async Task<IActionResult> CreateTag([FromBody] TagDto.SaveTagRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _tagService.CreateTagAsync(sessionId, request));
        }

        [HttpPut]
        [ResourceGuard]
        [Route("{id:long}")]
        public async Task<IActionResult> UpdateTag(long id,
                                                   [FromBody] TagDto.SaveTagRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _tagService.UpdateTagAsync(sessionId, id, request));
        }

        [HttpDelete]
        [ResourceGuard]
        [Route("{id:long}")]
        public async Task<IActionResult> DeleteTag(long id)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _tagService.DeleteTagAsync(sessionId, id));
        }

        private IActionResult ToResponse(TagOperationResult result)
        {
            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return result.Tag == null ? Ok(new { success = true }) : Ok(result.Tag);
        }

        public Tags(TagService tagService)
        {
            _tagService = tagService;
        }
    }
}
