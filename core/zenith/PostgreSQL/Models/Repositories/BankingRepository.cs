using System.Data;
using System.Globalization;

using Dapper;

using ZenithFin.Api.Models.Dtos;

using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.EnableBanking;

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
            }) > 0;
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
        
        public async Task<long?> GetBankConnectionIdAsync(string aspspSessionId)
        {
            const string sql = """
                               SELECT id
                               FROM "BankConnection"
                               WHERE aspsp_session_id = @AspspSessionId
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.QueryFirstOrDefaultAsync<long?>(sql, new { AspspSessionId = aspspSessionId });
        }
        
        public async Task UpsertAccountsAsync(long bankConnectionId,
            IReadOnlyList<EnableBankingEntities.AccountData> accounts)
        {
            const string sql = """
                               INSERT INTO "Account"
                               (
                                   enable_banking_uid,
                                   iban,
                                   name,
                                   currency,
                                   bank_connection_id
                               )
                               VALUES
                               (
                                   @EnableBankingUid,
                                   @Iban,
                                   @Name,
                                   @Currency,
                                   @BankConnectionId
                               )
                               ON CONFLICT (enable_banking_uid)
                               DO UPDATE SET
                                   iban = EXCLUDED.iban,
                                   name = EXCLUDED.name,
                                   currency = EXCLUDED.currency,
                                   bank_connection_id = EXCLUDED.bank_connection_id;
                               """;

            using var connection = _connectionFactory.Create();
            foreach (var account in accounts)
            {
                await connection.ExecuteAsync(sql, new
                {
                    EnableBankingUid = account.uid,
                    Iban = account.accountId.iban,
                    Name = account.name,
                    Currency = account.currency,
                    BankConnectionId = bankConnectionId
                });
            }
        }
        
        public async Task<AccountDto.Account[]> GetAccountsForUserAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   a.id AS Id,
                                   a.enable_banking_uid AS EnableBankingUid,
                                   a.iban AS Iban,
                                   a.name AS Name,
                                   a.currency AS Currency,
                                   a.bank_connection_id AS BankConnectionId,
                                   bc.aspsp_name AS BankName
                               FROM "Account" a
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE bc.user_id = @UserId
                                 AND bc.status = 'ACTIVE'::"BankStatus"
                               ORDER BY a.id;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<AccountDto.Account> accounts = await connection.QueryAsync<AccountDto.Account>(sql, new { UserId = userId });

            return accounts.ToArray();
        }

        public async Task<CachedAccountBalances?> GetCachedBalancesAsync(long userId, 
                                                                         long accountId)
        {
            const string sql = """
                               SELECT
                                   a.balances_fetched_at AS "FetchedAt",
                                   b.id AS "BalanceId",
                                   b.name AS "Name",
                                   b.currency AS "Currency",
                                   b.amount::text AS "Amount",
                                   b.balance_type AS "BalanceType",
                                   b.last_change_date_time AS "LastChangeDateTime",
                                   to_char(b.reference_date, 'YYYY-MM-DD') AS "ReferenceDate",
                                   b.last_committed_transaction AS "LastCommittedTransaction"
                               FROM "Account" a
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               LEFT JOIN "Balance" b
                                   ON b.account_id = a.id
                               WHERE a.id = @AccountId
                                 AND bc.user_id = @UserId
                                 AND bc.status = 'ACTIVE'::"BankStatus"
                               ORDER BY b.balance_type, b.currency;
                               """;

            using var connection = _connectionFactory.Create();

            var rows = (await connection.QueryAsync<CachedBalanceRow>(sql, new { UserId = userId, AccountId = accountId })).ToArray();

            if (rows.Length == 0 || rows[0].FetchedAt == null) return null;

            var balances = rows
                .Where(row => row.BalanceId.HasValue)
                .Select(row => new EnableBankingEntities.Balance(
                    name: row.Name,
                    balanceAmount: new EnableBankingEntities.BalanceAmount(
                        currency: row.Currency,
                        amount: row.Amount),
                    balanceType: row.BalanceType,
                    lastChangeDateTime: row.LastChangeDateTime,
                    referenceDate: row.ReferenceDate == null
                        ? null
                        : DateOnly.ParseExact(
                            row.ReferenceDate,
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture),
                    lastCommittedTransaction: row.LastCommittedTransaction))
                .ToArray();

            return new CachedAccountBalances(
                rows[0].FetchedAt!.Value,
                balances);
        }

        public async Task SaveBalancesAsync(long userId, 
                                            long accountId,
                                            IReadOnlyList<EnableBankingEntities.Balance> balances,
                                            DateTime fetchedAt)
        {
            if (fetchedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("Fetch time must be UTC.", nameof(fetchedAt));


            if (balances.GroupBy(b => (b.balanceType, b.balanceAmount.currency))
                        .Any(group => group.Count() > 1))
            {
                throw new InvalidOperationException("Multiple balances have the same type and currency.");
            }

            using var connection = _connectionFactory.Create();

            if (connection.State != ConnectionState.Open) connection.Open();

            using var transaction = connection.BeginTransaction();

            const string lockSql = """
                                   SELECT a.id
                                   FROM "Account" a
                                   INNER JOIN "BankConnection" bc
                                       ON bc.id = a.bank_connection_id
                                   WHERE a.id = @AccountId
                                     AND bc.user_id = @UserId
                                     AND bc.status = 'ACTIVE'::"BankStatus"
                                   FOR UPDATE OF a;
                                   """;

            var ownedAccount = await connection.QuerySingleOrDefaultAsync<long?>(
                lockSql,
                new { UserId = userId, AccountId = accountId },
                transaction);

            if (ownedAccount == null)
                throw new InvalidOperationException(
                    "The account is not available for this user.");

            const string upsertSql = """
                                     INSERT INTO "Balance"
                                     (
                                         account_id,
                                         amount,
                                         currency,
                                         balance_type,
                                         name,
                                         last_change_date_time,
                                         reference_date,
                                         last_committed_transaction,
                                         fetched_at
                                     )
                                     VALUES
                                     (
                                         @AccountId,
                                         CAST(@Amount AS numeric),
                                         @Currency,
                                         @BalanceType,
                                         @Name,
                                         @LastChangeDateTime,
                                         CAST(@ReferenceDate AS date),
                                         @LastCommittedTransaction,
                                         @FetchedAt
                                     )
                                     ON CONFLICT (account_id, balance_type, currency)
                                     DO UPDATE SET
                                         amount = EXCLUDED.amount,
                                         name = EXCLUDED.name,
                                         last_change_date_time = EXCLUDED.last_change_date_time,
                                         reference_date = EXCLUDED.reference_date,
                                         last_committed_transaction =
                                             EXCLUDED.last_committed_transaction,
                                         fetched_at = EXCLUDED.fetched_at
                                     RETURNING id;
                                     """;

            var retainedIds = new List<long>();

            foreach (var balance in balances)
            {
                long id = await connection.QuerySingleAsync<long>(
                    upsertSql,
                    new
                    {
                        AccountId = accountId,

                        Amount = balance.balanceAmount.amount,
                        Currency = balance.balanceAmount.currency,
                        BalanceType = balance.balanceType,
                        
                        Name = balance.name,
                        
                        LastChangeDateTime = NormalizeBankTimestamp(balance.lastChangeDateTime),
                        ReferenceDate = balance.referenceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        LastCommittedTransaction = balance.lastCommittedTransaction,
                       
                        FetchedAt = fetchedAt
                    },
                    transaction);

                retainedIds.Add(id);
            }
            
            const string deleteSql = """
                                     DELETE FROM "Balance"
                                     WHERE account_id = @AccountId
                                       AND NOT (id = ANY(@RetainedIds));
                                     """;

            await connection.ExecuteAsync(
                deleteSql,
                new
                {
                    AccountId = accountId,
                    RetainedIds = retainedIds.ToArray()
                },
                transaction);

            const string timestampSql = """
                                        UPDATE "Account"
                                        SET balances_fetched_at = @FetchedAt
                                        WHERE id = @AccountId;
                                        """;

            await connection.ExecuteAsync(timestampSql, 
                                    new { AccountId = accountId, FetchedAt = fetchedAt }, 
                                          transaction);
            transaction.Commit();
        }

        private static DateTime? NormalizeBankTimestamp(DateTime? timestamp)
        {
            if (timestamp == null) return null;
            return timestamp.Value.Kind switch
            {
                DateTimeKind.Utc => timestamp.Value,
                DateTimeKind.Local => timestamp.Value.ToUniversalTime(),
                _ => throw new InvalidOperationException("The bank timestamp has no timezone. Check its JSON mapping.")
            };
        }
    }
}