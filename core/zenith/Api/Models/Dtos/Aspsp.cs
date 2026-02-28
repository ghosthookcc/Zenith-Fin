using System.Text.Json.Serialization;

namespace ZenithFin.Api.Models.Dtos
{
    public static class AspspDto
    {
        public class AuthenticationRequest
        {
            public IReadOnlyList<AuthenticationAspsp> Aspsps { get; set; } = null!;
        }
        public class AuthenticationAspsp
        {
            public string Bank { get; set; } = null!;
            public string Country { get; set; } = null!;
            public string PsuType { get; set; } = null!;
        }

        public class AuthenticationResponse : CommonDto.Normalized
        {
            public List<AspspUrl> Urls { get; set; } = null!;
        }
        public class AspspUrl
        {
            public string Url { get; set; } = null!;
            public string Bank { get; set; } = null!;
        }

        public class AspspsResponse : CommonDto.Normalized
        {
            public AllAspsps Aspsps { get; set; } = null!;
        }

        public class AllAspsps
        {
            public List<Dictionary<string, AspspInformation>> Aspsps { get; set; } = [];
        }

        public class AspspInformation
        {
            [JsonPropertyName("country")] public string Country { get; set; } = null!;
            [JsonPropertyName("psuType")] public string PsuType { get; set; } = null!;
        }

        public class AspspAuthenticationCallbackRequest()
        {
            [JsonPropertyName("state")] public string State { get; set; } = null!;
            [JsonPropertyName("code")] public string Code { get; set; } = null!;
        }
    }
}
