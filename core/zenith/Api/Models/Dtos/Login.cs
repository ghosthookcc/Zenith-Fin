namespace ZenithFin.Api.Models.Dtos
{
    public static class LoginDto
    {
        public class LoginRequest 
        {
            public string Email { get; set; } = null!;
            public string Password { get; set; } = null!;
        }

        public class LoginResponse : CommonDto.Normalized
        {
            public string Url { get; set; } = null!;
            public double JwtLifeSpanInSeconds { get; set; }
        }
    }
}
