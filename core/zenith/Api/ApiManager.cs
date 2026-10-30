using Dapper;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using ZenithFin.Api.Auth;
using ZenithFin.EnableBanking;
using ZenithFin.AI;
using ZenithFin.PostgreSQL;
using ZenithFin.PostgreSQL.Models.Entities;
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

            builder.Services.AddSingleton<BankingRepository>();
            builder.Services.AddSingleton<BankingService>();

            builder.Services.AddSingleton<TransactionRepository>();
            builder.Services.AddSingleton<LedgerRepository>();
            builder.Services.AddSingleton<LedgerService>();
            builder.Services.AddSingleton<SharingRepository>();
            builder.Services.AddSingleton<SharingService>();
            builder.Services.AddSingleton<EconomyService>();
            builder.Services.AddSingleton<TransactionService>();
            builder.Services.AddHostedService<TransactionSyncJob>();

            builder.Services.AddSingleton<CreditAccountRepository>();
            builder.Services.AddSingleton<CreditAccountService>();

            builder.Services.AddSingleton<ExpectedSalaryRepository>();
            builder.Services.AddSingleton<ExpectedSalaryService>();

            builder.Services.AddSingleton<AccountAliasRepository>();
            builder.Services.AddSingleton<AccountAliasService>();

            builder.Services.AddSingleton<RecurringRuleRepository>();
            builder.Services.AddSingleton<RecurringService>();

            builder.Services.AddSingleton<VirtualAccountRepository>();
            builder.Services.AddSingleton<VirtualAccountService>();

            builder.Services.AddSingleton<ManualTransactionRepository>();
            builder.Services.AddSingleton<ManualTransactionService>();

            builder.Services.AddSingleton<AccountShareRepository>();
            builder.Services.AddSingleton<AccountShareService>();

            builder.Services.AddSingleton<ExportRepository>();
            builder.Services.AddSingleton<ExportService>();

            builder.Services.AddSingleton<RecurringPaymentsService>();

            builder.Services.Configure<AvanzaOptions>(builder.Configuration.GetSection("Avanza"));
            builder.Services.AddSingleton<AvanzaRepository>();
            builder.Services.AddSingleton<AvanzaService>();

            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy(AvanzaService.CredentialsRateLimit, context =>
                    RateLimitPartition.GetFixedWindowLimiter(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString()))),
                                                             _ => new FixedWindowRateLimiterOptions
                                                             {
                                                                 PermitLimit = 5,
                                                                 Window = TimeSpan.FromMinutes(15),
                                                                 QueueLimit = 0
                                                             }));
            });

            builder.Services.AddSingleton<TagRepository>();
            builder.Services.AddSingleton<CorrectionRepository>();
            builder.Services.AddSingleton<CategorizationService>();
            builder.Services.AddSingleton<CorrectionService>();
            builder.Services.AddSingleton<TagService>();

            builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("AI"));
            builder.Services.AddSingleton<AiClient>();
            builder.Services.AddSingleton<AiCategorizationService>();

            builder.Services.AddSingleton<JwtAuthenticator>();

            builder.Services.Configure<EnableBankingOptions>(builder.Configuration.GetSection("EnableBanking"));
            builder.Services.AddTransient<EnableBankingWorkspace>();

            builder.Services.AddDataProtection()
                            .PersistKeysToFileSystem(new DirectoryInfo("./"))
                            .SetApplicationName("ZenithFin");

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

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
            _app.UseRateLimiter();

            _app.UseCors("Frontend");
            _app.UseCookiePolicy();

            _app.UseAuthentication();
            _app.UseAuthorization();

            _app.MapControllers();

            await _app!.RunAsync();
        }
    }
}
