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
            public string Url { get; set; } = null!;
        }

        public class AspspsResponse
        {
            public string Message { get; set; } = null!;
            public bool Success { get; set; }
            public int Code { get; set; }
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
    }
}
