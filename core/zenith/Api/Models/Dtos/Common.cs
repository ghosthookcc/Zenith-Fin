namespace ZenithFin.Api.Models.Dtos
{
    public static class CommonDto
    {
        public class Normalized
        {
            public string Message { get; set; } = null!;
            public bool Success { get; set; }
            public int Code { get; set; }
        }
    }
}
