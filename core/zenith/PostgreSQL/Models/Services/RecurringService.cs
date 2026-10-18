using System.Globalization;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.Categorization;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record RecurringResult(int StatusCode,
                                         string? Message,
                                         bool? Recurring = null);

    public sealed class RecurringService
    {
        private readonly TransactionRepository _transactionRepository;
        private readonly RecurringRuleRepository _recurringRuleRepository;
        private readonly UserRepository _userRepository;

        private readonly CategorizationService _categorizationService;

        private static readonly string[] VoidStatuses = { "CNCL", "RJCT" };

        public RecurringService(TransactionRepository transactionRepository,
                                RecurringRuleRepository recurringRuleRepository,
                                UserRepository userRepository,
                                CategorizationService categorizationService)
        {
            _transactionRepository = transactionRepository;
            _recurringRuleRepository = recurringRuleRepository;
            _userRepository = userRepository;

            _categorizationService = categorizationService;
        }

        public async Task<Dictionary<CorrectionKey, RecurringSeries>> DetectAsync(long userId,
                                                                                  OwnerProfile owner)
        {
            Dictionary<CorrectionKey, bool> rules = new();

            foreach (RecurringRuleRow rule in await _recurringRuleRepository.GetRulesAsync(userId))
                rules[rule.Key] = rule.Recurring;

            Dictionary<CorrectionKey, RecurringSeries> detected = new();

            var series = (await _transactionRepository.GetCategorizationCandidatesAsync(userId,
                                                                                       includeRuleTagged: true,
                                                                                       includeUserTagged: true))
                         .Where(transaction => !VoidStatuses.Contains(transaction.Status))
                         .Select(transaction => (Key: CorrectionKey.From(CategorizationService.ToInput(transaction), owner),
                                                 Date: DateOnly.ParseExact(transaction.OccurredOn, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
                         .Where(item => item.Key != null)
                         .GroupBy(item => item.Key!.Value);

            foreach (var group in series)
            {
                bool? manual = rules.TryGetValue(group.Key, out bool recurring) ? recurring : null;
                RecurringSeries? found = RecurringDetector.Detect(group.Select(item => item.Date), manual);

                if (found != null) detected[group.Key] = found;
            }

            return detected;
        }

        public async Task<RecurringResult> SetAsync(string sessionId,
                                                    long transactionId,
                                                    TransactionDto.SetRecurringRequest request)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId))
                return new RecurringResult(StatusCodes.Status401Unauthorized, "No user found for session");

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            if (essentials == null) return new RecurringResult(StatusCodes.Status401Unauthorized, "No user found for session");

            CategorizationCandidate? transaction = await _transactionRepository.GetCategorizationCandidateAsync(essentials.UserId, transactionId);

            if (transaction == null) return new RecurringResult(StatusCodes.Status404NotFound, "Transaction not found");

            CorrectionKey? key = CorrectionKey.From(CategorizationService.ToInput(transaction),
                                                    await _categorizationService.GetOwnerAsync(essentials.UserId));

            if (key == null)
                return new RecurringResult(StatusCodes.Status400BadRequest, "This transaction has no name to group its payments by");

            if (request.Recurring is bool recurring)
                await _recurringRuleRepository.SaveRuleAsync(essentials.UserId, key.Value, recurring, DateTime.UtcNow);
            else
                await _recurringRuleRepository.ClearRuleAsync(essentials.UserId, key.Value);

            return new RecurringResult(StatusCodes.Status200OK, null, request.Recurring);
        }
    }
}
