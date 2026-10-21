using System.Data;

using Dapper;

using Npgsql;

using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.PostgreSQL.Models.Repositories
{
    public sealed class SharingRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public SharingRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<(long Id, string Name)?> FindUserByEmailAsync(string email)
        {
            const string sql = """
                               SELECT id AS "Id", full_name AS "Name"
                               FROM "User"
                               WHERE lower(email) = lower(@Email);
                               """;

            using var connection = _connectionFactory.Create();
            var row = await connection.QuerySingleOrDefaultAsync<UserNameRow>(sql, new { Email = email });

            return row == null ? null : (row.Id, row.Name);
        }

        public async Task<string> GetUserNameAsync(long userId)
        {
            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleAsync<string>("""SELECT full_name FROM "User" WHERE id = @UserId;""", new { UserId = userId });
        }

        public async Task<PartnershipRow?> GetPartnershipAsync(long userId,
                                                               bool includeEnded = false)
        {
            string sql = $"""
                          SELECT
                              p.id AS "Id",
                              p.inviter_id AS "InviterId",
                              p.invitee_id AS "InviteeId",
                              p.status::text AS "Status",
                              p.created_at AS "CreatedAt",
                              p.responded_at AS "RespondedAt",
                              p.ended_at AS "EndedAt"
                          FROM "Partnership" p
                          WHERE (p.inviter_id = @UserId OR p.invitee_id = @UserId)
                            AND p.status IN ('PENDING', 'ACCEPTED'{(includeEnded ? ", 'ENDED'" : "")})
                          ORDER BY (p.status = 'ACCEPTED') DESC, (p.status = 'PENDING') DESC, p.id DESC
                          LIMIT 1;
                          """;

            using var connection = _connectionFactory.Create();
            return await connection.QuerySingleOrDefaultAsync<PartnershipRow>(sql, new { UserId = userId });
        }

        public async Task<string?> CreatePartnershipAsync(long inviterId,
                                                          long inviteeId,
                                                          int inviterShare,
                                                          string notification)
        {
            using var connection = (NpgsqlConnection)_connectionFactory.Create();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await LockUsersAsync(connection, transaction, inviterId, inviteeId);

            const string openSql = """
                                   SELECT count(*)
                                   FROM "Partnership"
                                   WHERE status IN ('PENDING', 'ACCEPTED')
                                     AND (inviter_id IN (@InviterId, @InviteeId) OR invitee_id IN (@InviterId, @InviteeId));
                                   """;

            if (await connection.ExecuteScalarAsync<long>(openSql, new { InviterId = inviterId, InviteeId = inviteeId }, transaction) > 0)
                return "One of you already shares an economy with someone or has an invitation waiting";

            const string insertSql = """
                                     WITH created AS (
                                         INSERT INTO "Partnership" (inviter_id, invitee_id, status)
                                         VALUES (@InviterId, @InviteeId, 'PENDING')
                                         RETURNING id
                                     )
                                     INSERT INTO "SplitRule" (partnership_id, inviter_share, valid_from, status, proposed_by)
                                     SELECT id, @InviterShare, DATE '2000-01-01', 'PENDING', @InviterId FROM created;
                                     """;

            await connection.ExecuteAsync(insertSql, new { InviterId = inviterId, InviteeId = inviteeId, InviterShare = inviterShare }, transaction);
            await NotifyAsync(connection, transaction, inviteeId, notification);
            await transaction.CommitAsync();

            return null;
        }

        public async Task<long?> RespondPartnershipAsync(long userId,
                                                         long partnershipId,
                                                         bool accept,
                                                         string notification)
        {
            using var connection = (NpgsqlConnection)_connectionFactory.Create();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            const string sql = """
                               UPDATE "Partnership"
                               SET status = CAST(@Status AS "ShareStatus"),
                                   responded_at = now()
                               WHERE id = @PartnershipId
                                 AND invitee_id = @UserId
                                 AND status = 'PENDING'
                               RETURNING inviter_id;
                               """;

            long? inviterId = await connection.QuerySingleOrDefaultAsync<long?>(sql, new
            {
                UserId = userId,
                PartnershipId = partnershipId,
                Status = accept ? "ACCEPTED" : "DECLINED"
            }, transaction);

            if (inviterId == null) return null;

            await connection.ExecuteAsync("""
                                          UPDATE "SplitRule"
                                          SET status = CAST(@Status AS "ShareStatus"), responded_at = now()
                                          WHERE partnership_id = @PartnershipId AND status = 'PENDING';
                                          """, new { PartnershipId = partnershipId, Status = accept ? "ACCEPTED" : "DECLINED" }, transaction);

            await NotifyAsync(connection, transaction, inviterId.Value, notification);
            await transaction.CommitAsync();

            return inviterId;
        }

        public async Task<long?> EndPartnershipAsync(long userId,
                                                     long partnershipId,
                                                     string notification)
        {
            using var connection = (NpgsqlConnection)_connectionFactory.Create();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            const string sql = """
                               UPDATE "Partnership"
                               SET status = CASE WHEN status = 'PENDING' THEN 'CANCELLED'::"ShareStatus" ELSE 'ENDED'::"ShareStatus" END,
                                   ended_at = now()
                               WHERE id = @PartnershipId
                                 AND ((status = 'ACCEPTED' AND (inviter_id = @UserId OR invitee_id = @UserId))
                                   OR (status = 'PENDING' AND inviter_id = @UserId))
                               RETURNING CASE WHEN inviter_id = @UserId THEN invitee_id ELSE inviter_id END;
                               """;

            long? otherId = await connection.QuerySingleOrDefaultAsync<long?>(sql, new { UserId = userId, PartnershipId = partnershipId }, transaction);

            if (otherId == null) return null;

            await connection.ExecuteAsync("""
                                          UPDATE "AccountSharing"
                                          SET status = CASE WHEN status = 'ACCEPTED' THEN 'ENDED'::"ShareStatus" ELSE 'CANCELLED'::"ShareStatus" END,
                                              ended_at = now()
                                          WHERE partnership_id = @PartnershipId AND status IN ('PENDING', 'ACCEPTED');

                                          UPDATE "SplitRule"
                                          SET status = 'CANCELLED', responded_at = now()
                                          WHERE partnership_id = @PartnershipId AND status = 'PENDING';
                                          """, new { PartnershipId = partnershipId }, transaction);

            await NotifyAsync(connection, transaction, otherId.Value, notification);
            await transaction.CommitAsync();

            return otherId;
        }

        public async Task<string?> ShareAccountAsync(long userId,
                                                     long partnershipId,
                                                     long partnerId,
                                                     long accountId,
                                                     string notification)
        {
            using var connection = (NpgsqlConnection)_connectionFactory.Create();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            const string sql = """
                               INSERT INTO "AccountSharing" (partnership_id, account_id, owner_id, status)
                               SELECT @PartnershipId, a.id, @UserId, 'PENDING'
                               FROM "Account" a
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE a.id = @AccountId
                                 AND bc.user_id = @UserId
                                 AND EXISTS (SELECT 1 FROM "Partnership" p WHERE p.id = @PartnershipId AND p.status = 'ACCEPTED')
                               RETURNING id;
                               """;

            try
            {
                long? id = await connection.QuerySingleOrDefaultAsync<long?>(sql, new
                {
                    UserId = userId,
                    PartnershipId = partnershipId,
                    AccountId = accountId
                }, transaction);

                if (id == null) return "That account isn't one of yours";
            }
            catch (PostgresException errno) when (errno.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return "That account is already shared or waiting for an answer";
            }

            await NotifyAsync(connection, transaction, partnerId, notification);
            await transaction.CommitAsync();

            return null;
        }

        public async Task<long?> RespondShareAsync(long userId,
                                                   long sharingId,
                                                   bool accept,
                                                   string notification)
        {
            using var connection = (NpgsqlConnection)_connectionFactory.Create();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            const string sql = """
                               UPDATE "AccountSharing" s
                               SET status = CAST(@Status AS "ShareStatus"),
                                   responded_at = now()
                               FROM "Partnership" p
                               WHERE s.id = @SharingId
                                 AND p.id = s.partnership_id
                                 AND p.status = 'ACCEPTED'
                                 AND s.status = 'PENDING'
                                 AND s.owner_id <> @UserId
                                 AND (p.inviter_id = @UserId OR p.invitee_id = @UserId)
                               RETURNING s.owner_id;
                               """;

            long? ownerId = await connection.QuerySingleOrDefaultAsync<long?>(sql, new
            {
                UserId = userId,
                SharingId = sharingId,
                Status = accept ? "ACCEPTED" : "DECLINED"
            }, transaction);

            if (ownerId == null) return null;

            await NotifyAsync(connection, transaction, ownerId.Value, notification);
            await transaction.CommitAsync();

            return ownerId;
        }

        public async Task<long?> EndShareAsync(long userId,
                                               long sharingId,
                                               string notification)
        {
            using var connection = (NpgsqlConnection)_connectionFactory.Create();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            const string sql = """
                               UPDATE "AccountSharing" s
                               SET status = CASE WHEN s.status = 'PENDING' THEN 'CANCELLED'::"ShareStatus" ELSE 'ENDED'::"ShareStatus" END,
                                   ended_at = now()
                               FROM "Partnership" p
                               WHERE s.id = @SharingId
                                 AND p.id = s.partnership_id
                                 AND (p.inviter_id = @UserId OR p.invitee_id = @UserId)
                                 AND (s.status = 'ACCEPTED' OR (s.status = 'PENDING' AND s.owner_id = @UserId))
                               RETURNING CASE WHEN p.inviter_id = @UserId THEN p.invitee_id ELSE p.inviter_id END;
                               """;

            long? otherId = await connection.QuerySingleOrDefaultAsync<long?>(sql, new { UserId = userId, SharingId = sharingId }, transaction);

            if (otherId == null) return null;

            await NotifyAsync(connection, transaction, otherId.Value, notification);
            await transaction.CommitAsync();

            return otherId;
        }

        public async Task ProposeSplitAsync(long userId,
                                            long partnershipId,
                                            long partnerId,
                                            int inviterShare,
                                            DateOnly validFrom,
                                            string notification)
        {
            using var connection = (NpgsqlConnection)_connectionFactory.Create();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await connection.ExecuteAsync("""
                                          UPDATE "SplitRule"
                                          SET status = 'CANCELLED', responded_at = now()
                                          WHERE partnership_id = @PartnershipId AND status = 'PENDING';

                                          INSERT INTO "SplitRule" (partnership_id, inviter_share, valid_from, status, proposed_by)
                                          VALUES (@PartnershipId, @InviterShare, CAST(@ValidFrom AS date), 'PENDING', @UserId);
                                          """, new
            {
                UserId = userId,
                PartnershipId = partnershipId,
                InviterShare = inviterShare,
                ValidFrom = validFrom.ToString("yyyy-MM-dd")
            }, transaction);

            await NotifyAsync(connection, transaction, partnerId, notification);
            await transaction.CommitAsync();
        }

        public async Task<long?> RespondSplitAsync(long userId,
                                                   long ruleId,
                                                   bool accept,
                                                   string notification)
        {
            using var connection = (NpgsqlConnection)_connectionFactory.Create();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            const string findSql = """
                                   SELECT r.partnership_id AS "PartnershipId", r.proposed_by AS "ProposedBy", to_char(r.valid_from, 'YYYY-MM-DD') AS "ValidFrom"
                                   FROM "SplitRule" r
                                   INNER JOIN "Partnership" p
                                       ON p.id = r.partnership_id
                                   WHERE r.id = @RuleId
                                     AND r.status = 'PENDING'
                                     AND r.proposed_by <> @UserId
                                     AND p.status = 'ACCEPTED'
                                     AND (p.inviter_id = @UserId OR p.invitee_id = @UserId)
                                   FOR UPDATE OF r;
                                   """;

            PendingSplitRow? pending = await connection.QuerySingleOrDefaultAsync<PendingSplitRow>(findSql, new { UserId = userId, RuleId = ruleId }, transaction);

            if (pending == null) return null;

            if (accept)
                await connection.ExecuteAsync("""
                                              UPDATE "SplitRule"
                                              SET status = 'CANCELLED', responded_at = now()
                                              WHERE partnership_id = @PartnershipId
                                                AND status = 'ACCEPTED'
                                                AND valid_from = CAST(@ValidFrom AS date);
                                              """, new { pending.PartnershipId, pending.ValidFrom }, transaction);

            await connection.ExecuteAsync("""
                                          UPDATE "SplitRule"
                                          SET status = CAST(@Status AS "ShareStatus"), responded_at = now()
                                          WHERE id = @RuleId;
                                          """, new { RuleId = ruleId, Status = accept ? "ACCEPTED" : "DECLINED" }, transaction);

            await NotifyAsync(connection, transaction, pending.ProposedBy, notification);
            await transaction.CommitAsync();

            return pending.ProposedBy;
        }

        public async Task<SplitRuleRow[]> GetSplitRulesAsync(long partnershipId)
        {
            const string sql = """
                               SELECT
                                   id AS "Id",
                                   inviter_share AS "InviterShare",
                                   to_char(valid_from, 'YYYY-MM-DD') AS "ValidFrom",
                                   status::text AS "Status",
                                   proposed_by AS "ProposedBy"
                               FROM "SplitRule"
                               WHERE partnership_id = @PartnershipId
                               ORDER BY valid_from, id;
                               """;

            using var connection = _connectionFactory.Create();
            return (await connection.QueryAsync<SplitRuleRow>(sql, new { PartnershipId = partnershipId })).ToArray();
        }

        public async Task<AccountSharingRow[]> GetAccountSharingsAsync(long partnershipId)
        {
            const string sql = """
                               SELECT
                                   s.id AS "Id",
                                   s.account_id AS "AccountId",
                                   s.owner_id AS "OwnerId",
                                   s.status::text AS "Status",
                                   s.created_at AS "CreatedAt",
                                   s.ended_at AS "EndedAt",
                                   COALESCE(aa.bank_name_alias, a.name, bc.aspsp_name) AS "Name",
                                   bc.aspsp_name AS "BankName",
                                   a.iban AS "Iban"
                               FROM "AccountSharing" s
                               INNER JOIN "Account" a
                                   ON a.id = s.account_id
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               LEFT JOIN "AccountAlias" aa
                                   ON aa.account_id = a.id
                               WHERE s.partnership_id = @PartnershipId
                               ORDER BY s.id;
                               """;

            using var connection = _connectionFactory.Create();
            return (await connection.QueryAsync<AccountSharingRow>(sql, new { PartnershipId = partnershipId })).ToArray();
        }

        public async Task<ArrangementRow[]> GetArrangementsAsync(long partnershipId)
        {
            const string sql = """
                               SELECT
                                   s.id AS "Id",
                                   s.account_id AS "AccountId",
                                   s.payer_id AS "PayerId",
                                   s.signature AS "Signature",
                                   s.credit_debit_indicator AS "CreditDebitIndicator",
                                   s.label AS "Label",
                                   to_char(s.valid_from, 'YYYY-MM-DD') AS "ValidFrom",
                                   s.status::text AS "Status",
                                   s.proposed_by AS "ProposedBy",
                                   s.created_at AS "CreatedAt",
                                   s.ended_at AS "EndedAt",
                                   COALESCE(aa.bank_name_alias, a.name, bc.aspsp_name) AS "AccountName"
                               FROM "SharedArrangement" s
                               INNER JOIN "Account" a
                                   ON a.id = s.account_id
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               LEFT JOIN "AccountAlias" aa
                                   ON aa.account_id = a.id
                               WHERE s.partnership_id = @PartnershipId
                               ORDER BY s.id;
                               """;

            using var connection = _connectionFactory.Create();
            return (await connection.QueryAsync<ArrangementRow>(sql, new { PartnershipId = partnershipId })).ToArray();
        }

        public async Task<string?> ProposeArrangementAsync(long userId,
                                                           long partnershipId,
                                                           long partnerId,
                                                           long accountId,
                                                           string signature,
                                                           string creditDebitIndicator,
                                                           string label,
                                                           DateOnly validFrom,
                                                           string notification)
        {
            using var connection = (NpgsqlConnection)_connectionFactory.Create();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            const string sql = """
                               INSERT INTO "SharedArrangement"
                               (partnership_id, account_id, payer_id, signature, credit_debit_indicator, label, valid_from, status, proposed_by)
                               SELECT @PartnershipId, a.id, @UserId, @Signature, @CreditDebitIndicator, @Label, CAST(@ValidFrom AS date), 'PENDING', @UserId
                               FROM "Account" a
                               INNER JOIN "BankConnection" bc
                                   ON bc.id = a.bank_connection_id
                               WHERE a.id = @AccountId
                                 AND bc.user_id = @UserId
                                 AND EXISTS (SELECT 1 FROM "Partnership" p WHERE p.id = @PartnershipId AND p.status = 'ACCEPTED')
                               RETURNING id;
                               """;

            try
            {
                long? id = await connection.QuerySingleOrDefaultAsync<long?>(sql, new
                {
                    UserId = userId,
                    PartnershipId = partnershipId,
                    AccountId = accountId,
                    Signature = signature,
                    CreditDebitIndicator = creditDebitIndicator,
                    Label = label,
                    ValidFrom = validFrom.ToString("yyyy-MM-dd")
                }, transaction);

                if (id == null) return "Only payments from your own accounts can be shared this way";
            }
            catch (PostgresException errno) when (errno.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return "This payment is already shared or waiting for an answer";
            }

            await NotifyAsync(connection, transaction, partnerId, notification);
            await transaction.CommitAsync();

            return null;
        }

        public async Task<long?> RespondArrangementAsync(long userId,
                                                         long arrangementId,
                                                         bool accept,
                                                         string notification)
        {
            using var connection = (NpgsqlConnection)_connectionFactory.Create();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            const string sql = """
                               UPDATE "SharedArrangement" s
                               SET status = CAST(@Status AS "ShareStatus"),
                                   responded_at = now()
                               FROM "Partnership" p
                               WHERE s.id = @ArrangementId
                                 AND p.id = s.partnership_id
                                 AND p.status = 'ACCEPTED'
                                 AND s.status = 'PENDING'
                                 AND s.proposed_by <> @UserId
                                 AND (p.inviter_id = @UserId OR p.invitee_id = @UserId)
                               RETURNING s.proposed_by;
                               """;

            long? proposer = await connection.QuerySingleOrDefaultAsync<long?>(sql, new
            {
                UserId = userId,
                ArrangementId = arrangementId,
                Status = accept ? "ACCEPTED" : "DECLINED"
            }, transaction);

            if (proposer == null) return null;

            await NotifyAsync(connection, transaction, proposer.Value, notification);
            await transaction.CommitAsync();

            return proposer;
        }

        public async Task<long?> EndArrangementAsync(long userId,
                                                     long arrangementId,
                                                     string notification)
        {
            using var connection = (NpgsqlConnection)_connectionFactory.Create();
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            const string sql = """
                               UPDATE "SharedArrangement" s
                               SET status = CASE WHEN s.status = 'PENDING' THEN 'CANCELLED'::"ShareStatus" ELSE 'ENDED'::"ShareStatus" END,
                                   ended_at = now()
                               FROM "Partnership" p
                               WHERE s.id = @ArrangementId
                                 AND p.id = s.partnership_id
                                 AND (p.inviter_id = @UserId OR p.invitee_id = @UserId)
                                 AND (s.status = 'ACCEPTED' OR (s.status = 'PENDING' AND s.proposed_by = @UserId))
                               RETURNING CASE WHEN p.inviter_id = @UserId THEN p.invitee_id ELSE p.inviter_id END;
                               """;

            long? otherId = await connection.QuerySingleOrDefaultAsync<long?>(sql, new { UserId = userId, ArrangementId = arrangementId }, transaction);

            if (otherId == null) return null;

            await NotifyAsync(connection, transaction, otherId.Value, notification);
            await transaction.CommitAsync();

            return otherId;
        }

        public async Task<NotificationRow[]> GetNotificationsAsync(long userId)
        {
            const string sql = """
                               SELECT id AS "Id", text AS "Text", created_at AS "CreatedAt", (read_at IS NOT NULL) AS "Read"
                               FROM "Notification"
                               WHERE user_id = @UserId
                               ORDER BY created_at DESC, id DESC
                               LIMIT 30;
                               """;

            using var connection = _connectionFactory.Create();
            return (await connection.QueryAsync<NotificationRow>(sql, new { UserId = userId })).ToArray();
        }

        public async Task MarkNotificationsReadAsync(long userId)
        {
            using var connection = _connectionFactory.Create();
            await connection.ExecuteAsync("""UPDATE "Notification" SET read_at = now() WHERE user_id = @UserId AND read_at IS NULL;""", new { UserId = userId });
        }

        public async Task<int> CountInboxAsync(long userId)
        {
            const string sql = """
                               SELECT
                                   (SELECT count(*) FROM "Notification" WHERE user_id = @UserId AND read_at IS NULL)
                                 + (SELECT count(*) FROM "Partnership" WHERE invitee_id = @UserId AND status = 'PENDING')
                                 + (SELECT count(*)
                                    FROM "AccountSharing" s
                                    INNER JOIN "Partnership" p ON p.id = s.partnership_id
                                    WHERE s.status = 'PENDING' AND s.owner_id <> @UserId AND p.status = 'ACCEPTED'
                                      AND (p.inviter_id = @UserId OR p.invitee_id = @UserId))
                                 + (SELECT count(*)
                                    FROM "SharedArrangement" s
                                    INNER JOIN "Partnership" p ON p.id = s.partnership_id
                                    WHERE s.status = 'PENDING' AND s.proposed_by <> @UserId AND p.status = 'ACCEPTED'
                                      AND (p.inviter_id = @UserId OR p.invitee_id = @UserId))
                                 + (SELECT count(*)
                                    FROM "SplitRule" r
                                    INNER JOIN "Partnership" p ON p.id = r.partnership_id
                                    WHERE r.status = 'PENDING' AND r.proposed_by <> @UserId AND p.status = 'ACCEPTED'
                                      AND (p.inviter_id = @UserId OR p.invitee_id = @UserId));
                               """;

            using var connection = _connectionFactory.Create();
            return (int)await connection.ExecuteScalarAsync<long>(sql, new { UserId = userId });
        }

        private static async Task LockUsersAsync(NpgsqlConnection connection,
                                                 NpgsqlTransaction transaction,
                                                 long first,
                                                 long second)
        {
            foreach (long id in new[] { Math.Min(first, second), Math.Max(first, second) })
                await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(7734, CAST(@Id AS integer));", new { Id = id }, transaction);
        }

        private static Task NotifyAsync(IDbConnection connection,
                                        IDbTransaction transaction,
                                        long userId,
                                        string text)
        {
            return connection.ExecuteAsync("""INSERT INTO "Notification" (user_id, text) VALUES (@UserId, @Text);""",
                                           new { UserId = userId, Text = text },
                                           transaction);
        }
    }
}
