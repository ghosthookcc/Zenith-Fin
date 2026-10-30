using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

using Sodium;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed class AvanzaOptions
    {
        public string? PublicKey { get; set; }
        public string PublicKeyFile { get; set; } = "/etc/avanza-sync/public.key";
    }

    public sealed record AvanzaResult(int StatusCode,
                                      string? Message);

    public sealed class AvanzaService
    {
        public const string CredentialsRateLimit = "avanza-credentials";

        private const int PublicKeyLength = 32;
        private const int MaxUsernameLength = 100;
        private const int MaxPasswordLength = 200;
        private const int MinTotpLength = 16;
        private const int MaxTotpLength = 128;

        private static readonly Regex Base32 = new("^[A-Z2-7]+$", RegexOptions.Compiled);
        private static readonly int[] InvalidBase32Lengths = { 1, 3, 6 };

        private readonly AvanzaRepository _avanzaRepository;
        private readonly UserRepository _userRepository;
        private readonly AvanzaOptions _options;

        public AvanzaService(AvanzaRepository avanzaRepository,
                             UserRepository userRepository,
                             IOptions<AvanzaOptions> options)
        {
            _avanzaRepository = avanzaRepository;
            _userRepository = userRepository;
            _options = options.Value;
        }

        public async Task<AvanzaResult> SaveCredentialsAsync(string sessionId,
                                                             AvanzaDto.SaveCredentialsRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return new AvanzaResult(StatusCodes.Status401Unauthorized, "No user found for session");

            byte[]? publicKey = PublicKey();

            if (publicKey == null) return new AvanzaResult(StatusCodes.Status503ServiceUnavailable, "Avanza is not set up on the server yet");

            string username = (request.Username ?? string.Empty).Trim();
            string password = request.Password ?? string.Empty;
            string? totpSecret = NormalizeTotpSecret(request.TotpSecret);

            if (username.Length == 0 || username.Length > MaxUsernameLength || username.Any(char.IsControl))
                return BadRequest("Enter your Avanza username");

            if (string.IsNullOrWhiteSpace(password) || password.Length > MaxPasswordLength)
                return BadRequest("Enter your Avanza password");

            if (totpSecret == null)
                return BadRequest("The TOTP secret must be the base32 key Avanza shows when you add an authenticator app");

            await _avanzaRepository.SaveCredentialsAsync(userId.Value, Encrypt(username, password, totpSecret, publicKey), Fingerprint(publicKey));

            return new AvanzaResult(StatusCodes.Status204NoContent, null);
        }

        public static byte[] Encrypt(string username,
                                     string password,
                                     string totpSecret,
                                     byte[] publicKey)
        {
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(new { username, password, totpSecret });

            try
            {
                return SealedPublicKeyBox.Create(plaintext, publicKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        public async Task<AvanzaResult> DeleteCredentialsAsync(string sessionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return new AvanzaResult(StatusCodes.Status401Unauthorized, "No user found for session");

            await _avanzaRepository.DeleteCredentialsAsync(userId.Value);

            return new AvanzaResult(StatusCodes.Status204NoContent, null);
        }

        public async Task<AvanzaDto.SyncStatus?> GetStatusAsync(string sessionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            AvanzaStatusRow row = await _avanzaRepository.GetStatusAsync(userId.Value);
            byte[]? publicKey = PublicKey();

            return new AvanzaDto.SyncStatus
            {
                Configured = publicKey != null,
                Exists = row.UpdatedAt != null,
                KeyMatches = publicKey != null && row.KeyFingerprint == Fingerprint(publicKey),
                UpdatedAt = row.UpdatedAt,
                LastAttemptAt = row.LastAttemptAt,
                LastSuccessAt = row.LastSuccessAt,
                Status = row.Status,
                ErrorClass = row.ErrorClass
            };
        }

        public async Task<AvanzaDto.Balances?> GetBalancesAsync(string sessionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            return BuildBalances(await _avanzaRepository.GetSnapshotsAsync(userId.Value));
        }

        public static AvanzaDto.Balances BuildBalances(IReadOnlyList<AvanzaSnapshotRow> rows)
        {
            if (rows.Count == 0) return new AvanzaDto.Balances();

            DateTime latest = rows.Max(row => row.FetchedAt);
            Dictionary<string, string> names = rows.GroupBy(row => row.AccountId)
                                                   .ToDictionary(group => group.Key,
                                                                 group => group.OrderBy(row => row.FetchedAt).Last().AccountName);

            return new AvanzaDto.Balances
            {
                FetchedAt = latest,
                Accounts = rows.Where(row => row.FetchedAt == latest)
                               .OrderByDescending(row => row.TotalValue)
                               .ThenBy(row => row.AccountName, StringComparer.Ordinal)
                               .Select(row => new AvanzaDto.Account
                               {
                                   AccountId = row.AccountId,
                                   AccountName = row.AccountName,
                                   AccountType = row.AccountType,
                                   TotalValue = row.TotalValue,
                                   Currency = row.Currency,
                                   FetchedAt = row.FetchedAt
                               })
                               .ToArray(),
                History = rows.OrderBy(row => row.FetchedAt)
                              .ThenBy(row => row.AccountId, StringComparer.Ordinal)
                              .Select(row => new AvanzaDto.HistoryPoint
                              {
                                  AccountId = row.AccountId,
                                  AccountName = names[row.AccountId],
                                  FetchedAt = row.FetchedAt,
                                  TotalValue = row.TotalValue
                              })
                              .ToArray()
            };
        }

        public static string? NormalizeTotpSecret(string? secret)
        {
            if (secret == null) return null;

            string normalized = new string(secret.Where(character => !char.IsWhiteSpace(character) && character != '-').ToArray())
                                    .ToUpperInvariant()
                                    .TrimEnd('=');

            if (normalized.Length < MinTotpLength || normalized.Length > MaxTotpLength) return null;

            if (!Base32.IsMatch(normalized) || InvalidBase32Lengths.Contains(normalized.Length % 8)) return null;

            return normalized;
        }

        public static string Fingerprint(byte[] publicKey)
        {
            return Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
        }

        private byte[]? PublicKey()
        {
            string? encoded = _options.PublicKey;

            if (string.IsNullOrWhiteSpace(encoded) && File.Exists(_options.PublicKeyFile))
                encoded = File.ReadAllText(_options.PublicKeyFile);

            if (string.IsNullOrWhiteSpace(encoded)) return null;

            try
            {
                byte[] key = Convert.FromBase64String(encoded.Trim());

                return key.Length == PublicKeyLength ? key : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static AvanzaResult BadRequest(string message)
        {
            return new AvanzaResult(StatusCodes.Status400BadRequest, message);
        }

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }
    }
}
