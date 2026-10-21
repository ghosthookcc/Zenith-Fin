using System.Globalization;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.Ledger;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record SharingResult(int StatusCode,
                                       string? Message);

    public sealed class SharingService
    {
        private readonly SharingRepository _sharingRepository;
        private readonly LedgerRepository _ledgerRepository;
        private readonly UserRepository _userRepository;
        private readonly TransactionRepository _transactionRepository;

        private static readonly SharingResult Ok = new(StatusCodes.Status200OK, null);
        private static readonly SharingResult Unauthorized = new(StatusCodes.Status401Unauthorized, "No user found for session");

        public SharingService(SharingRepository sharingRepository,
                              LedgerRepository ledgerRepository,
                              UserRepository userRepository,
                              TransactionRepository transactionRepository)
        {
            _sharingRepository = sharingRepository;
            _ledgerRepository = ledgerRepository;
            _userRepository = userRepository;
            _transactionRepository = transactionRepository;
        }

        public async Task<SharingResult> ProposeArrangementAsync(string sessionId,
                                                                 long transactionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            PartnershipRow? partnership = await _sharingRepository.GetPartnershipAsync(userId.Value);

            if (partnership?.Status != "ACCEPTED") return BadRequest("Start sharing your economy with your partner first");

            CategorizationCandidate? transaction = await _transactionRepository.GetCategorizationCandidateAsync(userId.Value, transactionId);

            if (transaction == null) return NotFound("Only payments from your own accounts can be shared this way");
            if (transaction.Amount >= 0) return BadRequest("Only money going out can be shared as a cost");

            string? description = Utility.TransactionText.Describe(transaction.CounterpartyName, transaction.RemittanceInformation);
            string? signature = Categorization.CorrectionKey.SignatureOf(description);

            if (signature == null || description == null) return BadRequest("This payment has no name or message to recognise it by");

            string me = await _sharingRepository.GetUserNameAsync(userId.Value);
            DateOnly validFrom = DateOnly.ParseExact(transaction.OccurredOn, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            string? error = await _sharingRepository.ProposeArrangementAsync(userId.Value,
                                                                             partnership.Id,
                                                                             partnership.PartnerOf(userId.Value),
                                                                             transaction.AccountId,
                                                                             signature,
                                                                             transaction.CreditDebitIndicator,
                                                                             description,
                                                                             validFrom,
                                                                             $"{me} wants to share the payment \"{description}\" from {validFrom:yyyy-MM-dd} on, split like your other shared costs.");

            return error == null ? Ok : new SharingResult(StatusCodes.Status409Conflict, error);
        }

        public async Task<SharingResult> RespondArrangementAsync(string sessionId,
                                                                 long arrangementId,
                                                                 bool accept)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            string me = await _sharingRepository.GetUserNameAsync(userId.Value);

            return await _sharingRepository.RespondArrangementAsync(userId.Value,
                                                                    arrangementId,
                                                                    accept,
                                                                    accept ? $"{me} agreed to share your payment." : $"{me} declined sharing your payment.") == null
                ? NotFound("That suggestion is no longer waiting for you")
                : Ok;
        }

        public async Task<SharingResult> EndArrangementAsync(string sessionId,
                                                             long arrangementId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            string me = await _sharingRepository.GetUserNameAsync(userId.Value);

            return await _sharingRepository.EndArrangementAsync(userId.Value,
                                                                arrangementId,
                                                                $"{me} stopped sharing a payment. Later payments are private again.") == null
                ? NotFound("That payment isn't shared")
                : Ok;
        }

        public async Task<SharingDto.State?> GetStateAsync(string sessionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            long me = userId.Value;
            SharingDto.State state = new() { Me = new SharingDto.Person { Id = me, Name = await _sharingRepository.GetUserNameAsync(me) } };

            PartnershipRow? partnership = await _sharingRepository.GetPartnershipAsync(me);
            LedgerAccountRow[] myAccounts = await _ledgerRepository.GetAccountsAsync(new[] { me });

            AccountSharingRow[] sharings = Array.Empty<AccountSharingRow>();
            HashSet<string> partnerKeys = new();

            if (partnership != null)
            {
                long partnerId = partnership.PartnerOf(me);

                state.Partnership = new SharingDto.Partnership
                {
                    Id = partnership.Id,
                    Status = partnership.Status,
                    Partner = new SharingDto.Person { Id = partnerId, Name = await _sharingRepository.GetUserNameAsync(partnerId) },
                    SentByMe = partnership.InviterId == me,
                    CreatedAt = partnership.CreatedAt,
                    RespondedAt = partnership.RespondedAt
                };

                state.SplitRules = (await _sharingRepository.GetSplitRulesAsync(partnership.Id))
                                   .Where(rule => rule.Status is "ACCEPTED" or "PENDING")
                                   .Select(rule => ToSplitRule(rule, partnership, me))
                                   .ToArray();

                if (partnership.Status == "ACCEPTED")
                {
                    sharings = await _sharingRepository.GetAccountSharingsAsync(partnership.Id);
                    partnerKeys = (await _ledgerRepository.GetAccountsAsync(new[] { partnerId }))
                                  .Select(row => LedgerService.ToAccount(row).PhysicalKey)
                                  .ToHashSet();
                }

                state.PartnerAccounts = sharings.Where(sharing => sharing.OwnerId != me && sharing.Status == "ACCEPTED")
                                                .Select(sharing => new SharingDto.SharedAccount
                                                {
                                                    SharingId = sharing.Id,
                                                    AccountId = sharing.AccountId,
                                                    Name = sharing.Name,
                                                    BankName = sharing.BankName,
                                                    Iban = sharing.Iban,
                                                    Status = sharing.Status
                                                })
                                                .ToArray();

                List<SharingDto.InboxItem> inbox = new();
                string partnerName = state.Partnership.Partner.Name;

                if (partnership.Status == "PENDING" && partnership.InviteeId == me)
                {
                    SharingDto.SplitRule? proposal = state.SplitRules.FirstOrDefault(rule => rule.Status == "PENDING");

                    inbox.Add(new SharingDto.InboxItem
                    {
                        Kind = "partnership",
                        Id = partnership.Id,
                        Text = $"{partnerName} wants to share your economy" + (proposal == null ? "." : $", split {Percent(proposal.MyShare)} you and {Percent(proposal.PartnerShare)} {partnerName}."),
                        CreatedAt = partnership.CreatedAt
                    });
                }

                inbox.AddRange(sharings.Where(sharing => sharing.OwnerId != me && sharing.Status == "PENDING")
                                       .Select(sharing => new SharingDto.InboxItem
                                       {
                                           Kind = "account",
                                           Id = sharing.Id,
                                           Text = $"{partnerName} wants to share the account {sharing.Name} ({sharing.BankName}) with you.",
                                           CreatedAt = sharing.CreatedAt
                                       }));

                if (partnership.Status == "ACCEPTED")
                    inbox.AddRange((await _sharingRepository.GetArrangementsAsync(partnership.Id))
                                   .Where(arrangement => arrangement.Status == "PENDING" && arrangement.ProposedBy != me)
                                   .Select(arrangement => new SharingDto.InboxItem
                                   {
                                       Kind = "arrangement",
                                       Id = arrangement.Id,
                                       Text = $"{partnerName} wants to share the payment \"{arrangement.Label}\" from {arrangement.AccountName}, from {arrangement.ValidFrom} on.",
                                       CreatedAt = arrangement.CreatedAt
                                   }));

                if (partnership.Status == "ACCEPTED")
                    inbox.AddRange(state.SplitRules.Where(rule => rule.Status == "PENDING" && !rule.ProposedByMe)
                                                   .Select(rule => new SharingDto.InboxItem
                                                   {
                                                       Kind = "split",
                                                       Id = rule.Id,
                                                       Text = $"{partnerName} suggests splitting shared costs {Percent(rule.MyShare)} you and {Percent(rule.PartnerShare)} {partnerName} from {rule.ValidFrom}.",
                                                       CreatedAt = DateTime.UtcNow
                                                   }));

                state.Inbox = inbox;
            }

            state.MyAccounts = myAccounts.Select(row =>
            {
                AccountSharingRow? sharing = sharings.Where(candidate => candidate.AccountId == row.Id && candidate.Status is "PENDING" or "ACCEPTED")
                                                     .OrderByDescending(candidate => candidate.Id)
                                                     .FirstOrDefault();

                return new SharingDto.ShareableAccount
                {
                    AccountId = row.Id,
                    Name = row.Name,
                    BankName = row.BankName ?? "",
                    Iban = row.Iban,
                    SharingId = sharing?.Id,
                    Status = sharing?.Status,
                    PartnerHasIt = partnerKeys.Contains(LedgerService.ToAccount(row).PhysicalKey)
                };
            }).ToArray();

            NotificationRow[] notifications = await _sharingRepository.GetNotificationsAsync(me);

            state.Notifications = notifications.Select(row => new SharingDto.Notification
            {
                Id = row.Id,
                Text = row.Text,
                CreatedAt = row.CreatedAt,
                Read = row.Read
            }).ToArray();
            state.Unread = notifications.Count(row => !row.Read);

            return state;
        }

        public async Task<int?> CountInboxAsync(string sessionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            return userId == null ? null : await _sharingRepository.CountInboxAsync(userId.Value);
        }

        public async Task<SharingResult> InvitePartnerAsync(string sessionId,
                                                            SharingDto.InvitePartnerRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            string email = request.Email?.Trim() ?? "";

            if (email.Length is < 3 or > 255 || !email.Contains('@')) return BadRequest("Enter the email your partner signs in with");

            int? myShare = ToBasisPoints(request.MyShare);

            if (myShare == null) return BadRequest("Your share has to be between 0 and 100 %");

            (long Id, string Name)? partner = await _sharingRepository.FindUserByEmailAsync(email);

            if (partner == null) return NotFound("Nobody uses Zenith with that email yet");
            if (partner.Value.Id == userId.Value) return BadRequest("Invite someone else than yourself");

            string me = await _sharingRepository.GetUserNameAsync(userId.Value);
            string? error = await _sharingRepository.CreatePartnershipAsync(userId.Value,
                                                                            partner.Value.Id,
                                                                            myShare.Value,
                                                                            $"{me} invited you to share your economy, split {Percent(10000 - myShare.Value)} you and {Percent(myShare.Value)} {me}.");

            return error == null ? Ok : new SharingResult(StatusCodes.Status409Conflict, error);
        }

        public async Task<SharingResult> RespondPartnershipAsync(string sessionId,
                                                                 long partnershipId,
                                                                 bool accept)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            string me = await _sharingRepository.GetUserNameAsync(userId.Value);

            return await _sharingRepository.RespondPartnershipAsync(userId.Value,
                                                                    partnershipId,
                                                                    accept,
                                                                    accept ? $"{me} accepted sharing your economy." : $"{me} declined sharing your economy.") == null
                ? NotFound("That invitation is no longer waiting for you")
                : Ok;
        }

        public async Task<SharingResult> EndPartnershipAsync(string sessionId,
                                                             long partnershipId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            string me = await _sharingRepository.GetUserNameAsync(userId.Value);

            return await _sharingRepository.EndPartnershipAsync(userId.Value,
                                                                partnershipId,
                                                                $"{me} stopped sharing your economy. Shared accounts are private again from today.") == null
                ? NotFound("There is nothing to end")
                : Ok;
        }

        public async Task<SharingResult> ShareAccountAsync(string sessionId,
                                                           long accountId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            PartnershipRow? partnership = await _sharingRepository.GetPartnershipAsync(userId.Value);

            if (partnership?.Status != "ACCEPTED") return BadRequest("Start sharing your economy with your partner first");

            string me = await _sharingRepository.GetUserNameAsync(userId.Value);
            string accountName = (await _ledgerRepository.GetAccountsAsync(new[] { userId.Value })).FirstOrDefault(row => row.Id == accountId)?.Name ?? "an account";

            string? error = await _sharingRepository.ShareAccountAsync(userId.Value,
                                                                       partnership.Id,
                                                                       partnership.PartnerOf(userId.Value),
                                                                       accountId,
                                                                       $"{me} wants to share the account {accountName} with you.");

            return error == null ? Ok : new SharingResult(StatusCodes.Status409Conflict, error);
        }

        public async Task<SharingResult> RespondShareAsync(string sessionId,
                                                           long sharingId,
                                                           bool accept)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            string me = await _sharingRepository.GetUserNameAsync(userId.Value);

            return await _sharingRepository.RespondShareAsync(userId.Value,
                                                              sharingId,
                                                              accept,
                                                              accept ? $"{me} accepted your shared account." : $"{me} declined your shared account.") == null
                ? NotFound("That request is no longer waiting for you")
                : Ok;
        }

        public async Task<SharingResult> EndShareAsync(string sessionId,
                                                       long sharingId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            string me = await _sharingRepository.GetUserNameAsync(userId.Value);

            return await _sharingRepository.EndShareAsync(userId.Value,
                                                          sharingId,
                                                          $"{me} stopped sharing an account. It is private again from today.") == null
                ? NotFound("That account isn't shared")
                : Ok;
        }

        public async Task<SharingResult> ProposeSplitAsync(string sessionId,
                                                           SharingDto.ProposeSplitRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            PartnershipRow? partnership = await _sharingRepository.GetPartnershipAsync(userId.Value);

            if (partnership?.Status != "ACCEPTED") return BadRequest("Start sharing your economy with your partner first");

            int? myShare = ToBasisPoints(request.MyShare);

            if (myShare == null) return BadRequest("Your share has to be between 0 and 100 %");

            if (!DateOnly.TryParseExact(request.ValidFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly validFrom) ||
                validFrom.Year < 2000 || validFrom > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1))
                return BadRequest("Choose the date the new split starts");

            int inviterShare = partnership.InviterId == userId.Value ? myShare.Value : 10000 - myShare.Value;
            string me = await _sharingRepository.GetUserNameAsync(userId.Value);

            await _sharingRepository.ProposeSplitAsync(userId.Value,
                                                       partnership.Id,
                                                       partnership.PartnerOf(userId.Value),
                                                       inviterShare,
                                                       validFrom,
                                                       $"{me} suggests a new split from {validFrom:yyyy-MM-dd}: {Percent(10000 - myShare.Value)} you and {Percent(myShare.Value)} {me}.");

            return Ok;
        }

        public async Task<SharingResult> RespondSplitAsync(string sessionId,
                                                           long ruleId,
                                                           bool accept)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            string me = await _sharingRepository.GetUserNameAsync(userId.Value);

            return await _sharingRepository.RespondSplitAsync(userId.Value,
                                                              ruleId,
                                                              accept,
                                                              accept ? $"{me} accepted the new split." : $"{me} declined the new split.") == null
                ? NotFound("That suggestion is no longer waiting for you")
                : Ok;
        }

        public async Task<SharingResult> MarkReadAsync(string sessionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            await _sharingRepository.MarkNotificationsReadAsync(userId.Value);

            return Ok;
        }

        public static SharingDto.SplitRule ToSplitRule(SplitRuleRow rule,
                                                       PartnershipRow partnership,
                                                       long me)
        {
            int myShare = partnership.InviterId == me ? rule.InviterShare : 10000 - rule.InviterShare;

            return new SharingDto.SplitRule
            {
                Id = rule.Id,
                MyShare = myShare / 100m,
                PartnerShare = (10000 - myShare) / 100m,
                ValidFrom = rule.ValidFrom,
                Status = rule.Status,
                ProposedByMe = rule.ProposedBy == me
            };
        }

        private static int? ToBasisPoints(decimal? percent)
        {
            if (percent is not decimal value || value < 0 || value > 100) return null;

            return (int)Math.Round(value * 100, MidpointRounding.AwayFromZero);
        }

        private static string Percent(decimal percent)
        {
            return $"{percent.ToString("0.##", CultureInfo.InvariantCulture)} %";
        }

        private static string Percent(int basisPoints)
        {
            return Percent(basisPoints / 100m);
        }

        private static SharingResult BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);

        private static SharingResult NotFound(string message) => new(StatusCodes.Status404NotFound, message);

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }
    }
}
