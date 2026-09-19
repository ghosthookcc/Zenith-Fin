using Auth0.AspNetCore.Authentication;
using System.Text.Json;
using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.EnableBanking;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed class BankingService
    {
        private readonly BankingRepository _bankingRepository;
        private readonly UserRepository _userRepository;

        private readonly EnableBankingWorkspace _workspace;
        
        public BankingService(BankingRepository bankingRepository,
            UserRepository userRepository,
            EnableBankingWorkspace workspace)
        {
            _bankingRepository = bankingRepository;
            _userRepository = userRepository;

            _workspace = workspace;
        }
        
        private async Task SyncAccountsFromExistingSessionsAsync(string userSessionId)
        {
            Guid activeSessionId = Guid.Parse(userSessionId);

            UserEssentials? essentials =
                await _userRepository.GetUserIdBySessionId(activeSessionId);

            if (essentials == null)
                return;

            AspspBankConnectionDto[]? sessions = await _bankingRepository.AllBankSessionsAsync(essentials.UserId);

            if (sessions == null || sessions.Length == 0) return;

            foreach (AspspBankConnectionDto session in sessions)
            {
                if (session.Status != BankStatus.ACTIVE) continue;
                if (!Guid.TryParse(session.AspspSessionId, out Guid bankingSessionId)) continue;

                Console.WriteLine($"Syncing accounts from Enable Banking session {bankingSessionId}");

                Response.SessionData sessionData = await _workspace.Authenticator.FetchSession(bankingSessionId);

                if (!string.Equals(sessionData.status, "AUTHORIZED", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Skipping session {bankingSessionId}: status={sessionData.status}");
                    continue;
                }

                long? bankConnectionId = await _bankingRepository.GetBankConnectionIdAsync(session.AspspSessionId!);

                if (bankConnectionId == null) continue;

                List<EnableBankingEntities.AccountData> accounts = new();

                foreach (Guid accountId in sessionData.accounts)
                {
                    Console.WriteLine($"Fetching details for account {accountId}");
                    EnableBankingEntities.AccountData account = await _workspace.Authenticator.FetchAccountDetails(accountId);
                    accounts.Add(account);
                }

                if (accounts.Count > 0) await _bankingRepository.UpsertAccountsAsync(bankConnectionId.Value, accounts);
            }
        }

        public async Task StartPendingAspspAuthenticationAsync(AspspDto.AuthenticationAspsp aspsp,
            string state,
            string sessionId)
        {
            AspspAuthenticationEntity authentication = new ()
            { 
                ActiveSessionId = Guid.Parse(sessionId),
                State = state,

                AspspName = aspsp.Bank,
                AspspCountry = aspsp.Country,
                AspspPsuType = aspsp.PsuType,
            };

            await _bankingRepository.InsertPendingAspspAuthenticationAsync(authentication);
        }

        public async Task<AspspDto.AllAspsps?> GetInactiveAspsps(string sessionId,
            AspspDto.AllAspsps? aspsps)
        {
            Guid activeSessionId = Guid.Parse(sessionId);
            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);
            if (essentials != null)
            {
                AspspBankConnectionDto[]? activeSessions = await _bankingRepository.AllBankSessionsAsync(essentials.UserId);

                if (activeSessions == null || activeSessions.Length == 0)
                    return aspsps;

                if (aspsps != null)
                {
                    aspsps.Aspsps = aspsps.Aspsps.Where(dict => !activeSessions.Any(session =>
                            dict.ContainsKey(session.AspspName!) &&
                            dict[session.AspspName!].Country == session.AspspCountry))
                        .ToList();
                }

            }
            return aspsps;
        }

        public async Task StartAspspSessionAsync(string bankingSessionId, 
            DateTimeOffset expiresAt,
            string userSessionId,
            string state,
            IReadOnlyList<EnableBankingEntities.AccountData> accounts)
        {
            Guid activeSessionId = Guid.Parse(userSessionId);

            PendingBankSession? authentication =
                await _bankingRepository.SelectPendingBankAuthenticationAsync(
                    activeSessionId,
                    state);

            UserEssentials? essentials =
                await _userRepository.GetUserIdBySessionId(activeSessionId);

            if (authentication == null || essentials == null)
                return;

            AspspBankConnectionDto[]? sessions =
                await _bankingRepository.AllBankSessionsAsync(essentials.UserId);

            AspspBankingSessionEntity bankingSession = new()
            {
                UserId = essentials.UserId,
                AspspSessionId = bankingSessionId,
                AspspName = authentication.AspspName,
                AspspCountry = authentication.AspspCountry,
                AspspPsuType = authentication.PsuType,
                ConsentExpiresAt = expiresAt,
                Status = BankStatus.ACTIVE
            };

            await _bankingRepository.DeletePendingBankSessionAsync(state);

            bool existingConnection = false;

            if (sessions?.Length > 0)
            {
                foreach (AspspBankConnectionDto session in sessions)
                {
                    if (session.AspspName == authentication.AspspName &&
                        session.AspspCountry == authentication.AspspCountry)
                    {
                        await _bankingRepository.RefreshBankSessionAsync(
                            session.AspspSessionId!,
                            bankingSession.AspspSessionId,
                            bankingSession.Status.Value,
                            bankingSession.ConsentExpiresAt.Value);

                        existingConnection = true;
                        break;
                    }
                }
            }

            if (!existingConnection)
            {
                await _bankingRepository.InsertBankSessionAsActiveAsync(
                    bankingSession);
            }

            long? bankConnectionId =
                await _bankingRepository.GetBankConnectionIdAsync(
                    bankingSession.AspspSessionId);

            if (bankConnectionId != null)
            {
                await _bankingRepository.UpsertAccountsAsync(
                    bankConnectionId.Value,
                    accounts);
            }
        }
        
        public async Task<AccountDto.Balance[]> GetAccountsBalancesAsync(string sessionId)
        {
            Guid activeSessionId = Guid.Parse(sessionId);

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            if (essentials == null) return Array.Empty<AccountDto.Balance>();
            
            await SyncAccountsFromExistingSessionsAsync(sessionId);
            AccountDto.Account[] accounts = await _bankingRepository.GetAccountsForUserAsync(essentials.UserId);

            List<AccountDto.Balance> result = new();
            foreach (AccountDto.Account account in accounts)
            {
                Response.AccountsBalances balances = await _workspace.Authenticator.FetchAccountBalance(account.EnableBankingUid);                
                result.Add(new AccountDto.Balance
                {
                    Id = account.Id,
                    EnableBankingUid = account.EnableBankingUid,
                    Iban = account.Iban,
                    Name = account.Name,
                    Currency = account.Currency,
                    Balances = balances.balances
                });
            }
            return result.ToArray();
        }
    }
}