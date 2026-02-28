using Microsoft.AspNetCore.Mvc;
using System.Reflection.Metadata.Ecma335;
using System.Security.Claims;
using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;

using ZenithFin.EnableBanking;

using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/auth")]
    public class Auth : ControllerBase
    {
        private readonly EnableBankingWorkspace _workspace;

        private readonly UserService _userService;
        private readonly BankingService _bankingService;

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
            string? authenticationHeader = Request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(authenticationHeader) && authenticationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                string token = authenticationHeader.Substring("Bearer ".Length).Trim();
                ClaimsPrincipal? principal = await _jwtAuthenticator.ValidateJwtForSession(token);
                if (principal != null)
                {
                    string? sessionId = principal.FindFirst(ClaimTypes.Sid)?.Value;
                    if (await _userService.IsUserSessionActiveAsync(sessionId))
                    {
                        return BadRequest(new LoginDto.LoginResponse
                        {
                            Message = "User already has an active session",
                            Success = false,
                            Code = StatusCodes.Status403Forbidden
                        });
                    }
                }
            }

            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return BadRequest(new LoginDto.LoginResponse
                {
                    Message = "Email and password are required",
                    Success = false,
                    Code = StatusCodes.Status400BadRequest,
                    JwtLifeSpanInSeconds = 0
                });
            }

            long? userId = await _userService.ValidateLoginAsync(request);
            if (userId == null)
            {
                return Unauthorized(new LoginDto.LoginResponse()
                {
                    Message = "Password or email did not match a existing user",
                    Success = false,
                    Code = StatusCodes.Status401Unauthorized,
                    JwtLifeSpanInSeconds = 0
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
                Code = StatusCodes.Status302Found,
                JwtLifeSpanInSeconds = (DateTime.Now - session.ExpiresAt).TotalSeconds
            });
        }

        [HttpGet]
        [ResourceGuard]
        [Route("users/session")]
        public IActionResult UserHasSession() 
        {
            string? userId = HttpContext.Items["UserId"] as string;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized("No session ID found");
            return Ok(new
            {
                success = true,
                userId = userId,
                message = "Session is valid"
            });
        }

        [HttpPost]
        [ResourceGuard]
        [Route("aspsp/connect")]
        public async Task<IActionResult> AuthenticateAspsp([FromBody] AspspDto.AuthenticationRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;
            if (string.IsNullOrEmpty(sessionId))
                return Unauthorized("No session ID found");
            Console.WriteLine("SessionId: " + sessionId);

            if (request?.Aspsps == null || !request.Aspsps.Any())
            {
                return BadRequest("No ASPSP(s) provided");
            }

            List<AspspDto.AspspUrl> pendingUrls = new ();
            foreach (AspspDto.AuthenticationAspsp aspsp in request.Aspsps)
            {
                string state = Guid.NewGuid().ToString();
                DateTime? expiresAt = await _userService.GetSessionExpirationDate(sessionId);

                AspspAuthenticationAttempt attempt = await _workspace.Authenticator.Authenticate(aspsp,
                                                                                                 expiresAt,
                                                                                                 state);
                if (attempt.success && attempt.url != null)
                {
                    AspspDto.AspspUrl url = new () 
                    { 
                        Bank = aspsp.Bank, 
                        Url = attempt.url 
                    };
                    pendingUrls.Add(url);

                    await _bankingService.StartPendingAspspAuthenticationAsync(aspsp,
                                                                               state,
                                                                               sessionId);
                }
            }

            return Ok(new AspspDto.AuthenticationResponse()
            {
                Message = "Created authentication callback urls",
                Success = true,
                Code = StatusCodes.Status200OK,
                Urls = pendingUrls
            });
        }

        public Auth(EnableBankingWorkspace workspace,
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
