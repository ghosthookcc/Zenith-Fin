using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Options;

namespace ZenithFin.AI
{
    public sealed class AiException : Exception
    {
        public int? StatusCode { get; }

        public AiException(string message,
                           int? statusCode = null) : base(message)
        {
            StatusCode = statusCode;
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

            using HttpRequestMessage request = new(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(Options.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.ApiKey);

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            string text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new AiException(ErrorMessage(text) ?? $"The AI request failed with HTTP {(int)response.StatusCode}",
                                      (int)response.StatusCode);

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

        private static string? ErrorMessage(string body)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                JsonElement root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0];

                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("error", out JsonElement error))
                {
                    if (error.ValueKind == JsonValueKind.String) return error.GetString();

                    if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement message))
                        return message.GetString();
                }
            }
            catch (JsonException)
            {
            }

            return null;
        }
    }
}
