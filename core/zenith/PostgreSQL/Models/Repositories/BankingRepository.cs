using Dapper;

using ZenithFin.Api.Models.Dtos;

using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Dtos;
using Microsoft.EntityFrameworkCore.Design;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class BankingRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public BankingRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task InsertPendingAspspAuthenticationAsync(AspspAuthenticationEntity authenticationDetails)
        {
            const string sql = """
            INSERT INTO "PendingBankConnection"
            (active_session_id, state, aspsp_name, aspsp_country, psu_type, auth_expires_at)
            VALUES
            (@ActiveSessionId, @State, @AspspName, @AspspCountry, @AspspPsuType, @AuthExpiresAt);
            """;

            using var connection = _connectionFactory.Create();
            await connection.ExecuteAsync(sql, authenticationDetails);
        }

        public async Task<PendingBankSession?> SelectPendingBankAuthenticationAsync(Guid? userSession,
                                                                                    string state)
        {
            if (userSession == null)
            {
                return null;
            }

            Console.WriteLine("UserSession: " + userSession.ToString());
            Console.WriteLine("State: " + state);

            const string sql = """
            SELECT aspsp_name AS AspspName, 
                   aspsp_country AS AspspCountry,
                   psu_type AS PsuType,
                   auth_expires_at AS AuthExpiresAt
            FROM "PendingBankConnection"
            WHERE active_session_id = @SessionId AND state = @State
            """;

            using var connection = _connectionFactory.Create();
            return await connection.QueryFirstOrDefaultAsync<PendingBankSession?>(sql, new { SessionId = userSession.Value, State = state });
        }

        public async Task<bool> DeletePendingBankSessionAsync(string state)
        {
            const string sql = """
            DELETE FROM "PendingBankConnection"
            WHERE state = @State
            """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new { State = state }) > 0 ? true : false;
        }

        public async Task<bool> InsertBankSessionAsActiveAsync(AspspBankingSessionEntity pendingBankSession)
        {
            const string sql = """
            INSERT INTO "BankConnection"
            (user_id, aspsp_session_id, aspsp_name, aspsp_country, psu_type, consent_expires_at, status)
            VALUES
            (@UserId, @AspspSessionId, @AspspName, @AspspCountry, @AspspPsuType, @ConsentExpiresAt, @Status::"BankStatus");
            """;

            using var connection = _connectionFactory.Create();
            return await connection.ExecuteAsync(sql, new
            {
                pendingBankSession.UserId,
                pendingBankSession.AspspSessionId,
                pendingBankSession.AspspName,
                pendingBankSession.AspspCountry,
                pendingBankSession.AspspPsuType,
                pendingBankSession.ConsentExpiresAt,
                Status = pendingBankSession.Status.ToString()
            }) > 0 ? true : false;
        }

        public async Task<AspspBankConnectionDto[]?> AllBankSessionsAsync(long userId)
        {
            const string sql = """
            SELECT aspsp_session_id AS AspspSessionId,
                   aspsp_name AS AspspName, 
                   aspsp_country AS AspspCountry, 
                   consent_expires_at AS ConsentExpiresAt,
                   status AS Status
            FROM "BankConnection"
            WHERE user_id = @UserId
            """;

            using var connection = _connectionFactory.Create();
            IEnumerable<AspspBankConnectionDto> entries = await connection.QueryAsync<AspspBankConnectionDto>(sql, 
                                                                                                              new { UserId = userId });
            return entries.ToArray();
        }

        public async Task RefreshBankSessionAsync(string aspspSessionId,
                                                  string newAspspSessionId,
                                                  BankStatus newStatus,
                                                  DateTimeOffset newConsentExpiresAt)
        {
            const string sql = """
            UPDATE "BankConnection"
            SET aspsp_session_id = @NewAspspSessionId,
                status = @NewStatus::"BankStatus",
                consent_expires_at = @NewConsentExpiresAt
            WHERE aspsp_session_id = @AspspSessionId
            """;

            using var connection = _connectionFactory.Create();
            await connection.ExecuteAsync(sql, new
            {
                AspspSessionId = aspspSessionId,
                NewAspspSessionId = newAspspSessionId,
                NewStatus = newStatus.ToString(),
                NewConsentExpiresAt = newConsentExpiresAt
            });
        }
    }
}
