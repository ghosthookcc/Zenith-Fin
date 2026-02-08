namespace ZenithFin.Api.Models.Dtos
{
    public static class LoginDto
    {
        public class LoginRequest 
        {
            public string Email { get; set; } = null!;
            public string Password { get; set; } = null!;
        }

        public class LoginResponse
        {
            public string Message { get; set; } = null!;
            public bool Success { get; set; }
            public int Code { get; set; }
            public string Url { get; set; } = null!;
        }
    }
}
