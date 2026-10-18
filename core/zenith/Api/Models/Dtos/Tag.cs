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
        }
    }
}
