using Dapper;

using ZenithFin.Api.Models.Dtos;

using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Dtos;

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

        public async Task InsertBankSessionAsActiveAsync(AspspBankingSessionEntity pendingBankSession)
        {
            const string sql = """
            INSERT INTO "BankConnection"
            (active_session_id, aspsp_session_id, aspsp_name, aspsp_country, psu_type, consent_expires_at)
            VALUES
            (@ActiveSessionId, @AspspSessionId, @AspspName, @AspspCountry, @AspspPsuType, @ConsentExpiresAt);
            """;

            using var connection = _connectionFactory.Create();
            await connection.ExecuteAsync(sql, pendingBankSession);
        }
    }
}
