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
    [Route("api/v{version:apiVersion}/auth")]
    public class Auth : ControllerBase
    {
        private readonly EnableBankingWorkspace _workspace;
        private readonly UserService _userService;
        private readonly JwtAuthenticator _jwtAuthenticator;

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

        [HttpPost]
        [Route("users/login")]
        public async Task<IActionResult> AuthenticateUserSession([FromBody] LoginDto.LoginRequest request)
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

            long? userId = await _userService.ValidateLoginAsync(request);
            if (userId == null)
            {
                return Unauthorized(new LoginDto.LoginResponse()
                {
                    Message = "Password or email did not match a existing user",
                    Success = false,
                    Code = StatusCodes.Status401Unauthorized
                });
            }

            SessionCreated session = await _userService.CreateSessionAsync((long)userId,
                                                                           _jwtAuthenticator.Protector);

            string jsonWebToken = _jwtAuthenticator.CreateJwtForSession(session.UserId,
                                                                        session.SessionId.ToString(),
                                                                        session.RawJwtSecret,
                                                                        session.ExpiresAt);

            Response.Cookies.Append("AuthToken", jsonWebToken,
                                    new CookieOptions
                                    {
                                        HttpOnly = true,
                                        Secure = true,
                                        SameSite = SameSiteMode.None,
                                        Expires = session.ExpiresAt
                                    });

            return Ok(new LoginDto.LoginResponse()
            {
                Message = "Login successful",
                Success = true,
                Url = "https://localhost:4444/dashboard",
                Code = StatusCodes.Status302Found
            });
        }

        [HttpGet]
        [ResourceGuard]
        [Route("users/session")]
        public void UserHasSession() { }

        [HttpPost]
        [ResourceGuard]
        [Route("aspsp/connect")]
        public IActionResult AuthenticateAspsp([FromBody] AspspDto.AuthenticationRequest request)
        {
            return Ok(request);
        }

        public Auth(EnableBankingWorkspace workspace,
                    UserService userService,
                    JwtAuthenticator jwtAuthenticator)
        {
            _workspace = workspace;
            _userService = userService;
            _jwtAuthenticator = jwtAuthenticator;
        }
    }
}
