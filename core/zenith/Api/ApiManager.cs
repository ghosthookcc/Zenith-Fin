using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Mvc;
using ZenithFin.Api.Auth;
using ZenithFin.EnableBanking;
using ZenithFin.PostgreSQL;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api
{
    public class ApiManager
    {
        private WebApplication? _app;
        public async Task StartAsync()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();

            builder.Services.AddSingleton<IDatabaseConnectionFactory, DatabaseConnectionFactory>();

            builder.Services.AddSingleton<UserRepository>();
            builder.Services.AddSingleton<UserService>();

            builder.Services.AddTransient<JwtAuthenticator>();

            builder.Services.AddDataProtection();

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            builder.Services.AddScoped(scopedService => new EnableBankingWorkspace("EnableBanking/workspace.json"));

            builder.Services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions = true;
            });

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("Frontend", policy =>
                {
                    policy.WithOrigins("https://localhost:4444")
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials()
                          .Build();
                });
            });

            builder.Services.Configure<CookiePolicyOptions>(options =>
            {
                options.MinimumSameSitePolicy = SameSiteMode.None;
                options.HttpOnly = HttpOnlyPolicy.Always;
                options.Secure = CookieSecurePolicy.Always;
            });
            builder.Services.AddControllersWithViews();

            _app = builder.Build();

            if (_app.Environment.IsDevelopment())
            {
                _app.UseSwagger();
                _app.UseSwaggerUI();
            }

            _app.UseHttpsRedirection();
            _app.UseRouting();

            _app.UseCors("Frontend");
            _app.UseCookiePolicy();

            _app.UseAuthentication();
            _app.UseAuthorization();

            _app.Use(async (context, next) =>
            {
                Console.WriteLine("=== Incoming Request ===");
                Console.WriteLine($"{context.Request.Method} {context.Request.Path}");
                Console.WriteLine("Headers:");
                foreach (var header in context.Request.Headers)
                {
                    Console.WriteLine($"{header.Key}: {header.Value}");
                }
                Console.WriteLine("Cookies:");
                foreach (var cookie in context.Request.Cookies)
                {
                    Console.WriteLine($"{cookie.Key}: {cookie.Value}");
                }
                Console.WriteLine("=======================");

                await next();
            });

            _app.MapControllers();

            await _app!.RunAsync();
        }
    }
}
