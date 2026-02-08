using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace ZenithFin.Api.Auth
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ResourceGuardAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _cookieName;

        public ResourceGuardAttribute(string cookieName = "AuthToken")
        {
            _cookieName = cookieName;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, 
                                                 ActionExecutionDelegate next)
        {
            var cookies = context.HttpContext.Request.Cookies;

            if (!cookies.TryGetValue(_cookieName, out var token) || string.IsNullOrEmpty(token))
            {
                context.Result = new RedirectResult("https://localhost:4444/");
                return;
            }

            try
            {
                var jwtAuthenticator = context.HttpContext.RequestServices.GetRequiredService<JwtAuthenticator>();
                ClaimsPrincipal? principal = await jwtAuthenticator.ValidateJwtForSession(token);
                if (principal != null)
                {
                    context.HttpContext.User = principal;
                }
                await next();
            }
            catch
            {
                context.Result = new RedirectResult("https://localhost:4444/");
            }
        }
    }
}
