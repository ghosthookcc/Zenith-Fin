using System.Text.RegularExpressions;

using Npgsql;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
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
        private readonly ManualTransactionService _manualTransactionService;
        private readonly UserRepository _userRepository;

        private const int MaxNameLength = 40;

        private const decimal MaxBalance = 1_000_000_000_000m;

        private static readonly string[] Countings = { "NEUTRAL", "IN", "OUT", "FLOW" };

        private static readonly Regex IbanFormat = new(@"^[A-Z]{2}[0-9]{2}[A-Z0-9]{10,30}$", RegexOptions.Compiled);

        private static readonly VirtualAccountResult Unauthorized = new(StatusCodes.Status401Unauthorized, "No user found for session");

        public VirtualAccountService(VirtualAccountRepository virtualAccountRepository,
                                     ManualTransactionService manualTransactionService,
                                     UserRepository userRepository)
        {
            _virtualAccountRepository = virtualAccountRepository;
            _manualTransactionService = manualTransactionService;
            _userRepository = userRepository;
        }

        public async Task<VirtualAccountDto.VirtualAccountsResponse?> GetVirtualAccountsAsync(string sessionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            VirtualAccountDto.VirtualAccount[] accounts = await _virtualAccountRepository.GetVirtualAccountsForUserAsync(userId.Value);
            VirtualAccountDto.VirtualAccount[] tracked = accounts.Where(account => account.Balance != null && account.BalanceSetAt != null)
                                                                 .ToArray();

            if (tracked.Length > 0)
            {
                VirtualBalanceTransactionRow[] transactions = await _virtualAccountRepository.GetTransactionsSinceAsync(userId.Value,
                                                                                                                        tracked.Min(account => account.BalanceSetAt!.Value));
                ManualTransactionDto.ManualTransaction[] manualTransactions = await _manualTransactionService.GetManualTransactionsAsync(userId.Value);

                Dictionary<long, DateTime> recordedAt = manualTransactions.Where(manual => manual.MatchedTransactionId != null)
                                                                          .ToDictionary(manual => manual.MatchedTransactionId!.Value,
                                                                                        manual => manual.CreatedAt);
                ManualTransactionDto.ManualTransaction[] unmatched = manualTransactions.Where(manual => manual.MatchedTransactionId == null)
                                                                                       .ToArray();

                foreach (VirtualAccountDto.VirtualAccount account in tracked)
                {
                    string[] keys = VirtualAccountMatcher.Keys(account.AccountNumber, account.ClearingNumber, account.Iban);
                    DateTime anchor = account.BalanceSetAt!.Value;

                    decimal banked = transactions.Where(transaction => SeenAt(transaction, recordedAt) > anchor &&
                                                                       VirtualAccountMatcher.Matches(transaction.CounterpartyAccount, keys))
                                                 .Sum(transaction => transaction.Amount);
                    decimal recorded = unmatched.Where(manual => manual.CreatedAt > anchor)
                                                .Sum(manual => (manual.ToVirtualAccountId == account.Id ? manual.Amount : 0) -
                                                               (manual.FromVirtualAccountId == account.Id ? manual.Amount : 0));

                    account.CurrentBalance = account.Balance!.Value - banked + recorded;
                }
            }

            return new VirtualAccountDto.VirtualAccountsResponse
            {
                VirtualAccounts = accounts
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
                if (!await _virtualAccountRepository.UpdateVirtualAccountAsync(userId.Value, virtualAccountId, valid, request.SetBalance)) return NotFound();
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

            decimal? balance = request.SetBalance ? request.Balance : null;

            if (balance is decimal amount && Math.Abs(amount) > MaxBalance)
                return (null, BadRequest("That balance is too large"));

            return (new VirtualAccountDto.VirtualAccount
            {
                Name = name,
                AccountNumber = accountNumber,
                ClearingNumber = clearingNumber.Length == 0 ? null : clearingNumber,
                Iban = iban.Length == 0 ? null : iban,
                Counting = counting,
                Balance = balance,
                CurrentBalance = balance
            }, null);
        }

        private static DateTime SeenAt(VirtualBalanceTransactionRow transaction,
                                       IReadOnlyDictionary<long, DateTime> recordedAt)
        {
            return recordedAt.TryGetValue(transaction.Id, out DateTime recorded) && recorded < transaction.FirstSeenAt
                ? recorded
                : transaction.FirstSeenAt;
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
