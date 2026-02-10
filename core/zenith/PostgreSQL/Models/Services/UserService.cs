using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;
using System.Security.Cryptography;
using ZenithFin.Api.Auth;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed class UserService
    {
        private readonly UserRepository _userRepository;

        public UserService(UserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task RegisterAsync(RegisterDto.RegisterRequest request)
        {
            /*
            var existingUser = await _userRepository.GetByEmailAsync(email);
            if (existingUser != null)
                throw new Exception("Email already in use");
            */

            string fullName = $"{request.FirstName} {request.LastName}";

            UserEntity user = new UserEntity
            {
                FullName = fullName,
                Email = request.Email,
                Phone = request.Phone,
                PasswordHash = Hashing.HashPassword(request.Password)
            };

            await _userRepository.InsertUserAsync(user);
        }

        public async Task<long?> ValidateLoginAsync(LoginDto.LoginRequest request)
        {
            LoginAttempt? attempt = await _userRepository.ValidateLoginAsync(request);
            if (attempt == null)
            {
                return null;
            }
            return Hashing.VerifyPassword(request.Password, attempt.PasswordHash!) ? attempt.UserId : null;
        }

        public async Task<SessionCreated> CreateSessionAsync(long userId, 
                                                             Protector protector)
        {
            Guid sessionId = Guid.NewGuid();

            string jwtSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            string jwtSecretEncrypted = protector.Encrypt(jwtSecret);

            DateTime issuedAt = DateTime.UtcNow;
            DateTime expiresAt = issuedAt.AddMinutes(5);

            var session = new SessionEntity
            { 
                SessionId = sessionId,
                UserId = userId,
                JwtSecretEncrypted = jwtSecretEncrypted,
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt,
            };

            await _userRepository.InsertUserSessionAsync(session);

            return new SessionCreated() 
            { 
                SessionId = sessionId, 
                UserId = userId, 
                RawJwtSecret = jwtSecret, 
                ExpiresAt = expiresAt 
            };
        }
    }
}
