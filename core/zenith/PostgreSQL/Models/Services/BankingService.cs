using Auth0.AspNetCore.Authentication;
using System.Text.Json;
using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed class BankingService
    {
        private readonly BankingRepository _bankingRepository;
        private readonly UserRepository _userRepository;

        private readonly JwtAuthenticator _jwtAuthenticator;

        public BankingService(BankingRepository bankingRepository,
                              UserRepository userRepository,
                              JwtAuthenticator jwtAuthenticator)
        {
            _bankingRepository = bankingRepository;
            _userRepository = userRepository;

            _jwtAuthenticator = jwtAuthenticator;
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

        public async Task StartAspspSessionAsync(string bankingSessionId, DateTimeOffset expiresAt,
                                                 string userSessionId,
                                                 string state)
        {
            Guid activeSessionId = Guid.Parse(userSessionId);
            string aspspSessionId = bankingSessionId;
            DateTimeOffset consentExpiresAt = expiresAt;

            PendingBankSession? authentication = await _bankingRepository.SelectPendingBankAuthenticationAsync(activeSessionId,
                                                                                                               state);
            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);
            if (authentication != null && essentials != null)
            {
                Console.WriteLine(JsonSerializer.Serialize(authentication).ToString());

                AspspBankConnectionDto[]? sessions = await _bankingRepository.AllBankSessionsAsync(essentials.UserId);

                AspspBankingSessionEntity bankingSession = new()
                {
                    UserId = essentials.UserId,
                    AspspSessionId = aspspSessionId,
                    AspspName = authentication.AspspName,
                    AspspCountry = authentication.AspspCountry,
                    AspspPsuType = authentication.PsuType,
                    ConsentExpiresAt = consentExpiresAt,
                    Status = BankStatus.ACTIVE
                };

                await _bankingRepository.DeletePendingBankSessionAsync(state);
                if (sessions?.Length > 0)
                {
                    foreach (AspspBankConnectionDto session in sessions)
                    {
                        if (session.AspspName == authentication.AspspName
                        &&  session.AspspCountry == authentication.AspspCountry)
                        {
                            await _bankingRepository.RefreshBankSessionAsync(session.AspspSessionId!,
                                                                             bankingSession.AspspSessionId,
                                                                             bankingSession.Status.Value,
                                                                             bankingSession.ConsentExpiresAt.Value);
                            return;
                        }
                    }
                }
                await _bankingRepository.InsertBankSessionAsActiveAsync(bankingSession);
            }
        }
    }
}