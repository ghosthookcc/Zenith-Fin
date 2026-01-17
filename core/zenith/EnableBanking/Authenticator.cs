using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ZenithFin.EnableBanking
{
    internal class Authenticator
    {
        private readonly string jwtAudience = "api.enablebanking.com";
        private readonly string jwtIssuer = "enablebanking.com";

        internal Workspace workspace;

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

            JwtSecurityToken jsonWebToken = new(audience: jwtAudience,
                                                 issuer: jwtIssuer,
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

        public async Task<bool> Authenticate(Client client)
        {
            if (workspace.config != null)
            {
                string keyPath = workspace.config["keyPath"];
                string applicationId = workspace.config["applicationId"];

                string jsonWebToken = GenerateToken(keyPath, applicationId);
                Console.WriteLine("Created application JwT:");
                Console.WriteLine(jsonWebToken + "\n");
                client.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jsonWebToken);

                dynamic response = await Wrapper.GET.Application(client)
                                                    .SendAsync();

                Dictionary<string, object> body = new()
                {
                    {
                        "access", new Dictionary<string, string>()
                        {
                            { "valid_until", DateTime.UtcNow.AddDays(10).ToString("u") }
                        }},
                    {
                    "aspsp", new Dictionary<string, string>()
                    {
                        { "name", "Nordea" },
                        { "country", "SE" }
                    }},
                    { "state", System.Guid.NewGuid().ToString() },
                    { "redirect_url", "https://localhost:4444/" },
                    { "psu_type", "personal" }
                };

                response = await Wrapper.POST.Authentication(client)
                                             .WithBody(body) 
                                             .SendAsync();
                Console.WriteLine($"To authenticate open URL {response.url}");

                Console.Write("Paste here the URL you have been redirected to: ");
                string? redirectedUrl = Console.ReadLine();

                string? code = HttpUtility.ParseQueryString(new Uri(redirectedUrl!).Query)["code"];
                body = new()
                {
                    { "code", code! }
                };

                /*
                response = await Wrapper.POST.Sessions(client)
                                             .WithBody(body)
                                             .SendAsync<Response>();

                json = await response.Content.ReadAsStringAsync();
                Dictionary<string, JsonElement>? jsonAsData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                Console.WriteLine($"New user session {jsonAsData?["session_id"]} has been created. The following accounts are available:");
                Console.WriteLine("==========================================\n");
                foreach (JsonElement account in jsonAsData!["accounts"].EnumerateArray())
                {
                    Console.WriteLine($"- {account}\n");
                }
                Console.WriteLine("==========================================");
                */
                return true;
            }
            return false;
        }

        internal Authenticator(Workspace workspace)
        {
            this.workspace = workspace;
        }
    }
}
