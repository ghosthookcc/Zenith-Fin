using System.Text.RegularExpressions;

using Npgsql;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record VirtualAccountResult(int StatusCode,
                                              string? Message,
                                              VirtualAccountDto.VirtualAccount? VirtualAccount = null);

    public sealed class VirtualAccountService
    {
        private readonly VirtualAccountRepository _virtualAccountRepository;
        private readonly UserRepository _userRepository;

        private const int MaxNameLength = 40;

        private static readonly string[] Countings = { "NEUTRAL", "IN", "OUT", "FLOW" };

        private static readonly Regex IbanFormat = new(@"^[A-Z]{2}[0-9]{2}[A-Z0-9]{10,30}$", RegexOptions.Compiled);

        private static readonly VirtualAccountResult Unauthorized = new(StatusCodes.Status401Unauthorized, "No user found for session");

        public VirtualAccountService(VirtualAccountRepository virtualAccountRepository,
                                     UserRepository userRepository)
        {
            _virtualAccountRepository = virtualAccountRepository;
            _userRepository = userRepository;
        }

        public async Task<VirtualAccountDto.VirtualAccountsResponse?> GetVirtualAccountsAsync(string sessionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            return new VirtualAccountDto.VirtualAccountsResponse
            {
                VirtualAccounts = await _virtualAccountRepository.GetVirtualAccountsForUserAsync(userId.Value)
            };
        }

        public async Task<VirtualAccountResult> CreateAsync(string sessionId,
                                                            VirtualAccountDto.SaveVirtualAccountRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            VirtualAccountDto.VirtualAccount[] existing = await _virtualAccountRepository.GetVirtualAccountsForUserAsync(userId.Value);
            (VirtualAccountDto.VirtualAccount? valid, VirtualAccountResult? invalid) = Validate(request, existing, null);

            if (valid == null) return invalid!;

            long id;

            try
            {
                id = await _virtualAccountRepository.InsertVirtualAccountAsync(userId.Value, valid);
            }
            catch (PostgresException errno) when (errno.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return DuplicateName(valid.Name);
            }

            valid.Id = id;

            return new VirtualAccountResult(StatusCodes.Status200OK, null, valid);
        }

        public async Task<VirtualAccountResult> UpdateAsync(string sessionId,
                                                            long virtualAccountId,
                                                            VirtualAccountDto.SaveVirtualAccountRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            VirtualAccountDto.VirtualAccount[] existing = await _virtualAccountRepository.GetVirtualAccountsForUserAsync(userId.Value);

            if (existing.All(account => account.Id != virtualAccountId)) return NotFound();

            (VirtualAccountDto.VirtualAccount? valid, VirtualAccountResult? invalid) = Validate(request, existing, virtualAccountId);

            if (valid == null) return invalid!;

            try
            {
                if (!await _virtualAccountRepository.UpdateVirtualAccountAsync(userId.Value, virtualAccountId, valid)) return NotFound();
            }
            catch (PostgresException errno) when (errno.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return DuplicateName(valid.Name);
            }

            valid.Id = virtualAccountId;

            return new VirtualAccountResult(StatusCodes.Status200OK, null, valid);
        }

        public async Task<VirtualAccountResult> DeleteAsync(string sessionId,
                                                            long virtualAccountId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            return await _virtualAccountRepository.DeleteVirtualAccountAsync(userId.Value, virtualAccountId)
                ? new VirtualAccountResult(StatusCodes.Status200OK, null)
                : NotFound();
        }

        private static (VirtualAccountDto.VirtualAccount? Account, VirtualAccountResult? Invalid) Validate(VirtualAccountDto.SaveVirtualAccountRequest request,
                                                                                                         IReadOnlyList<VirtualAccountDto.VirtualAccount> existing,
                                                                                                         long? editedId)
        {
            string? name = TransactionText.Clean(request.Name);

            if (name == null) return (null, BadRequest("Give the account a name"));
            if (name.Length > MaxNameLength) return (null, BadRequest($"Names can be at most {MaxNameLength} characters"));

            if (existing.Any(account => account.Id != editedId && string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase)))
                return (null, DuplicateName(name));

            string accountNumber = Digits(request.AccountNumber);

            if (accountNumber.Length is < 5 or > 17)
                return (null, BadRequest("Account numbers need between 5 and 17 digits"));

            string clearingNumber = Digits(request.ClearingNumber);

            if (clearingNumber.Length is not (0 or 4 or 5))
                return (null, BadRequest("Clearing numbers have 4 or 5 digits"));

            string iban = new string((request.Iban ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

            if (iban.Length > 0 && !IbanFormat.IsMatch(iban))
                return (null, BadRequest("That doesn't look like an IBAN, for example SE45 5000 0000 0583 9825 7466"));

            string counting = request.Counting?.Trim().ToUpperInvariant() ?? "";

            if (!Countings.Contains(counting))
                return (null, BadRequest("Choose whether the account counts as neutral, money in, money out, or money in and out"));

            return (new VirtualAccountDto.VirtualAccount
            {
                Name = name,
                AccountNumber = accountNumber,
                ClearingNumber = clearingNumber.Length == 0 ? null : clearingNumber,
                Iban = iban.Length == 0 ? null : iban,
                Counting = counting
            }, null);
        }

        private static string Digits(string? value)
        {
            return new string((value ?? "").Where(char.IsAsciiDigit).ToArray());
        }

        private static VirtualAccountResult BadRequest(string message)
        {
            return new VirtualAccountResult(StatusCodes.Status400BadRequest, message);
        }

        private static VirtualAccountResult NotFound()
        {
            return new VirtualAccountResult(StatusCodes.Status404NotFound, "Virtual account not found");
        }

        private static VirtualAccountResult DuplicateName(string name)
        {
            return new VirtualAccountResult(StatusCodes.Status409Conflict, $"There is already an account called {name}");
        }

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }
    }
}
