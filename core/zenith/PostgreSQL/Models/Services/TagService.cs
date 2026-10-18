using System.Text.RegularExpressions;

using Npgsql;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.Categorization;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record TagOperationResult(int StatusCode,
                                            string? Message,
                                            TagDto.Tag? Tag = null);

    public sealed class TagService
    {
        private readonly TagRepository _tagRepository;
        private readonly UserRepository _userRepository;

        private readonly CategorizationService _categorizationService;
        private readonly AiCategorizationService _aiCategorizationService;
        private readonly CorrectionService _correctionService;

        private sealed record ValidTag(string Name,
                                       string Icon,
                                       string Color,
                                       string Kind,
                                       string[] Keywords);

        private const int MaxNameLength = 40;
        private const int MaxKeywordLength = 40;
        private const int MaxKeywords = 20;

        private static readonly string[] Kinds = { "SPENDING", "INCOME", "TRANSFER" };

        private static readonly Regex IconFormat = new(@"^[a-z][a-zA-Z0-9]{0,23}$", RegexOptions.Compiled);
        private static readonly Regex ColorFormat = new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

        private static readonly TagOperationResult Unauthorized = new(StatusCodes.Status401Unauthorized, "No user found for session");

        public TagService(TagRepository tagRepository,
                          UserRepository userRepository,
                          CategorizationService categorizationService,
                          AiCategorizationService aiCategorizationService,
                          CorrectionService correctionService)
        {
            _tagRepository = tagRepository;
            _userRepository = userRepository;

            _categorizationService = categorizationService;
            _aiCategorizationService = aiCategorizationService;
            _correctionService = correctionService;
        }

        public async Task<TagDto.TagsResponse?> GetTagsAsync(string sessionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            return new TagDto.TagsResponse
            {
                Tags = await _tagRepository.GetTagsForUserAsync(userId.Value),
                Usage = await _tagRepository.GetTagUsageAsync(userId.Value),
                Ai = _aiCategorizationService.GetStatus(userId.Value),
                Corrections = await _correctionService.GetCorrectionsAsync(userId.Value)
            };
        }

        public async Task<TagOperationResult> CreateTagAsync(string sessionId,
                                                             TagDto.SaveTagRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            TagDto.Tag[] tags = await _tagRepository.GetTagsForUserAsync(userId.Value);
            (ValidTag? tag, TagOperationResult? invalid) = Validate(request, tags, null);

            if (tag == null) return invalid!;

            long tagId;

            try
            {
                tagId = await _tagRepository.InsertTagAsync(userId.Value, tag.Name, tag.Icon, tag.Color, tag.Kind, tag.Keywords);
            }
            catch (PostgresException errno) when (errno.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return DuplicateName(tag.Name);
            }

            return await CompleteAsync(userId.Value, tagId);
        }

        public async Task<TagOperationResult> UpdateTagAsync(string sessionId,
                                                             long tagId,
                                                             TagDto.SaveTagRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            TagDto.Tag[] tags = await _tagRepository.GetTagsForUserAsync(userId.Value);
            TagOperationResult? notEditable = CheckEditable(tags, tagId);

            if (notEditable != null) return notEditable;

            (ValidTag? tag, TagOperationResult? invalid) = Validate(request, tags, tagId);

            if (tag == null) return invalid!;

            try
            {
                bool updated = await _tagRepository.UpdateTagAsync(userId.Value, tagId, tag.Name, tag.Icon, tag.Color, tag.Kind, tag.Keywords);

                if (!updated) return NotFound();
            }
            catch (PostgresException errno) when (errno.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return DuplicateName(tag.Name);
            }

            return await CompleteAsync(userId.Value, tagId);
        }

        public async Task<TagOperationResult> DeleteTagAsync(string sessionId,
                                                             long tagId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            TagOperationResult? notEditable = CheckEditable(await _tagRepository.GetTagsForUserAsync(userId.Value), tagId);

            if (notEditable != null) return notEditable;

            if (!await _tagRepository.DeleteTagAsync(userId.Value, tagId)) return NotFound();

            await RecategorizeAsync(userId.Value, reopenAiOther: false);

            return new TagOperationResult(StatusCodes.Status200OK, null);
        }

        private async Task<TagOperationResult> CompleteAsync(long userId,
                                                             long tagId)
        {
            await RecategorizeAsync(userId, reopenAiOther: true);

            TagDto.Tag? saved = (await _tagRepository.GetTagsForUserAsync(userId)).FirstOrDefault(tag => tag.Id == tagId);

            return saved == null ? NotFound() : new TagOperationResult(StatusCodes.Status200OK, null, saved);
        }

        private async Task RecategorizeAsync(long userId,
                                             bool reopenAiOther)
        {
            try
            {
                if (reopenAiOther) await _aiCategorizationService.ReopenOtherAsync(userId);

                await _categorizationService.CategorizeAsync(userId, reviewRuleTags: true);
            }
            catch (Exception errno)
            {
                Console.WriteLine($"Categorization after a tag change failed for user {userId}: {errno}");
            }
        }

        private static (ValidTag? Tag, TagOperationResult? Invalid) Validate(TagDto.SaveTagRequest request,
                                                                             IReadOnlyList<TagDto.Tag> existing,
                                                                             long? editedTagId)
        {
            string? name = TransactionText.Clean(request.Name);

            if (name == null) return (null, BadRequest("Give the category a name"));
            if (name.Length > MaxNameLength) return (null, BadRequest($"Names can be at most {MaxNameLength} characters"));

            if (existing.Any(tag => tag.Id != editedTagId && string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)))
                return (null, DuplicateName(name));

            string icon = request.Icon?.Trim() ?? "";

            if (!IconFormat.IsMatch(icon)) return (null, BadRequest("Pick an icon for the category"));

            string color = request.Color?.Trim() ?? "";

            if (!ColorFormat.IsMatch(color)) return (null, BadRequest("Colors must look like #1a2b3c"));

            string kind = request.Kind?.Trim().ToUpperInvariant() ?? "";

            if (!Kinds.Contains(kind)) return (null, BadRequest("Choose whether the category is spending, income or a transfer"));

            string[] keywords = (request.Keywords ?? Array.Empty<string>())
                                .Select(TransactionText.Clean)
                                .OfType<string>()
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray();

            if (keywords.Length > MaxKeywords) return (null, BadRequest($"Use at most {MaxKeywords} match words"));
            if (keywords.Any(keyword => keyword.Length > MaxKeywordLength))
                return (null, BadRequest($"Match words can be at most {MaxKeywordLength} characters"));
            if (keywords.Any(keyword => TextTokens.Tokenize(keyword).Sum(token => token.Length) < 2))
                return (null, BadRequest("Match words need at least two letters or digits"));

            return (new ValidTag(name, icon, color.ToLowerInvariant(), kind, keywords), null);
        }

        private static TagOperationResult? CheckEditable(IReadOnlyList<TagDto.Tag> tags,
                                                         long tagId)
        {
            TagDto.Tag? tag = tags.FirstOrDefault(candidate => candidate.Id == tagId);

            if (tag == null) return NotFound();
            if (tag.IsSystem) return new TagOperationResult(StatusCodes.Status403Forbidden, "Predefined categories can't be changed");

            return null;
        }

        private static TagOperationResult BadRequest(string message)
        {
            return new TagOperationResult(StatusCodes.Status400BadRequest, message);
        }

        private static TagOperationResult NotFound()
        {
            return new TagOperationResult(StatusCodes.Status404NotFound, "Category not found");
        }

        private static TagOperationResult DuplicateName(string name)
        {
            return new TagOperationResult(StatusCodes.Status409Conflict, $"There is already a category called {name}");
        }

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }
    }
}
