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

            Dictionary<string, long> tagIds = (await _tagRepository.GetTagsForUserAsync(userId))
                                              .Where(tag => tag.IsSystem && tag.Key != null)
                                              .ToDictionary(tag => tag.Key!, tag => tag.Id);

            TransactionAccountRow[] accounts = await _transactionRepository.GetAccountsForUserAsync(userId);
            OwnerProfile owner = new(await _userRepository.GetFullNameAsync(userId),
                                     accounts.Select(account => account.Iban));

            List<(long TransactionId, long TagId)> changes = new();

            foreach (CategorizationCandidate candidate in candidates)
            {
                string key = RuleCategorizer.Categorize(new CategorizationInput(candidate.CounterpartyName,
                                                                                candidate.RemittanceInformation,
                                                                                candidate.BankTransactionCode,
                                                                                candidate.CounterpartyAccount,
                                                                                candidate.CreditDebitIndicator,
                                                                                candidate.OriginalCurrency),
                                                        owner);

                if (!tagIds.TryGetValue(key, out long tagId)) continue;
                if (candidate.TagSource == RuleSource && candidate.TagId == tagId) continue;

                changes.Add((candidate.Id, tagId));
            }

            if (changes.Count > 0) await _transactionRepository.ApplyRuleTagsAsync(userId, changes, DateTime.UtcNow);

            return changes.Count;
        }
    }
}
