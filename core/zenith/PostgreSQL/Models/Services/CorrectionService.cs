using ZenithFin.Api.Models.Dtos;
using ZenithFin.Categorization;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record CorrectionResult(int StatusCode,
                                          string? Message,
                                          TransactionDto.CorrectTagResponse? Response = null);

    public sealed class CorrectionService
    {
        private readonly TransactionRepository _transactionRepository;
        private readonly TagRepository _tagRepository;
        private readonly UserRepository _userRepository;
        private readonly CorrectionRepository _correctionRepository;

        private readonly CategorizationService _categorizationService;

        private const string NoUser = "No user found for session";

        public CorrectionService(TransactionRepository transactionRepository,
                                 TagRepository tagRepository,
                                 UserRepository userRepository,
                                 CorrectionRepository correctionRepository,
                                 CategorizationService categorizationService)
        {
            _transactionRepository = transactionRepository;
            _tagRepository = tagRepository;
            _userRepository = userRepository;
            _correctionRepository = correctionRepository;

            _categorizationService = categorizationService;
        }

        public async Task<CorrectionResult> CorrectAsync(string sessionId,
                                                         long transactionId,
                                                         TransactionDto.CorrectTagRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return new CorrectionResult(StatusCodes.Status401Unauthorized, NoUser);

            TagDto.Tag? tag = (await _tagRepository.GetTagsForUserAsync(userId.Value))
                              .FirstOrDefault(candidate => candidate.Id == request.TagId);

            if (tag == null) return new CorrectionResult(StatusCodes.Status400BadRequest, "Pick one of your categories");

            CategorizationCandidate? transaction = await _transactionRepository.GetCategorizationCandidateAsync(userId.Value, transactionId);
            DateTime now = DateTime.UtcNow;

            if (transaction == null || !await _transactionRepository.SetUserTagAsync(userId.Value, transactionId, tag.Id, now))
                return new CorrectionResult(StatusCodes.Status404NotFound, "Transaction not found");

            List<TransactionDto.TransactionTag> updated = new()
            {
                new TransactionDto.TransactionTag { Id = transactionId, TagId = tag.Id, TagSource = CategorizationService.UserSource }
            };

            bool remembered = false;
            int moved = 0;

            CorrectionKey? key = request.Remember
                ? CorrectionKey.From(CategorizationService.ToInput(transaction), await _categorizationService.GetOwnerAsync(userId.Value))
                : null;

            if (key != null)
            {
                long? replacedTagId = transaction.TagSource is CategorizationService.RuleSource or CategorizationService.AiSource
                    ? transaction.TagId
                    : null;

                await _correctionRepository.SaveCorrectionAsync(userId.Value,
                                                                key.Value,
                                                                TransactionText.Describe(transaction.CounterpartyName, transaction.RemittanceInformation) ?? key.Value.Signature,
                                                                AiCategorizer.Describe(transaction.CreditDebitIndicator,
                                                                                       transaction.BankTransactionCode,
                                                                                       transaction.CounterpartyName,
                                                                                       transaction.RemittanceInformation),
                                                                tag.Id,
                                                                replacedTagId,
                                                                now);

                remembered = true;

                try
                {
                    IReadOnlyList<TagChange> changes = await _categorizationService.CategorizeAsync(userId.Value, reviewRuleTags: true);

                    updated.AddRange(changes.Select(change => new TransactionDto.TransactionTag
                    {
                        Id = change.TransactionId,
                        TagId = change.TagId,
                        TagSource = change.Source
                    }));

                    moved = changes.Count(change => change.Source == CategorizationService.LearnedSource &&
                                                    change.TagId == tag.Id &&
                                                    change.PreviousTagId != tag.Id);
                }
                catch (Exception errno)
                {
                    Console.WriteLine($"Applying a correction to similar transactions failed for user {userId}: {errno}");
                }
            }

            return new CorrectionResult(StatusCodes.Status200OK, null, new TransactionDto.CorrectTagResponse
            {
                Transactions = updated,
                Remembered = remembered,
                Moved = moved
            });
        }

        public async Task<IReadOnlyList<TagDto.Correction>> GetCorrectionsAsync(long userId)
        {
            TagCorrectionRow[] corrections = await _correctionRepository.GetCorrectionsAsync(userId);

            if (corrections.Length == 0) return Array.Empty<TagDto.Correction>();

            OwnerProfile owner = await _categorizationService.GetOwnerAsync(userId);
            Dictionary<CorrectionKey, int> matches = new();

            foreach (CategorizationCandidate transaction in await _transactionRepository.GetCategorizationCandidatesAsync(userId,
                                                                                                                       includeRuleTagged: true,
                                                                                                                       includeUserTagged: true))
            {
                if (CorrectionKey.From(CategorizationService.ToInput(transaction), owner) is CorrectionKey key)
                    matches[key] = matches.GetValueOrDefault(key) + 1;
            }

            return corrections.Select(correction => new TagDto.Correction
            {
                Id = correction.Id,
                Description = correction.Description,
                CreditDebitIndicator = correction.CreditDebitIndicator,
                OwnTransfer = correction.OwnTransfer,
                TagId = correction.TagId,
                ReplacedTagId = correction.ReplacedTagId,
                Matches = matches.GetValueOrDefault(correction.Key),
                UpdatedAt = correction.UpdatedAt
            }).ToArray();
        }

        public async Task<TagOperationResult> ForgetAsync(string sessionId,
                                                          long correctionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return new TagOperationResult(StatusCodes.Status401Unauthorized, NoUser);

            if (!await _correctionRepository.DeleteCorrectionAsync(userId.Value, correctionId))
                return new TagOperationResult(StatusCodes.Status404NotFound, "That change is already forgotten");

            try
            {
                await _categorizationService.CategorizeAsync(userId.Value, reviewRuleTags: true);
            }
            catch (Exception errno)
            {
                Console.WriteLine($"Categorization after forgetting a correction failed for user {userId}: {errno}");
            }

            return new TagOperationResult(StatusCodes.Status200OK, null);
        }

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }
    }
}
