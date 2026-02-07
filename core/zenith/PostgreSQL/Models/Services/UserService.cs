using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed class UserService
    {
        public readonly UserRepository _userRepository;

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
    }
}
