using System.Text.Json;
using ZenithFin.Utility;

namespace ZenithFin.EnableBanking
{
    internal static class EnableBankingJson
    {
        internal static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
            PropertyNameCaseInsensitive = true,
        };
    }
}
