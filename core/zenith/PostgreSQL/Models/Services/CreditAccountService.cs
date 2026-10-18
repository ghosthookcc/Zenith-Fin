using Npgsql;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record CreditAccountResult(int StatusCode,
                                             string? Message,
                                             CreditAccountDto.CreditAccount? CreditAccount = null);

    public sealed class CreditAccountService
    {
        private readonly CreditAccountRepository _creditAccountRepository;
        private readonly TransactionRepository _transactionRepository;
        private readonly UserRepository _userRepository;

        private sealed record ValidCreditAccount(string Name,
                                                 long AccountId,
                                                 string Counting);

        private const int MaxNameLength = 40;

        private static readonly string[] Countings = { "NEUTRAL", "FLOW" };

        private static readonly CreditAccountResult Unauthorized = new(StatusCodes.Status401Unauthorized, "No user found for session");

        public CreditAccountService(CreditAccountRepository creditAccountRepository,
                                    TransactionRepository transactionRepository,
                                    UserRepository userRepository)
        {
            _creditAccountRepository = creditAccountRepository;
            _transactionRepository = transactionRepository;
            _userRepository = userRepository;
        }

        public async Task<CreditAccountDto.CreditAccountsResponse?> GetCreditAccountsAsync(string sessionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            return new CreditAccountDto.CreditAccountsResponse
            {
                CreditAccounts = await _creditAccountRepository.GetCreditAccountsForUserAsync(userId.Value)
            };
        }

        public async Task<CreditAccountResult> CreateAsync(string sessionId,
                                                           CreditAccountDto.SaveCreditAccountRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            CreditAccountDto.CreditAccount[] existing = await _creditAccountRepository.GetCreditAccountsForUserAsync(userId.Value);
            (ValidCreditAccount? valid, CreditAccountResult? invalid) = await ValidateAsync(userId.Value, request, existing, null);

            if (valid == null) return invalid!;

            long? creditAccountId;

            try
            {
                creditAccountId = await _creditAccountRepository.InsertCreditAccountAsync(userId.Value, valid.Name, valid.AccountId, valid.Counting);
            }
            catch (PostgresException errno) when (errno.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Conflict();
            }

            return creditAccountId == null ? UnknownAccount() : await CompleteAsync(userId.Value, creditAccountId.Value);
        }

        public async Task<CreditAccountResult> UpdateAsync(string sessionId,
                                                           long creditAccountId,
                                                           CreditAccountDto.SaveCreditAccountRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            CreditAccountDto.CreditAccount[] existing = await _creditAccountRepository.GetCreditAccountsForUserAsync(userId.Value);

            if (existing.All(creditAccount => creditAccount.Id != creditAccountId)) return NotFound();

            (ValidCreditAccount? valid, CreditAccountResult? invalid) = await ValidateAsync(userId.Value, request, existing, creditAccountId);

            if (valid == null) return invalid!;

            try
            {
                bool updated = await _creditAccountRepository.UpdateCreditAccountAsync(userId.Value, creditAccountId, valid.Name, valid.AccountId, valid.Counting);

                if (!updated) return NotFound();
            }
            catch (PostgresException errno) when (errno.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Conflict();
            }

            return await CompleteAsync(userId.Value, creditAccountId);
        }

        public async Task<CreditAccountResult> DeleteAsync(string sessionId,
                                                           long creditAccountId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            return await _creditAccountRepository.DeleteCreditAccountAsync(userId.Value, creditAccountId)
                ? new CreditAccountResult(StatusCodes.Status200OK, null)
                : NotFound();
        }

        private async Task<CreditAccountResult> CompleteAsync(long userId,
                                                              long creditAccountId)
        {
            CreditAccountDto.CreditAccount? saved = (await _creditAccountRepository.GetCreditAccountsForUserAsync(userId))
                                                    .FirstOrDefault(creditAccount => creditAccount.Id == creditAccountId);

            return saved == null ? NotFound() : new CreditAccountResult(StatusCodes.Status200OK, null, saved);
        }

        private async Task<(ValidCreditAccount? CreditAccount, CreditAccountResult? Invalid)> ValidateAsync(long userId,
                                                                                                             CreditAccountDto.SaveCreditAccountRequest request,
                                                                                                             IReadOnlyList<CreditAccountDto.CreditAccount> existing,
                                                                                                             long? editedId)
        {
            string? name = TransactionText.Clean(request.Name);

            if (name == null) return (null, BadRequest("Give the credit account a name"));
            if (name.Length > MaxNameLength) return (null, BadRequest($"Names can be at most {MaxNameLength} characters"));

            if (existing.Any(creditAccount => creditAccount.Id != editedId &&
                                              string.Equals(creditAccount.Name, name, StringComparison.OrdinalIgnoreCase)))
                return (null, new CreditAccountResult(StatusCodes.Status409Conflict, $"There is already a credit account called {name}"));

            TransactionAccountRow[] accounts = await _transactionRepository.GetAccountsForUserAsync(userId);

            if (request.AccountId is not long accountId || accounts.All(account => account.Id != accountId))
                return (null, UnknownAccount());

            CreditAccountDto.CreditAccount? linked = existing.FirstOrDefault(creditAccount => creditAccount.Id != editedId &&
                                                                                              creditAccount.AccountId == accountId);

            if (linked != null)
                return (null, new CreditAccountResult(StatusCodes.Status409Conflict, $"That account already belongs to the credit account {linked.Name}"));

            string counting = request.Counting?.Trim().ToUpperInvariant() ?? "";

            if (!Countings.Contains(counting))
                return (null, BadRequest("Choose whether the credit account counts as neutral or as money in and out"));

            return (new ValidCreditAccount(name, accountId, counting), null);
        }

        private static CreditAccountResult BadRequest(string message)
        {
            return new CreditAccountResult(StatusCodes.Status400BadRequest, message);
        }

        private static CreditAccountResult NotFound()
        {
            return new CreditAccountResult(StatusCodes.Status404NotFound, "Credit account not found");
        }

        private static CreditAccountResult UnknownAccount()
        {
            return BadRequest("Pick one of your connected accounts");
        }

        private static CreditAccountResult Conflict()
        {
            return new CreditAccountResult(StatusCodes.Status409Conflict, "That name or account is already used by another credit account");
        }

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }
    }
}
