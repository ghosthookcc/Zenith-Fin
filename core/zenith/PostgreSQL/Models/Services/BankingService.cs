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

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            if (essentials == null) return;

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

            PendingBankSession? authentication = await _bankingRepository.SelectPendingBankAuthenticationAsync(activeSessionId, state);

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            if (authentication == null || essentials == null) return;

            AspspBankConnectionDto[]? sessions = await _bankingRepository.AllBankSessionsAsync(essentials.UserId);

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

            if (!existingConnection) await _bankingRepository.InsertBankSessionAsActiveAsync(bankingSession);
            long? bankConnectionId = await _bankingRepository.GetBankConnectionIdAsync(bankingSession.AspspSessionId);

            if (bankConnectionId != null)
            {
                await _bankingRepository.UpsertAccountsAsync(bankConnectionId.Value, accounts);
            }
            
            await _bankingRepository.DeletePendingBankSessionAsync(state);
        }
        
        public async Task<AccountDto.Balance[]> GetAccountsBalancesAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return Array.Empty<AccountDto.Balance>();

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            if (essentials == null) return Array.Empty<AccountDto.Balance>();
            
            AccountDto.Account[] accounts = await _bankingRepository.GetAccountsForUserAsync(essentials.UserId);

            List<AccountDto.Balance> result = new List<AccountDto.Balance>();

            foreach (var account in accounts)
            {
                int lockIndex = (int)((ulong)account.Id % (ulong)BalanceRefreshLocks.Length);

                SemaphoreSlim refreshLock = BalanceRefreshLocks[lockIndex];

                await refreshLock.WaitAsync();

                try
                {
                    CachedAccountBalances? cached = await _bankingRepository.GetCachedBalancesAsync(essentials.UserId, 
                                                                                                    account.Id);

                    DateTime now = DateTime.UtcNow;

                    bool cacheIsFresh = cached != null &&
                                        cached.FetchedAt <= now &&
                                        now - cached.FetchedAt < BalanceCacheDuration;
                    
                    Console.WriteLine($"Balance cache: account={account.Id}, " +
                                      $"found={cached != null}, " +
                                      $"fetchedAt={cached?.FetchedAt:O}, " +
                                      $"now={now:O}, " +
                                      $"fresh={cacheIsFresh}");

                    IReadOnlyList<EnableBankingEntities.Balance> balances;

                    if (cacheIsFresh)
                    {
                        balances = cached!.Balances;
                    }
                    else
                    {
                        Response.AccountsBalances response = await _workspace.Authenticator.FetchAccountBalance(account.EnableBankingUid);
                
                        if (response.balances == null) throw new InvalidOperationException("The bank returned no balance collection.");

                        balances = response.balances.ToArray();

                        await _bankingRepository.SaveBalancesAsync(essentials.UserId, 
                            account.Id,
                            balances,
                            DateTime.UtcNow);
                    }

                    result.Add(new AccountDto.Balance
                    {
                        Id = account.Id,
                        EnableBankingUid = account.EnableBankingUid,
                        BankName = account.BankName,
                        Iban = account.Iban,
                        Name = account.Name,
                        Currency = account.Currency,
                        Balances = balances
                    });
                }
                finally
                {
                    refreshLock.Release();
                }
            }

            return result.ToArray();
        }
        
        private static readonly TimeSpan BalanceCacheDuration = TimeSpan.FromMinutes(5);
        private static readonly SemaphoreSlim[] BalanceRefreshLocks = Enumerable.Range(0, 64)
                                                                                .Select(_ => new SemaphoreSlim(1, 1))
                                                                                .ToArray();
    }
}