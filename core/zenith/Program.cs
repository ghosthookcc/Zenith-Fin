using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ZenithFin
{
    class EnableBankingAuthenticator
    {
        private readonly HttpClient client = new HttpClient();
        private readonly string apiOrigin = "https://api.enablebanking.com";
        private readonly string jwtAudience = "api.enablebanking.com";
        private readonly string jwtIssuer = "enablebanking.com";

        private Workspace workspace;

        public async Task<Dictionary<string, JsonElement>?> requestApplicationData()
        {
            HttpResponseMessage response = await client.GetAsync(apiOrigin + "/application");
            string json = await response.Content.ReadAsStringAsync();
            Dictionary<string, JsonElement>? applicationData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            Console.WriteLine($"App name: {applicationData?["name"]}");
            Console.WriteLine($"App description: {applicationData?["description"]}\n");

            return applicationData;
        }

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

        public async Task<bool> Authenticate()
        {
            if (workspace.config != null)
            {
                string keyPath = workspace.config["keyPath"];
                string applicationId = workspace.config["applicationId"];

                string jsonWebToken = GenerateToken(keyPath, applicationId);
                Console.WriteLine("Created application JwT:");
                Console.WriteLine(jsonWebToken + "\n");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jsonWebToken);

                Dictionary<string, JsonElement>? applicationData = await requestApplicationData();

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

                HttpResponseMessage data = await client.PostAsync(apiOrigin + "/auth",
                                                                  new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

                string json = await data.Content.ReadAsStringAsync();
                Dictionary<string, string>? jsonAsDictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                Console.WriteLine($"To authenticate open URL {jsonAsDictionary?["url"]}");

                Console.Write("Paste here the URL you have been redirected to: ");
                string? redirectedUrl = Console.ReadLine();

                string? code = HttpUtility.ParseQueryString(new Uri(redirectedUrl!).Query)["code"];
                body = new()
                {
                    { "code", code! }
                };

                data = await client.PostAsync(apiOrigin + "/sessions",
                                              new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
                json = await data.Content.ReadAsStringAsync();
                Dictionary<string, JsonElement>? jsonAsData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                Console.WriteLine($"New user session {jsonAsData?["session_id"]} has been created. The following accounts are available:");
                Console.WriteLine("==========================================\n");
                foreach (var account in jsonAsData!["accounts"].EnumerateArray())
                {
                    Console.WriteLine($"- {account}\n");
                }
                Console.WriteLine("==========================================");
                return true;
            }
            return false;
        }

        public EnableBankingAuthenticator(Workspace workspace)
        {
            this.workspace = workspace;
        }
    }

    class Workspace
    {
        public Dictionary<string, string>? config = new();
        public Workspace(string configPath)
        {
            config = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(configPath));
        }
    }

    class EnableBanking : Workspace
    {
        public EnableBankingAuthenticator authenticator;

        public EnableBanking(string configPath) : base(configPath)
        {
            this.authenticator = new(this);
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            EnableBanking workspace = new("EnableBanking/config.json");
            await workspace.authenticator.Authenticate();
        }
    }
}