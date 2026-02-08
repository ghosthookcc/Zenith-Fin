using Microsoft.AspNetCore.Mvc;
using ZenithFin.EnableBanking;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;
using ZenithFin.Api.Auth;
using ZenithFin.Utility;
using ZenithFin.PostgreSQL.Models.Dtos;
using System.Security.Claims;

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

        [HttpGet]
        [ResourceGuard]
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
        [Route("users/session")]
        public async Task<IActionResult> UserHasSession()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var authenticationHeader))
            {
                Console.WriteLine("No Authorization header!");
                return Unauthorized();
            }

            try
            {
                var token = authenticationHeader.ToString().Replace("Bearer ", "");
                if (string.IsNullOrEmpty(token))
                {
                    Console.WriteLine("No token in Authorization header!");
                    return Unauthorized();
                }

                ClaimsPrincipal? principal = await _jwtAuthenticator.ValidateJwtForSession(token);
                if (principal == null)
                {
                    Console.WriteLine("Token validation failed - invalid or expired session");
                    return Unauthorized();
                }

                Claim? userId = principal.FindFirst(ClaimTypes.NameIdentifier);
                if (userId == null)
                {
                    Console.WriteLine("No user ID in token claims");
                    return Unauthorized();
                }

                Console.WriteLine($"Session valid for user: {userId.Value}");

                return Ok(new
                {
                    success = true,
                    userId = userId.Value,
                    message = "Session is valid"
                });
            }
            catch (Exception errno)
            {
                Console.WriteLine(errno.Message);
                return Unauthorized();
            }
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
