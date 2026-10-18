using System.Globalization;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.Categorization;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed class CategorizationService
    {
        private readonly TransactionRepository _transactionRepository;
        private readonly TagRepository _tagRepository;
        private readonly UserRepository _userRepository;
        private readonly CorrectionRepository _correctionRepository;

        public const string RuleSource = "RULE";
        public const string AiSource = "AI";
        public const string UserSource = "USER";
        public const string LearnedSource = "LEARNED";

        private const string TransferKind = "TRANSFER";

        public CategorizationService(TransactionRepository transactionRepository,
                                     TagRepository tagRepository,
                                     UserRepository userRepository,
                                     CorrectionRepository correctionRepository)
        {
            _transactionRepository = transactionRepository;
            _tagRepository = tagRepository;
            _userRepository = userRepository;
            _correctionRepository = correctionRepository;
        }

        public async Task<IReadOnlyList<TagChange>> CategorizeAsync(long userId,
                                                                    bool reviewRuleTags)
        {
            CategorizationCandidate[] candidates = await _transactionRepository.GetCategorizationCandidatesAsync(userId, reviewRuleTags);

            if (candidates.Length == 0) return Array.Empty<TagChange>();

            TagDto.Tag[] tags = await _tagRepository.GetTagsForUserAsync(userId);

            Dictionary<string, long> systemTagIds = tags.Where(tag => tag.IsSystem && tag.Key != null)
                                                        .ToDictionary(tag => tag.Key!, tag => tag.Id);

            UserTagRule[] userRules = tags.Where(tag => !tag.IsSystem)
                                          .Select(tag => new UserTagRule(tag.Id,
                                                                         tag.Keywords.Select(KeywordPattern.FromKeyword)
                                                                                     .OfType<KeywordPattern>()
                                                                                     .ToArray()))
                                          .Where(rule => rule.Patterns.Count > 0)
                                          .ToArray();

            OwnerProfile owner = await GetOwnerAsync(userId);

            long? transfersTagId = systemTagIds.TryGetValue(CategoryRules.Transfers, out long systemTransfersId) ? systemTransfersId : null;
            HashSet<long> transferTagIds = tags.Where(tag => tag.Kind == TransferKind).Select(tag => tag.Id).ToHashSet();

            HashSet<long> paired = TransferPairs.Find((await _transactionRepository.GetCategorizationCandidatesAsync(userId,
                                                                                                                   includeRuleTagged: true,
                                                                                                                   includeUserTagged: true))
                                                      .Select(transaction => ToTransferLeg(transaction, owner))
                                                      .ToArray());

            Dictionary<CorrectionKey, long> learned = new();

            foreach (TagCorrectionRow correction in await _correctionRepository.GetCorrectionsAsync(userId))
                learned.TryAdd(correction.Key, correction.TagId);

            List<TagChange> changes = new();

            foreach (CategorizationCandidate candidate in candidates)
            {
                CategorizationInput input = ToInput(candidate);

                long tagId;
                string source;

                if (learned.Count > 0 &&
                    CorrectionKey.From(input, owner) is CorrectionKey key &&
                    learned.TryGetValue(key, out tagId))
                {
                    source = LearnedSource;
                }
                else
                {
                    CategoryMatch match = RuleCategorizer.Categorize(input, owner, userRules);
                    long? ruleTagId = match.UserTagId ??
                                      (match.Key != null && systemTagIds.TryGetValue(match.Key, out long systemTagId) ? systemTagId : null);

                    source = RuleSource;

                    if (paired.Contains(candidate.Id) && transfersTagId != null && (ruleTagId == null || !transferTagIds.Contains(ruleTagId.Value)))
                        ruleTagId = transfersTagId;
                    else if (match.UserTagId == null && match.Key == CategoryRules.Other && candidate.TagSource == AiSource)
                        continue;

                    if (ruleTagId == null) continue;

                    tagId = ruleTagId.Value;
                }

                if (candidate.TagSource == source && candidate.TagId == tagId) continue;

                changes.Add(new TagChange(candidate.Id, tagId, source, candidate.TagId, candidate.TagSource));
            }

            if (changes.Count == 0) return changes;

            HashSet<long> updated = await _transactionRepository.ApplyTagsAsync(userId, changes, DateTime.UtcNow);

            return changes.Where(change => updated.Contains(change.TransactionId)).ToArray();
        }

        public async Task<OwnerProfile> GetOwnerAsync(long userId)
        {
            TransactionAccountRow[] accounts = await _transactionRepository.GetAccountsForUserAsync(userId);

            return new OwnerProfile(await _userRepository.GetFullNameAsync(userId),
                                    accounts.Select(account => account.Iban));
        }

        private static TransferLeg ToTransferLeg(CategorizationCandidate transaction,
                                                 OwnerProfile owner)
        {
            return new TransferLeg(transaction.Id,
                                   transaction.AccountId,
                                   DateOnly.ParseExact(transaction.OccurredOn, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                                   transaction.Amount,
                                   transaction.Currency,
                                   transaction.CreditDebitIndicator,
                                   transaction.BankTransactionCode,
                                   transaction.Status,
                                   RuleCategorizer.IsOwnTransfer(ToInput(transaction), owner));
        }

        public static CategorizationInput ToInput(CategorizationCandidate candidate)
        {
            return new CategorizationInput(candidate.CounterpartyName,
                                           candidate.RemittanceInformation,
                                           candidate.BankTransactionCode,
                                           candidate.CounterpartyAccount,
                                           candidate.CreditDebitIndicator,
                                           candidate.OriginalCurrency);
        }
    }
}
