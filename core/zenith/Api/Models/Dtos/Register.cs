namespace ZenithFin.Api.Models.Dtos
{
    public static class RegisterDto
    {
        public class RegisterRequest
        {
            public string FirstName { get; set; } = null!;
            public string LastName { get; set; } = null!;
            public string Email { get; set; } = null!;
            public string Phone { get; set; } = null!;
            public string Password { get; set; } = null!;
        }

        public class RegisterResponse
        {
            public string Message { get; set; } = null!;
            public bool Success { get; set; }
            public int Code { get; set; }
        }
    }
}
