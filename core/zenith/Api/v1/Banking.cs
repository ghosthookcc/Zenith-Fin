using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.EnableBanking;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;
using ZenithFin.Utility;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/aspsp")]
    public class Banking : ControllerBase
    {
        private readonly EnableBankingWorkspace _workspace;

        private readonly UserService _userService;
        private readonly BankingService _bankingService;

        private readonly JwtAuthenticator _jwtAuthenticator;

        [HttpGet]
        [ResourceGuard]
        [Route("all")]
        public IActionResult ListAspsps()
        {
            AspspDto.AllAspsps? aspsps = _workspace.config!
                                                   .RootElement
                                                   .GetProperty("EnableBanking")
                                                   .Deserialize<AspspDto.AllAspsps>();

            AspspDto.AspspsResponse response = new();
            response.Message = "Retrived ASPSPS";
            response.Success = true;
            response.Code = StatusCodes.Status200OK;

            if (aspsps != null) response.Aspsps = aspsps;

            return Ok(response);
        }

        [HttpGet]
        [ResourceGuard]
        [Route("all/inactive")]
        public async Task<IActionResult> ListInactiveAspsps()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;
            if (string.IsNullOrEmpty(sessionId))
                return Unauthorized("No session ID found");

            AspspDto.AllAspsps? aspsps = _workspace.config!
                                       .RootElement
                                       .GetProperty("EnableBanking")
                                       .Deserialize<AspspDto.AllAspsps>();

            AspspDto.AspspsResponse response = new();
            response.Message = "Retrived ASPSPS";
            response.Success = true;
            response.Code = StatusCodes.Status200OK;

            AspspDto.AllAspsps? inactiveAspsps = await _bankingService.GetInactiveAspsps(sessionId,
                                                                                         aspsps);
            if (inactiveAspsps != null) response.Aspsps = inactiveAspsps;

            return Ok(response);
        }

        [HttpPost]
        [ResourceGuard]
        [Route("auth/callback")]
        public async Task<IActionResult> HandleAuthCallback([FromBody] AspspDto.AspspAuthenticationCallbackRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;
            if (string.IsNullOrEmpty(sessionId))
                return Unauthorized("No session ID found");
            Console.WriteLine("Code: " + request.Code);

            AspspAuthenticationCallbackAttempt attempt = await _workspace.Authenticator.AuthenticateCallback(request.Code);
            if (attempt.success && attempt.sessionId != null && attempt.expiresAt != null)
            {
                await _bankingService.StartAspspSessionAsync(attempt.sessionId, attempt.expiresAt.Value,
                                                             sessionId,
                                                             request.State);
            }

            return Ok(new
            {
                ExpiresAt = attempt.expiresAt
            });
        }

        [HttpGet]
        [ResourceGuard]
        [Route("accounts/process")]
        public async Task<IActionResult> ProcessAccounts()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;
            if (string.IsNullOrEmpty(sessionId))
                return Unauthorized("No session ID found");
            bool attempt = await _workspace.Authenticator.FetchTransactions(sessionId);

            return Ok(attempt);
        }

        public Banking(EnableBankingWorkspace workspace,
                       UserService userService,
                       BankingService bankingService,
                       JwtAuthenticator jwtAuthenticator)
        {
            _workspace = workspace;
            _userService = userService;
            _bankingService = bankingService;
            _jwtAuthenticator = jwtAuthenticator;
        }
    }
}
