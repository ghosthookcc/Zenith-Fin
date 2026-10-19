using Microsoft.IdentityModel.Tokens;
using System.Globalization;
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
    internal sealed record FetchedTransaction(Transaction transaction,
                                              string raw);
    struct AspspAuthenticationAttempt
    {
        public string? url;
        public bool success;
    }
    struct AspspAuthenticationCallbackAttempt
    {
        public string? sessionId;
        public DateTimeOffset? expiresAt;
        public IReadOnlyList<AccountData>? accounts;
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

        private const int MaxTransactionPages = 500;

        public string? GetToken()
        {
            lock (_lock)
            {
                if (_jwt != null && DateTime.UtcNow < _expiresAt - RefreshSkew) return _jwt;

                RotateToken();
                return _jwt;
            }
        }

        public async Task<Response.AccountsBalances> FetchAccountBalance(Guid accountId)
        {
            GetToken();

            dynamic response = await Wrapper.GET
                                            .AccountsBalancesById(client, accountId)
                                            .SendAsync();

            return response;
        }

        public async Task<IReadOnlyList<FetchedTransaction>> FetchAccountTransactions(Guid accountId,
                                                                                    DateOnly dateFrom,
                                                                                    string? strategy = null)
        {
            GetToken();

            Console.WriteLine($"Transaction fetch start: account={accountId}, " +
                              $"dateFrom={dateFrom:yyyy-MM-dd}, " +
                              $"strategy={strategy ?? "(omitted)"}");

            const int maxEmptyRepeatRetries = 5;
            int emptyRepeatRetries = 0;

            List<FetchedTransaction> transactions = new();
            HashSet<string> seenContinuationKeys = new();
            string? continuationKey = null;

            for (int page = 1; page <= MaxTransactionPages; page++)
            {
                RequestBuilder request = Wrapper.GET
                                                .AccountTransactionsById(client, accountId)
                                                .WithQuery("date_from", dateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                if (strategy != null)
                {
                    request = request.WithQuery("strategy", strategy);
                }

                if (!string.IsNullOrEmpty(continuationKey))
                {
                    request = request.WithQuery("continuation_key", continuationKey);
                }

                using HttpResponseMessage response = await request.SendAsyncRaw();
                string json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode) throw EnableBankingException.FromResponse((int)response.StatusCode, json);

                Response.Transactions body =
                    JsonSerializer.Deserialize<Response.Transactions>(
                        json, EnableBankingJson.Options)
                    ?? throw new InvalidOperationException(
                        $"Enable Banking returned a null transaction response " +
                        $"for account {accountId}.");
                IReadOnlyList<JsonElement> elements = body?.transactions ?? Array.Empty<JsonElement>();

                foreach (JsonElement element in elements)
                {
                    Transaction? transaction = element.Deserialize<Transaction>(EnableBankingJson.Options);

                    if (transaction != null) transactions.Add(new FetchedTransaction(transaction, element.GetRawText()));
                }

                Console.WriteLine($"Fetched transactions page {page} for account {accountId}: {elements.Count} transactions");

                string? nextContinuationKey = body?.continuationKey;

                Console.WriteLine(
                    $"Transaction pagination: account={accountId}, page={page}, " +
                    $"count={elements.Count}, " +
                    $"requestKeyPresent={!string.IsNullOrEmpty(continuationKey)}, " +
                    $"responseKeyPresent={!string.IsNullOrEmpty(nextContinuationKey)}, " +
                    $"sameAsRequest={continuationKey != null &&
                        string.Equals(
                            continuationKey,
                            nextContinuationKey,
                            StringComparison.Ordinal)}");

                string? nextKey = body?.continuationKey;

                if (string.IsNullOrEmpty(nextKey))
                {
                    return transactions;
                }

                bool repeatedKey = !seenContinuationKeys.Add(nextKey);

                if (repeatedKey)
                {
                    bool emptyPendingResponse =
                        elements.Count == 0 &&
                        string.Equals(nextKey, continuationKey, StringComparison.Ordinal);

                    if (!emptyPendingResponse ||
                        emptyRepeatRetries >= maxEmptyRepeatRetries)
                    {
                        throw new TransactionPaginationException(
                            $"Transaction pagination did not progress for account " +
                            $"{accountId} after {page} requests. " +
                            $"Empty repeated-key retries: {emptyRepeatRetries}.");
                    }

                    emptyRepeatRetries++;

                    throw new TransactionPaginationException(
                        $"Stopped fetching transactions for account {accountId} " +
                        $"after {MaxTransactionPages} pages.");
                }

                continuationKey = nextKey;
            }

            throw new InvalidOperationException($"Stopped fetching transactions for account {accountId} after {MaxTransactionPages} pages.");
        }

        internal sealed class TransactionPaginationException : Exception
        {
            public TransactionPaginationException(string message)
                : base(message)
            {
            }
        }

        public async Task<bool> RevokeSession(Guid sessionId)
        {
            GetToken();

            using HttpResponseMessage response = await Wrapper.DELETE
                                                             .SessionById(client, sessionId)
                                                             .SendAsyncRaw();

            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound;
        }

        public async Task<Response.SessionData> FetchSession(Guid sessionId)
        {
            GetToken();

            dynamic response = await Wrapper.GET
                                            .SessionById(client, sessionId)
                                            .SendAsync();

            return response;
        }

        public async Task<EnableBankingEntities.AccountData> FetchAccountDetails(Guid accountId)
        {
            GetToken();

            dynamic response = await Wrapper.GET
                                            .AccountDetailsById(client, accountId)
                                            .SendAsync();

            return response;
        }

        public async Task<AspspAuthenticationAttempt> Authenticate(AuthenticationAspsp aspsp,
                                                                   DateTimeOffset? expiresAt,
                                                                   string state)
        {
            if (workspace.config != null)
            {
                GetToken();

                dynamic response = await Wrapper.GET.Application(client)
                                                    .SendAsync();

                if (expiresAt == null)
                {
                    expiresAt = DateTimeOffset.UtcNow.AddDays(30);
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

            Request.Sessions authenticationBody = new(code);

            dynamic response = await Wrapper.POST.Sessions(client)
                                                 .WithBody(authenticationBody)
                                                 .SendAsync();

            return new AspspAuthenticationCallbackAttempt
            {
                sessionId = response.sessionId,
                expiresAt = response.access.validUntil,
                accounts = response.accounts,
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

            if (string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Created application JwT:");
                Console.WriteLine(token + "\n");
            }

            return token;
        }

        internal Authenticator(Workspace workspace,
                               Client client,
                               string? redirectUrl = null)
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
                _redirectUrl = string.IsNullOrWhiteSpace(redirectUrl)
                    ? workspace.config
                               .RootElement
                               .GetProperty("EnableBanking")
                               .GetProperty("redirectUrl")
                               .GetString()!
                    : redirectUrl;
            }
        }
    }
}
