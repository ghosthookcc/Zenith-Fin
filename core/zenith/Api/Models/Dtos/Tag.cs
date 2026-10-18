namespace ZenithFin.Api.Models.Dtos
{
    public static class TagDto
    {
        public sealed class Tag
        {
            public long Id { get; set; }
            public string? Key { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public string Color { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public bool IsSystem { get; set; }
            public string[] Keywords { get; set; } = Array.Empty<string>();
        }

        public sealed class SaveTagRequest
        {
            public string? Name { get; set; }
            public string? Icon { get; set; }
            public string? Color { get; set; }
            public string? Kind { get; set; }
            public string[]? Keywords { get; set; }
        }

        public sealed class TagsResponse
        {
            public IReadOnlyList<Tag> Tags { get; set; } = Array.Empty<Tag>();
            public IReadOnlyDictionary<long, int> Usage { get; set; } = new Dictionary<long, int>();
        }
    }
}
