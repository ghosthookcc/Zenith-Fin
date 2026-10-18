namespace ZenithFin.AI
{
    public sealed class AiOptions
    {
        public bool Enabled { get; set; }
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string Model { get; set; } = "gpt-6-luna";
        public string? ApiKey { get; set; }
        public string? ReasoningEffort { get; set; }
        public int BatchSize { get; set; } = 40;
        public int MaxBatchesPerRun { get; set; } = 5;

        public bool IsConfigured => Enabled &&
                                    !string.IsNullOrWhiteSpace(Model) &&
                                    Uri.TryCreate(BaseUrl, UriKind.Absolute, out _);

        public string Provider => Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri? uri) ? uri.Host : BaseUrl;
    }
}
