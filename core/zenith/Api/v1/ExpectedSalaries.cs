using Microsoft.AspNetCore.Mvc;

using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/expected-salaries")]
    public class ExpectedSalaries : ControllerBase
    {
        private readonly ExpectedSalaryService _expectedSalaryService;

        [HttpPut]
        [ResourceGuard]
        [Route("{month}")]
        public async Task<IActionResult> SaveExpectedSalary(string month,
                                                            [FromBody] ExpectedSalaryDto.SaveExpectedSalaryRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _expectedSalaryService.SaveAsync(sessionId, month, request));
        }

        [HttpDelete]
        [ResourceGuard]
        [Route("{month}")]
        public async Task<IActionResult> DeleteExpectedSalary(string month)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            return ToResponse(await _expectedSalaryService.DeleteAsync(sessionId, month));
        }

        private IActionResult ToResponse(ExpectedSalaryResult result)
        {
            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return result.ExpectedSalary == null ? Ok(new { success = true }) : Ok(result.ExpectedSalary);
        }

        public ExpectedSalaries(ExpectedSalaryService expectedSalaryService)
        {
            _expectedSalaryService = expectedSalaryService;
        }
    }
}
