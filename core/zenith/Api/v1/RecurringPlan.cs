using Microsoft.AspNetCore.Mvc;

using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/recurring/plan")]
    public class RecurringPlan : ControllerBase
    {
        private readonly RecurringPlanService _recurringPlanService;

        [HttpPost]
        [ResourceGuard]
        [Route("items")]
        public async Task<IActionResult> AddPlanItem([FromBody] RecurringDto.SavePlanItemRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _recurringPlanService.CreateItemAsync(sessionId, request));
        }

        [HttpPut]
        [ResourceGuard]
        [Route("items/{itemId:long}")]
        public async Task<IActionResult> UpdatePlanItem(long itemId,
                                                        [FromBody] RecurringDto.SavePlanItemRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _recurringPlanService.UpdateItemAsync(sessionId, itemId, request));
        }

        [HttpDelete]
        [ResourceGuard]
        [Route("items/{itemId:long}")]
        public async Task<IActionResult> DeletePlanItem(long itemId)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _recurringPlanService.DeleteItemAsync(sessionId, itemId));
        }

        private IActionResult ToResponse(RecurringPlanResult result)
        {
            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return Ok(new { success = true, id = result.ItemId });
        }

        public RecurringPlan(RecurringPlanService recurringPlanService)
        {
            _recurringPlanService = recurringPlanService;
        }
    }
}
