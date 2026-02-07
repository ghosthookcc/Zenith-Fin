using Microsoft.AspNetCore.Mvc;
using ZenithFin.EnableBanking;
using ZenithFin.Api.Models.Dtos;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using ZenithFin.PostgreSQL.Models.Services;
using System.Threading.Tasks;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/auth")]
    public class Auth : ControllerBase
    {
        private readonly EnableBankingWorkspace _workspace;

        private readonly UserService _userService;

        [HttpGet]
        [Route("aspsp/auth")]
        public IActionResult AuthenticateBankSessionStart([FromQuery] string code,
                                                          [FromQuery] string state,
                                                          [FromQuery] string? error = null)
        {
            // validateBankingRedirect();
            return Ok();
        }

        [HttpPost]
        [Route("users/register")]
        public async Task<IActionResult> RegisterUser([FromBody] RegisterDto.RegisterRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
                {
                    return BadRequest(new RegisterDto.RegisterResponse
                    { 
                        Message = "Email and password are required",
                        Success = false,
                        Code = StatusCodes.Status400BadRequest
                    });
                }

                // Your registration logic here
                // e.g., check if user exists, hash password, save to database

                // Example: User already exists
                // return Conflict(new
                // {
                //     message = "User already exists",
                //     success = false
                // });

                await _userService.RegisterAsync(request);

                return Ok(new RegisterDto.RegisterResponse
                {
                    Message = "Registration successful",
                    Success = true,
                    Code = StatusCodes.Status200OK
                });
            }
            catch (Exception errno)
            {
                Console.WriteLine(errno.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new RegisterDto.RegisterResponse
                {
                    Message = "Internal server error",
                    Success = false,
                    Code = StatusCodes.Status500InternalServerError
                });
            }
        }

        [HttpGet]
        [Route("users/login")]
        public IActionResult AuthenticateUserSession(string returnUrl = "https://localhost:4444/dashboard")
        {
            return BadRequest();
        }

        public Auth(EnableBankingWorkspace workspace, 
                    UserService userService)
        {
            _workspace = workspace;

            _userService = userService;
        }
    }
}
