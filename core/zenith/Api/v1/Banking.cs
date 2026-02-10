using Microsoft.AspNetCore.Mvc;
using ZenithFin.EnableBanking;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;
using ZenithFin.Api.Auth;
using ZenithFin.Utility;
using ZenithFin.PostgreSQL.Models.Dtos;
using System.Security.Claims;
using System.Text.Json;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/aspsp")]
    public class Banking : ControllerBase
    {
        private readonly EnableBankingWorkspace _workspace;
        private readonly UserService _userService;
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
            return Ok(aspsps);
        }

        public Banking(EnableBankingWorkspace workspace,
                       UserService userService,
                       JwtAuthenticator jwtAuthenticator)
        {
            _workspace = workspace;
            _userService = userService;
            _jwtAuthenticator = jwtAuthenticator;
        }
    }
}
