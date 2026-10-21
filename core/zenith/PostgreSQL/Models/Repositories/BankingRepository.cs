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
        
        public async Task<ConsentLinkResult> LinkConsentAccountsAsync(long bankConnectionId,
                                                                      IReadOnlyList<EnableBankingEntities.AccountData> accounts)
        {
            using var connection = _connectionFactory.Create();

            if (connection.State != ConnectionState.Open) connection.Open();

            using var transaction = connection.BeginTransaction();

            const string existingSql = """
                                       SELECT
                                           id AS "Id",
                                           enable_banking_uid AS "EnableBankingUid",
                                           iban AS "Iban",
                                           identification_hash AS "IdentificationHash"
                                       FROM "Account"
                                       WHERE bank_connection_id = @BankConnectionId
                                       ORDER BY id
                                       FOR UPDATE;
                                       """;

            ConsentAccountRow[] existing = (await connection.QueryAsync<ConsentAccountRow>(existingSql,
                                                                                         new { BankConnectionId = bankConnectionId },
                                                                                         transaction)).ToArray();

            const string updateSql = """
                                     UPDATE "Account"
                                     SET enable_banking_uid = @EnableBankingUid,
                                         iban = @Iban,
                                         name = @Name,
                                         currency = @Currency,
                                         identification_hash = @IdentificationHash,
                                         bank_account_type = COALESCE(@BankAccountType, bank_account_type),
                                         bank_product = COALESCE(@BankProduct, bank_product),
                                         in_consent = true
                                     WHERE id = @Id;
                                     """;

            const string insertSql = """
                                     INSERT INTO "Account"
                                     (
                                         enable_banking_uid,
                                         iban,
                                         name,
                                         currency,
                                         bank_connection_id,
                                         identification_hash,
                                         bank_account_type,
                                         bank_product,
                                         in_consent
                                     )
                                     VALUES
                                     (
                                         @EnableBankingUid,
                                         @Iban,
                                         @Name,
                                         @Currency,
                                         @BankConnectionId,
                                         @IdentificationHash,
                                         @BankAccountType,
                                         @BankProduct,
                                         true
                                     )
                                     ON CONFLICT (enable_banking_uid)
                                     DO UPDATE SET
                                         iban = EXCLUDED.iban,
                                         name = EXCLUDED.name,
                                         currency = EXCLUDED.currency,
                                         bank_connection_id = EXCLUDED.bank_connection_id,
                                         identification_hash = EXCLUDED.identification_hash,
                                         bank_account_type = COALESCE(EXCLUDED.bank_account_type, "Account".bank_account_type),
                                         bank_product = COALESCE(EXCLUDED.bank_product, "Account".bank_product),
                                         in_consent = true
                                     RETURNING id;
                                     """;

            HashSet<long> linked = new();
            int added = 0;

            foreach (EnableBankingEntities.AccountData account in accounts)
            {
                string? iban = NormalizeIban(account.accountId?.iban);
                string? hash = string.IsNullOrWhiteSpace(account.identificationHash) ? null : account.identificationHash;

                ConsentAccountRow? match = existing.FirstOrDefault(row => !linked.Contains(row.Id) && row.EnableBankingUid == account.uid) ??
                                           existing.FirstOrDefault(row => !linked.Contains(row.Id) && hash != null && row.IdentificationHash == hash) ??
                                           existing.FirstOrDefault(row => !linked.Contains(row.Id) && iban != null && NormalizeIban(row.Iban) == iban);

                var values = new
                {
                    Id = match?.Id ?? 0,
                    EnableBankingUid = account.uid,
                    Iban = account.accountId?.iban,
                    Name = account.name,
                    Currency = account.currency,
                    BankConnectionId = bankConnectionId,
                    IdentificationHash = hash,
                    BankAccountType = string.IsNullOrWhiteSpace(account.cashAccountType) ? null : account.cashAccountType.Trim().ToUpperInvariant(),
                    BankProduct = string.IsNullOrWhiteSpace(account.product) ? null : account.product.Trim()
                };

                if (match != null)
                {
                    await connection.ExecuteAsync(updateSql, values, transaction);
                    linked.Add(match.Id);
                }
                else
                {
                    linked.Add(await connection.QuerySingleAsync<long>(insertSql, values, transaction));
                    added++;
                }
            }

            long[] released = existing.Where(row => !linked.Contains(row.Id)).Select(row => row.Id).ToArray();

            const string releaseSql = """
                                      UPDATE "Account"
                                      SET in_consent = false
                                      WHERE id = ANY(@AccountIds);
                                      """;

            if (released.Length > 0)
                await connection.ExecuteAsync(releaseSql, new { AccountIds = released }, transaction);

            transaction.Commit();

            return new ConsentLinkResult(linked.Count - added, added, released.Length);
        }

        public async Task<AspspDto.BankConnection[]> GetConnectionsForUserAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   bc.id AS "Id",
                                   bc.aspsp_name AS "Bank",
                                   bc.aspsp_country AS "Country",
                                   bc.psu_type AS "PsuType",
                                   bc.status::text AS "Status",
                                   bc.consent_expires_at AS "ConsentExpiresAt",
                                   (count(a.id) FILTER (WHERE a.in_consent))::int AS "Accounts",
                                   (
                                       SELECT count(*)::int
                                       FROM "Transaction" t
                                       INNER JOIN "Account" ta
                                           ON ta.id = t.account_id
                                       WHERE ta.bank_connection_id = bc.id
                                   ) AS "Transactions"
                               FROM "BankConnection" bc
                               LEFT JOIN "Account" a
                                   ON a.bank_connection_id = bc.id
                               WHERE bc.user_id = @UserId
                               GROUP BY bc.id
                               ORDER BY bc.aspsp_name, bc.id;
                               """;

            using var connection = _connectionFactory.Create();
            IEnumerable<AspspDto.BankConnection> connections = await connection.QueryAsync<AspspDto.BankConnection>(sql, new { UserId = userId });

            return connections.ToArray();
        }

        public async Task<OwnedBankConnection?> GetOwnedConnectionAsync(long userId,
                                                                        long connectionId)
        {
            const string sql = """
                               SELECT
                                   aspsp_session_id AS "AspspSessionId",
                                   aspsp_name AS "Bank"
                               FROM "BankConnection"
                               WHERE id = @ConnectionId
                                 AND user_id = @UserId;
                               """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleOrDefaultAsync<OwnedBankConnection>(sql, new { UserId = userId, ConnectionId = connectionId });
        }

        public async Task<RemovedBankConnection?> DeleteConnectionAsync(long userId,
                                                                        long connectionId)
        {
            using var connection = _connectionFactory.Create();

            if (connection.State != ConnectionState.Open) connection.Open();

            using var transaction = connection.BeginTransaction();

            const string lockSql = """
                                   SELECT id
                                   FROM "BankConnection"
                                   WHERE id = @ConnectionId
                                     AND user_id = @UserId
                                   FOR UPDATE;
                                   """;

            long? owned = await connection.QuerySingleOrDefaultAsync<long?>(lockSql,
                                                                           new { UserId = userId, ConnectionId = connectionId },
                                                                           transaction);

            if (owned == null) return null;

            const string countSql = """
                                    SELECT
                                        (SELECT count(*)::int FROM "Account" WHERE bank_connection_id = @ConnectionId) AS "Accounts",
                                        (
                                            SELECT count(*)::int
                                            FROM "Transaction" t
                                            INNER JOIN "Account" a
                                                ON a.id = t.account_id
                                            WHERE a.bank_connection_id = @ConnectionId
                                        ) AS "Transactions";
                                    """;

            RemovedBankConnection removed = await connection.QuerySingleAsync<RemovedBankConnection>(countSql,
                                                                                                   new { ConnectionId = connectionId },
                                                                                                   transaction);

            const string deleteSql = """
                                     DELETE FROM "Balance"
                                     WHERE account_id IN (SELECT id FROM "Account" WHERE bank_connection_id = @ConnectionId);

                                     DELETE FROM "Account"
                                     WHERE bank_connection_id = @ConnectionId;

                                     DELETE FROM "BankConnection"
                                     WHERE id = @ConnectionId
                                       AND user_id = @UserId;
                                     """;

            await connection.ExecuteAsync(deleteSql, new { UserId = userId, ConnectionId = connectionId }, transaction);

            transaction.Commit();

            return removed;
        }

        private static string? NormalizeIban(string? iban)
        {
            string normalized = new string((iban ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

            return normalized.Length == 0 ? null : normalized;
        }

        public async Task<AccountDto.Account[]> GetAccountsForUserAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   a.id AS Id,
                                   a.enable_banking_uid AS EnableBankingUid,
                                   a.iban AS Iban,
                                   COALESCE(aa.bank_name_alias, a.name) AS Name,
                                   a.name AS OriginalName,
                                   aa.bank_name_alias AS Alias,
                                   a.currency AS Currency,
                                   a.bank_connection_id AS BankConnectionId,
                                   a.in_consent AS InConsent,
                                   a.role::text AS StoredRole,
                                   a.bank_account_type AS BankAccountType,
                                   bc.aspsp_name AS BankName
                               FROM "Account" a
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               LEFT JOIN "AccountAlias" aa
                                   ON aa.account_id = a.id
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