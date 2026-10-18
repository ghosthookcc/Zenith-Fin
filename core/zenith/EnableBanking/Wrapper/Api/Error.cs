using System.Text.Json;

namespace ZenithFin.EnableBanking
{
    public sealed class EnableBankingException : Exception
    {
        public int StatusCode { get; }
        public string? ErrorCode { get; }

        public EnableBankingException(int statusCode,
                                      string? errorCode,
                                      string message) : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }

        internal static EnableBankingException FromResponse(int statusCode,
                                                            string body)
        {
            string? errorCode = null;
            string? message = null;

            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                JsonElement root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.String)
                        errorCode = error.GetString();

                    if (root.TryGetProperty("message", out JsonElement text) && text.ValueKind == JsonValueKind.String)
                        message = text.GetString();
                }
            }
            catch (JsonException)
            {
            }

            return new EnableBankingException(statusCode,
                                              errorCode,
                                              message ?? $"Enable Banking request failed with HTTP {statusCode}");
        }
    }
}
