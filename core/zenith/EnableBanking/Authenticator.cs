using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using ZenithFin.Utility;

using static ZenithFin.EnableBanking.EnableBankingEntities;

namespace ZenithFin.EnableBanking
{
    internal class Authenticator
    {
        internal Workspace workspace;
        internal Client client;

        private readonly object _lock = new();

        private readonly string _jwtAudience = "api.enablebanking.com";
        private readonly string _jwtIssuer = "enablebanking.com";

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

        private void RotateToken()
        {
            _jwt = GenerateToken();

            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(_jwt);

            _expiresAt = token.ValidTo;
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
                                                expires: now.AddMinutes(5),
                                                signingCredentials: signingCredentials);
            jsonWebToken.Header.Add("kid", applicationId);

            string token = new JwtSecurityTokenHandler().WriteToken(jsonWebToken);

            client.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            Console.WriteLine("Created application JwT:");
            Console.WriteLine(token + "\n");

            return token;
        }

        public async Task<bool> Authenticate()
        {
            if (workspace.config != null)
            {
                dynamic response = await Wrapper.GET.Application(client)
                                                    .SendAsync();

                Request.Authenticate authenticationBody = new(new Access(DateTime.UtcNow.AddDays(10)),
                                                               new Aspsp("Nordea", "SE"),
                                                               Guid.NewGuid().ToString(),
                                                               workspace.config
                                                                        .RootElement
                                                                        .GetProperty("EnableBanking")
                                                                        .GetProperty("redirectUrl")
                                                                        .GetString()!,
                                                               "personal");

                response = await Wrapper.POST.Authentication(client)
                                             .WithBody(authenticationBody)
                                             .SendAsync();
                Console.WriteLine($"To authenticate open URL {response.url}");
                return true;
            }
            return false;
        }

        internal Authenticator(Workspace workspace,
                               Client client)
        {
            this.workspace = workspace;
            this.client = client;

            client.Http.DefaultRequestHeaders
                       .Accept
                       .Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }
}
