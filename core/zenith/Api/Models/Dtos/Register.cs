namespace ZenithFin.Api.Models.Dtos
{
    public static class RegisterDto
    {
        public class RegisterRequest
        {
            public required string FirstName { get; set; }
            public required string LastName { get; set; }
            public required string Email { get; set; }
            public required string Phone { get; set; }
            public required string Password { get; set; }
        }

        public class RegisterResponse
        {
            public required string Message { get; set; }
            public required bool Success { get; set; }
            public required int Code { get; set; }
        }
    }
}
