using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record AccountAliasResult(int StatusCode,
                                            string? Message,
                                            AccountDto.AccountAlias? AccountAlias = null);

    public sealed class AccountAliasService
    {
        private readonly AccountAliasRepository _accountAliasRepository;
        private readonly UserRepository _userRepository;

        private const int MaxAliasLength = 40;

        public AccountAliasService(AccountAliasRepository accountAliasRepository,
                                   UserRepository userRepository)
        {
            _accountAliasRepository = accountAliasRepository;
            _userRepository = userRepository;
        }

        public async Task<AccountAliasResult> RenameAsync(string sessionId,
                                                          long accountId,
                                                          AccountDto.RenameAccountRequest request)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId))
                return new AccountAliasResult(StatusCodes.Status401Unauthorized, "No user found for session");

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            if (essentials == null) return new AccountAliasResult(StatusCodes.Status401Unauthorized, "No user found for session");

            string? alias = TransactionText.Clean(request.Alias);

            if (alias != null && alias.Length > MaxAliasLength)
                return new AccountAliasResult(StatusCodes.Status400BadRequest, $"Names can be at most {MaxAliasLength} characters");

            bool owned = alias == null
                ? await _accountAliasRepository.ClearAliasAsync(essentials.UserId, accountId)
                : await _accountAliasRepository.SaveAliasAsync(essentials.UserId, accountId, alias);

            return owned
                ? new AccountAliasResult(StatusCodes.Status200OK, null, new AccountDto.AccountAlias { AccountId = accountId, Alias = alias })
                : new AccountAliasResult(StatusCodes.Status404NotFound, "Account not found");
        }

        public async Task<AccountAliasResult> SetRoleAsync(string sessionId,
                                                           long accountId,
                                                           AccountDto.SetAccountRoleRequest request)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId))
                return new AccountAliasResult(StatusCodes.Status401Unauthorized, "No user found for session");

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            if (essentials == null) return new AccountAliasResult(StatusCodes.Status401Unauthorized, "No user found for session");

            string? role = string.IsNullOrWhiteSpace(request.Role) ? null : request.Role.Trim().ToUpperInvariant();

            if (role != null && !Ledger.AccountRoles.All.Contains(role))
                return new AccountAliasResult(StatusCodes.Status400BadRequest, "Choose everyday, savings, credit card, loan or investment");

            return await _accountAliasRepository.SetRoleAsync(essentials.UserId, accountId, role)
                ? new AccountAliasResult(StatusCodes.Status200OK, null)
                : new AccountAliasResult(StatusCodes.Status404NotFound, "Account not found");
        }
    }
}
