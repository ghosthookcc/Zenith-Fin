using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using ZenithFin.Utility;

using static ZenithFin.Api.Models.Dtos.AspspDto;
using static ZenithFin.EnableBanking.EnableBankingEntities;

namespace ZenithFin.EnableBanking
{
    struct AspspAuthenticationAttempt
    {
        public string? url;
        public bool success;
    }
    struct AspspAuthenticationCallbackAttempt
    {
        public string? sessionId;
        public DateTime? expiresAt;
        public bool success;
    }
    internal class Authenticator
    {
        internal Workspace workspace;
        internal Client client;

        private readonly object _lock = new();

        private readonly string _jwtAudience = "api.enablebanking.com";
        private readonly string _jwtIssuer = "enablebanking.com";

        private readonly string? _applicationId;
        private readonly string? _redirectUrl;

        private string? _jwt;
        private DateTime _expiresAt;

        private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(90);

        public string? GetToken()
        {
            lock (_lock)
            {
                if (_jwt != null && DateTime.UtcNow < _expiresAt - RefreshSkew) return _jwt;

                RotateToken();
                return _jwt;
            }
        }

        public async Task<AspspAuthenticationAttempt> Authenticate(AuthenticationAspsp aspsp,
                                                                   DateTime? expiresAt,
                                                                   string state)
        {
            if (workspace.config != null)
            {
                GetToken();

                dynamic response = await Wrapper.GET.Application(client)
                                                    .SendAsync();

                if (expiresAt == null)
                {
                    expiresAt = DateTime.UtcNow.AddDays(30);
                }

                Request.Authenticate authenticationBody = new(new Access(expiresAt.Value),
                                                              new Aspsp(aspsp.Bank, aspsp.Country),
                                                              state,
                                                              _redirectUrl!,
                                                              aspsp.PsuType);

                response = await Wrapper.POST.Authentication(client)
                                             .WithBody(authenticationBody)
                                             .SendAsync();
                Console.WriteLine($"To authenticate open URL {response.url}");
                return new AspspAuthenticationAttempt() 
                { 
                    url = response.url, 
                    success = true 
                };
            }
            return new AspspAuthenticationAttempt()
            {
                url = null,
                success = false
            };
        }

        public async Task<AspspAuthenticationCallbackAttempt> AuthenticateCallback(string code)
        {
            GetToken();

            Request.Sessions authenticationBody = new (code);

            dynamic response = await Wrapper.POST.Sessions(client)
                                                 .WithBody(authenticationBody)
                                                 .SendAsync();

            return new AspspAuthenticationCallbackAttempt
            {
                sessionId = response.sessionId,
                expiresAt = response.access.validUntil,
                success = true
            };
        }

        private void RotateToken()
        {
            _jwt = GenerateToken();

            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(_jwt);

            _expiresAt = token.ValidTo;

            /*
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string jsonString = JsonSerializer.Serialize(await Wrapper.GET.Aspsps(client)
                                                                          .SendAsync(), 
                                                         options);
            await File.WriteAllTextAsync("aspsps.json", jsonString);*/
        }

        private string GenerateToken()
        {
            string? keyPath = workspace.config!
                                       .RootElement
                                       .GetProperty("EnableBanking")
                                       .GetProperty("keyPath")
                                       .GetString();
            string? applicationId = workspace.config
                                             .RootElement
                                             .GetProperty("EnableBanking")
                                             .GetProperty("applicationId")
                                             .GetString();

            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(keyPath!));

            RsaSecurityKey rsaKey = new(rsa);

            SigningCredentials signingCredentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            };

            DateTime now = DateTime.UtcNow;
            long unixTimeNowInSeconds = new DateTimeOffset(now).ToUnixTimeSeconds();

            JwtSecurityToken jsonWebToken = new(audience: _jwtAudience,
                                                issuer: _jwtIssuer,
                                                claims: new[]
                                                {
                                                    new Claim(JwtRegisteredClaimNames.Iat,
                                                              unixTimeNowInSeconds.ToString(),
                                                              ClaimValueTypes.Integer64),
                                                    new Claim(JwtRegisteredClaimNames.Jti,
                                                              Guid.NewGuid().ToString())
                                                },
                                                expires: now.AddHours(23).AddMinutes(60.0 - RefreshSkew.Minutes),
                                                signingCredentials: signingCredentials);
            jsonWebToken.Header.Add("kid", applicationId);

            string token = new JwtSecurityTokenHandler().WriteToken(jsonWebToken);

            client.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            Console.WriteLine("Created application JwT:");
            Console.WriteLine(token + "\n");

            return token;
        }

        internal Authenticator(Workspace workspace,
                               Client client)
        {
            this.workspace = workspace;
            this.client = client;

            client.Http.DefaultRequestHeaders
                       .Accept
                       .Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (workspace.config != null)
            {
                _applicationId = workspace.config
                                          .RootElement
                                          .GetProperty("EnableBanking")
                                          .GetProperty("applicationId")
                                          .GetString()!;
                _redirectUrl = workspace.config
                                        .RootElement
                                        .GetProperty("EnableBanking")
                                        .GetProperty("redirectUrl")
                                        .GetString()!;
            }
        }
    }
}
