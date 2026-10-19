using System.Collections.Concurrent;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.Categorization;
using ZenithFin.AI;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed class AiCategorizationService
    {
        private readonly TransactionRepository _transactionRepository;
        private readonly TagRepository _tagRepository;
        private readonly CorrectionRepository _correctionRepository;

        private readonly AiClient _ai;

        private readonly ConcurrentDictionary<long, TagDto.AiStatus> _statuses = new();

        private static readonly SemaphoreSlim[] RunLocks = Enumerable.Range(0, 16)
                                                                     .Select(_ => new SemaphoreSlim(1, 1))
                                                                     .ToArray();

        public AiCategorizationService(TransactionRepository transactionRepository,
                                       TagRepository tagRepository,
                                       CorrectionRepository correctionRepository,
                                       AiClient ai)
        {
            _transactionRepository = transactionRepository;
            _tagRepository = tagRepository;
            _correctionRepository = correctionRepository;

            _ai = ai;
        }

        public TagDto.AiStatus GetStatus(long userId)
        {
            _statuses.TryGetValue(userId, out TagDto.AiStatus? known);

            return new TagDto.AiStatus
            {
                Enabled = _ai.Options.IsConfigured,
                Model = _ai.Options.Model,
                Provider = _ai.Options.Provider,
                LastRunAt = known?.LastRunAt,
                LastTagged = known?.LastTagged,
                LastError = known?.LastError
            };
        }

        public async Task<int> CategorizeAsync(long userId)
        {
            if (!_ai.Options.IsConfigured) return 0;

            SemaphoreSlim runLock = RunLocks[(int)((ulong)userId % (ulong)RunLocks.Length)];

            await runLock.WaitAsync();

            try
            {
                (int tagged, string? error) = await CategorizeUnlockedAsync(userId);

                _statuses[userId] = new TagDto.AiStatus { LastRunAt = DateTime.UtcNow, LastTagged = tagged, LastError = error };

                if (tagged > 0) Console.WriteLine($"AI categorization: user={userId}, tagged={tagged}");

                return tagged;
            }
            catch (Exception errno)
            {
                Console.WriteLine($"AI categorization failed for user {userId}: {errno}");

                _statuses[userId] = new TagDto.AiStatus
                {
                    LastRunAt = DateTime.UtcNow,
                    LastError = errno is AiException or HttpRequestException or TaskCanceledException
                        ? errno.Message
                        : "Unexpected error while sorting with AI."
                };

                return 0;
            }
            finally
            {
                runLock.Release();
            }
        }

        public async Task ReopenOtherAsync(long userId)
        {
            TagDto.Tag? other = (await _tagRepository.GetTagsForUserAsync(userId))
                                .FirstOrDefault(tag => tag.IsSystem && tag.Key == CategoryRules.Other);

            if (other != null) await _transactionRepository.ReopenAiOtherAsync(userId, other.Id);
        }

        private async Task<(int Tagged, string? Error)> CategorizeUnlockedAsync(long userId)
        {
            TagDto.Tag[] tags = await _tagRepository.GetTagsForUserAsync(userId);
            TagDto.Tag? other = tags.FirstOrDefault(tag => tag.IsSystem && tag.Key == CategoryRules.Other);

            if (other == null) return (0, null);

            AiCandidateRow[] candidates = await _transactionRepository.GetAiCandidatesAsync(userId, other.Id);

            if (candidates.Length == 0) return (0, null);

            AiCategory[] categories = tags.Select(tag => new AiCategory(tag.Id, tag.Name, tag.Kind, tag.Key, tag.Keywords))
                                          .ToArray();

            Dictionary<string, long> tagIds = categories.ToDictionary(category => category.Name,
                                                                      category => category.TagId,
                                                                      StringComparer.OrdinalIgnoreCase);

            Dictionary<long, string> tagNames = categories.ToDictionary(category => category.TagId, category => category.Name);

            AiExample[] examples = (await _correctionRepository.GetCorrectionsAsync(userId))
                                   .Where(correction => tagNames.ContainsKey(correction.TagId))
                                   .Take(MaxExamples)
                                   .Select(correction => new AiExample(correction.Example,
                                                                       tagNames[correction.TagId],
                                                                       correction.ReplacedTagId is long replaced
                                                                           ? tagNames.GetValueOrDefault(replaced)
                                                                           : null))
                                   .ToArray();

            IReadOnlyList<AiItem> items = AiCategorizer.Group(candidates.Select(candidate => new AiTransaction(candidate.Id,
                                                                                                             candidate.CounterpartyName,
                                                                                                             candidate.RemittanceInformation,
                                                                                                             candidate.BankTransactionCode,
                                                                                                             candidate.CreditDebitIndicator,
                                                                                                             candidate.Amount,
                                                                                                             candidate.Currency)));

            int batchSize = Math.Max(1, _ai.Options.BatchSize);
            int tagged = 0;
            TimeSpan waited = TimeSpan.Zero;

            foreach (AiItem[] batch in items.Take(batchSize * Math.Max(1, _ai.Options.MaxBatchesPerRun)).Chunk(batchSize))
            {
                AiPrompt prompt = AiCategorizer.BuildPrompt(batch, categories, examples);
                string json;

                try
                {
                    (json, waited) = await CompleteWithRateLimitAsync(prompt, waited);
                }
                catch (Exception errno) when (errno is AiException or HttpRequestException or TaskCanceledException)
                {
                    Console.WriteLine($"AI categorization stopped for user {userId} after {tagged} transactions: {errno.Message}");
                    return (tagged, errno.Message);
                }

                Dictionary<int, string> choices = AiCategorizer.ParseChoices(json, batch.Length);
                List<(long TransactionId, long TagId)> changes = new();

                if (choices.Count == 0) continue;

                for (int position = 0; position < batch.Length; position++)
                {
                    long tagId = choices.TryGetValue(position, out string? category) &&
                                 tagIds.TryGetValue(category, out long chosenTagId)
                        ? chosenTagId
                        : other.Id;

                    changes.AddRange(batch[position].TransactionIds.Select(transactionId => (transactionId, tagId)));
                }

                if (changes.Count > 0)
                    tagged += await _transactionRepository.ApplyAiTagsAsync(userId, changes, other.Id, DateTime.UtcNow);
            }

            return (tagged, null);
        }

        private async Task<(string Json, TimeSpan Waited)> CompleteWithRateLimitAsync(AiPrompt prompt,
                                                                                    TimeSpan waited)
        {
            while (true)
            {
                try
                {
                    string json = await _ai.CompleteJsonAsync(prompt.Instructions,
                                                              prompt.Input,
                                                              AiCategorizer.SchemaName,
                                                              prompt.Schema);

                    return (json, waited);
                }
                catch (AiException errno) when (errno.RetryAfter is TimeSpan delay &&
                                                delay >= TimeSpan.Zero &&
                                                waited + delay <= MaxRateLimitWaitPerRun)
                {
                    TimeSpan pause = delay + TimeSpan.FromMilliseconds(500);

                    Console.WriteLine($"AI rate limited, waiting {pause.TotalSeconds:F1}s before retrying");

                    await Task.Delay(pause);
                    waited += pause;
                }
            }
        }

        private static readonly TimeSpan MaxRateLimitWaitPerRun = TimeSpan.FromSeconds(60);

        private const int MaxExamples = 30;
    }
}
