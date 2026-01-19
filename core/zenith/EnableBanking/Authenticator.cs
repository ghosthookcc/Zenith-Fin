using Microsoft.IdentityModel.Tokens;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using ZenithFin.Utility;

namespace ZenithFin.EnableBanking
{
    internal class Authenticator
    {
        internal Workspace workspace;
        internal Client client;

        private readonly string _jwtAudience = "api.enablebanking.com";
        private readonly string _jwtIssuer = "enablebanking.com";

        public string GenerateToken(string keyPath, string applicationId)
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(keyPath));

            RsaSecurityKey rsaKey = new(rsa);

            SigningCredentials signingCredentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            };

            DateTime now = DateTime.Now;
            long unixTimeNowInSeconds = new DateTimeOffset(now).ToUnixTimeSeconds();

            JwtSecurityToken jsonWebToken = new(audience: _jwtAudience,
                                                 issuer: _jwtIssuer,
                                                 claims: new[]
                                                 {
                                                     new Claim(JwtRegisteredClaimNames.Iat,
                                                               unixTimeNowInSeconds.ToString(),
                                                               ClaimValueTypes.Integer64)
                                                 },
                                                 expires: now.AddMinutes(30),
                                                 signingCredentials: signingCredentials);
            jsonWebToken.Header.Add("kid", applicationId);
            return new JwtSecurityTokenHandler().WriteToken(jsonWebToken);
        }

        public async Task<bool> Authenticate()
        {
            if (workspace.config != null)
            {
                string? keyPath = workspace.config
                                           .RootElement
                                           .GetProperty("EnableBanking")
                                           .GetProperty("keyPath")
                                           .GetString();
                string? applicationId = workspace.config
                                                 .RootElement
                                                 .GetProperty("EnableBanking")
                                                 .GetProperty("applicationId")
                                                 .GetString();

                string jsonWebToken = GenerateToken(keyPath!, applicationId!);
                Console.WriteLine("Created application JwT:");
                Console.WriteLine(jsonWebToken + "\n");
                client.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jsonWebToken);
                client.Http.DefaultRequestHeaders
                           .Accept
                           .Add(new MediaTypeWithQualityHeaderValue("application/json"));

                dynamic response = await Wrapper.GET.Application(client)
                                                    .SendAsync();

                Request.Authenticate authenticationBody = new (new EnableBankingDtos.Access(DateTime.UtcNow.AddDays(10)),
                                                               new EnableBankingDtos.Aspsp("Nordea", "SE"),
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

        internal Authenticator(Workspace workspace, Client client)
        {
            this.workspace = workspace;
            this.client = client;
        }
    }
}
