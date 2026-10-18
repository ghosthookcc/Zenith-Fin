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

        private const string RuleSource = "RULE";
        private const string AiSource = "AI";

        public CategorizationService(TransactionRepository transactionRepository,
                                     TagRepository tagRepository,
                                     UserRepository userRepository)
        {
            _transactionRepository = transactionRepository;
            _tagRepository = tagRepository;
            _userRepository = userRepository;
        }

        public async Task<int> CategorizeAsync(long userId,
                                               bool reviewRuleTags)
        {
            CategorizationCandidate[] candidates = await _transactionRepository.GetCategorizationCandidatesAsync(userId, reviewRuleTags);

            if (candidates.Length == 0) return 0;

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

            TransactionAccountRow[] accounts = await _transactionRepository.GetAccountsForUserAsync(userId);
            OwnerProfile owner = new(await _userRepository.GetFullNameAsync(userId),
                                     accounts.Select(account => account.Iban));

            List<(long TransactionId, long TagId)> changes = new();

            foreach (CategorizationCandidate candidate in candidates)
            {
                CategoryMatch match = RuleCategorizer.Categorize(new CategorizationInput(candidate.CounterpartyName,
                                                                                         candidate.RemittanceInformation,
                                                                                         candidate.BankTransactionCode,
                                                                                         candidate.CounterpartyAccount,
                                                                                         candidate.CreditDebitIndicator,
                                                                                         candidate.OriginalCurrency),
                                                                 owner,
                                                                 userRules);

                long tagId;

                if (match.UserTagId != null) tagId = match.UserTagId.Value;
                else if (candidate.TagSource == AiSource) continue;
                else if (match.Key == null || !systemTagIds.TryGetValue(match.Key, out tagId)) continue;

                if (candidate.TagSource == RuleSource && candidate.TagId == tagId) continue;

                changes.Add((candidate.Id, tagId));
            }

            if (changes.Count > 0) await _transactionRepository.ApplyRuleTagsAsync(userId, changes, DateTime.UtcNow);

            return changes.Count;
        }
    }
}
