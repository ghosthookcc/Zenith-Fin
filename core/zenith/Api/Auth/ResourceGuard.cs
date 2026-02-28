using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace ZenithFin.Api.Auth
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ResourceGuardAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context,
                                                 ActionExecutionDelegate next)
        {
            HttpRequest Request = context.HttpContext.Request;

            if (!Request.Headers.TryGetValue("Authorization", out var authenticationHeader))
            {
                Console.WriteLine("No Authorization header!");
                context.Result = new UnauthorizedObjectResult(new
                {
                    message = "No authorization header provided",
                    success = false
                });
                return;
            }

            try
            {
                var token = authenticationHeader.ToString().Replace("Bearer ", "");
                if (string.IsNullOrEmpty(token))
                {
                    Console.WriteLine("No token in Authorization header!");
                    context.Result = new UnauthorizedObjectResult(new
                    {
                        message = "No token provided",
                        success = false
                    });
                    return;
                }

                JwtAuthenticator? jwtAuthenticator = context.HttpContext.RequestServices.GetService<JwtAuthenticator>();
                if (jwtAuthenticator == null)
                {
                    throw new InvalidOperationException("JwtAuthenticator sbe retrived as a service");
                }

                ClaimsPrincipal? principal = await jwtAuthenticator.ValidateJwtForSession(token);
                if (principal == null)
                {
                    Console.WriteLine("Token validation failed - invalid or expired session");
                    context.Result = new UnauthorizedObjectResult(new
                    {
                        message = "Invalid or expired session",
                        success = false
                    });
                    return;
                }

                Claim? userId = principal.FindFirst(ClaimTypes.NameIdentifier);
                if (userId == null)
                {
                    Console.WriteLine("No user ID in token claims");
                    context.Result = new UnauthorizedObjectResult(new
                    {
                        message = "Invalid token claims",
                        success = false
                    });
                    return;
                }

                Console.WriteLine($"Session valid for user: {userId.Value}");

                context.HttpContext.Items["UserId"] = userId.Value;
                context.HttpContext.Items["Principal"] = principal;
                context.HttpContext.Items["SessionId"] = principal.FindFirst(ClaimTypes.Sid)?.Value;

                await next();
            }
            catch (Exception errno)
            {
                Console.WriteLine(errno.Message);
                context.Result = new UnauthorizedObjectResult(new
                {
                    message = "Authentication failed",
                    error = errno.Message,
                    success = false
                });
                return;
            }
        }
    }
}
