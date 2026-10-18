using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

namespace ZenithFin.AI
{
    public sealed class AiException : Exception
    {
        public int? StatusCode { get; }
        public TimeSpan? RetryAfter { get; }

        public AiException(string message,
                           int? statusCode = null,
                           TimeSpan? retryAfter = null) : base(message)
        {
            StatusCode = statusCode;
            RetryAfter = retryAfter;
        }
    }

    public sealed class AiClient
    {
        private readonly HttpClient _http;

        public AiOptions Options { get; }

        public AiClient(IOptions<AiOptions> options)
        {
            Options = options.Value;

            _http = new HttpClient
            {
                BaseAddress = Uri.TryCreate(Options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out Uri? baseAddress) ? baseAddress : null,
                Timeout = TimeSpan.FromSeconds(120)
            };
        }

        public async Task<string> CompleteJsonAsync(string instructions,
                                                    string input,
                                                    string schemaName,
                                                    JsonObject schema,
                                                    CancellationToken cancellationToken = default)
        {
            JsonObject body = new()
            {
                ["model"] = Options.Model,
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = instructions },
                    new JsonObject { ["role"] = "user", ["content"] = input }),
                ["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject
                    {
                        ["name"] = schemaName,
                        ["strict"] = true,
                        ["schema"] = schema
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(Options.ReasoningEffort)) body["reasoning_effort"] = Options.ReasoningEffort;
            if (Options.MaxOutputTokens > 0) body["max_completion_tokens"] = Options.MaxOutputTokens;

            using HttpRequestMessage request = new(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(Options.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.ApiKey);

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            string text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw Failure(text, (int)response.StatusCode, response.Headers.RetryAfter);

            return StripCodeFence(MessageContent(text));
        }

        private static string MessageContent(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("choices", out JsonElement choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
                throw new AiException("The AI returned no answer");

            JsonElement choice = choices[0];

            if (choice.TryGetProperty("finish_reason", out JsonElement finish) &&
                finish.ValueKind == JsonValueKind.String &&
                finish.GetString() is "length" or "content_filter")
                throw new AiException($"The AI stopped before finishing ({finish.GetString()})");

            if (!choice.TryGetProperty("message", out JsonElement message) || message.ValueKind != JsonValueKind.Object)
                throw new AiException("The AI returned no message");

            if (message.TryGetProperty("refusal", out JsonElement refusal) && refusal.ValueKind == JsonValueKind.String)
                throw new AiException("The AI refused the request: " + refusal.GetString());

            if (!message.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.String)
                throw new AiException("The AI returned no text");

            return content.GetString() ?? "";
        }

        private static string StripCodeFence(string text)
        {
            string trimmed = text.Trim();

            if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

            int firstLineEnd = trimmed.IndexOf('\n');
            int closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

            return firstLineEnd >= 0 && closingFence > firstLineEnd
                ? trimmed[(firstLineEnd + 1)..closingFence].Trim()
                : trimmed;
        }

        private AiException Failure(string body,
                                    int statusCode,
                                    RetryConditionHeaderValue? retryHeader)
        {
            string? message = null;
            string? code = null;
            string? failedGeneration = null;

            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                JsonElement root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0];

                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out JsonElement error))
                {
                    if (error.ValueKind == JsonValueKind.String) message = error.GetString();

                    if (error.ValueKind == JsonValueKind.Object)
                    {
                        message = error.TryGetProperty("message", out JsonElement value) ? value.GetString() : null;
                        code = error.TryGetProperty("code", out JsonElement kind) && kind.ValueKind == JsonValueKind.String ? kind.GetString() : null;
                        failedGeneration = error.TryGetProperty("failed_generation", out JsonElement generation) &&
                                           generation.ValueKind == JsonValueKind.String
                            ? generation.GetString()
                            : null;
                    }
                }
            }
            catch (JsonException)
            {
            }

            if (failedGeneration != null)
                Console.WriteLine($"AI answer that failed validation ({failedGeneration.Length} chars): " +
                                  failedGeneration[..Math.Min(failedGeneration.Length, 400)]);

            if (code == "json_validate_failed")
                return new AiException($"The model's answer was cut off or was not valid JSON. Try a lower ReasoningEffort " +
                                       $"(now {Options.ReasoningEffort ?? "not set"}) or a higher MaxOutputTokens (now {Options.MaxOutputTokens}).",
                                       statusCode);

            if (statusCode == 429)
                return new AiException($"{Options.Provider} rate limit reached. The rest is sorted at the next sync.",
                                       statusCode,
                                       RetryDelay(retryHeader, message));

            return new AiException(message ?? $"The AI request failed with HTTP {statusCode}", statusCode);
        }

        private static TimeSpan? RetryDelay(RetryConditionHeaderValue? retryHeader,
                                            string? message)
        {
            if (retryHeader?.Delta is TimeSpan delta) return delta;
            if (retryHeader?.Date is DateTimeOffset date) return date - DateTimeOffset.UtcNow;

            Match match = RetryHint.Match(message ?? "");

            if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
                return null;

            return match.Groups[2].Value == "ms" ? TimeSpan.FromMilliseconds(amount) : TimeSpan.FromSeconds(amount);
        }

        private static readonly Regex RetryHint = new(@"try again in ([0-9.]+)(ms|s)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}
