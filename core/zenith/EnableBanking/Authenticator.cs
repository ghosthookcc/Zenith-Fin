using Microsoft.IdentityModel.Tokens;
using System.Diagnostics;
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
                string keyPath = workspace.config["keyPath"];
                string applicationId = workspace.config["applicationId"];

                string jsonWebToken = GenerateToken(keyPath, applicationId);
                Console.WriteLine("Created application JwT:");
                Console.WriteLine(jsonWebToken + "\n");
                client.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jsonWebToken);
                client.Http.DefaultRequestHeaders
                           .Accept
                           .Add(new MediaTypeWithQualityHeaderValue("application/json"));

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

                response = await Wrapper.POST.Sessions(client)
                                             .WithBody(body)
                                             .SendAsync();

                Console.WriteLine($"New user session {response.sessionId} has been created. The following accounts are available:");
                Console.WriteLine("==========================================\n");

                List<Response.AccountsBalances> balances = new ();
                foreach (Response.AccountData account in response.accounts)
                {
                    balances.Add(await Wrapper.GET.AccountsBalancesById(client,
                                                                        account.uid)
                                                  .SendAsync());
                    Console.WriteLine($"- {account.ToString()}\n");
                }
                Console.WriteLine("==========================================\n");

                Console.WriteLine("Balances:");
                Console.WriteLine("==========================================\n");
                foreach (Response.AccountsBalances data in balances)
                {
                    foreach (Response.Balance balance in data.balances)
                    {
                        Console.WriteLine($"{balance.balanceType} - {balance.balanceAmount.amount} {balance.balanceAmount.currency}");
                    }
                    Debug.WriteLine("");
                }
                Console.WriteLine("==========================================\n");
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
