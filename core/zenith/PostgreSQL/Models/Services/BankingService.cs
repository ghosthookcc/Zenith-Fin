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
        private readonly JwtAuthenticator _jwtAuthenticator;

        public BankingService(BankingRepository bankingRepository,
                              JwtAuthenticator jwtAuthenticator)
        {
            _bankingRepository = bankingRepository;
            _jwtAuthenticator = jwtAuthenticator;
        }

        public async Task StartPendingAspspAuthenticationAsync(AspspDto.AuthenticationAspsp aspsp,
                                                               string state,
                                                               string SessionId)
        {
            AspspAuthenticationEntity authentication = new ()
            { 
                ActiveSessionId = Guid.Parse(SessionId),
                State = state,

                AspspName = aspsp.Bank,
                AspspCountry = aspsp.Country,
                AspspPsuType = aspsp.PsuType,
            };

            await _bankingRepository.InsertPendingAspspAuthenticationAsync(authentication);
        }

        public async Task StartAspspSessionAsync(string bankingSessionId, DateTime expiresAt,
                                                 string userSessionId,
                                                 string state)
        {
            Guid activeSessionId = Guid.Parse(userSessionId);
            string aspspSessionId = bankingSessionId;
            DateTime consentExpiresAt = expiresAt;

            PendingBankSession? authentication = await _bankingRepository.SelectPendingBankAuthenticationAsync(activeSessionId,
                                                                                                               state);
            if (authentication != null)
            {
                Console.WriteLine(JsonSerializer.Serialize(authentication).ToString());
                AspspBankingSessionEntity bankingSession = new ()
                {
                    ActiveSessionId = activeSessionId,
                    AspspSessionId = aspspSessionId,
                    AspspName = authentication.AspspName,
                    AspspCountry = authentication.AspspCountry,
                    AspspPsuType = authentication.PsuType,
                    ConsentExpiresAt = consentExpiresAt,
                };

                if (await _bankingRepository.DeletePendingBankSessionAsync(state))
                {
                    await _bankingRepository.InsertBankSessionAsActiveAsync(bankingSession);
                }
            }
        }
    }
}