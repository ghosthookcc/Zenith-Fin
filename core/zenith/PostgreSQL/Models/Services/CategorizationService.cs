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

                    source = RuleSource;

                    if (match.UserTagId != null) tagId = match.UserTagId.Value;
                    else if (candidate.TagSource == AiSource) continue;
                    else if (match.Key == null || !systemTagIds.TryGetValue(match.Key, out tagId)) continue;
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
