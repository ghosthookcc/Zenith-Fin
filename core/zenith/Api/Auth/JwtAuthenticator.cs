using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ZenithFin.PostgreSQL.Models.Repositories;
using Microsoft.AspNetCore.DataProtection;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.Utility;

namespace ZenithFin.Api.Auth
{
    public class JwtAuthenticator
    {
        private readonly IConfiguration _config;
        private readonly UserRepository _userRepository;

        public readonly Protector Protector;

        public JwtAuthenticator(IConfiguration config,
                                UserRepository userRepository,
                                IDataProtectionProvider provider)
        {
            _config = config;
            _userRepository = userRepository;

            Protector = new(provider,
                            ProtectorPurpose.UserSession);
        }

        public string CreateJwtForSession(long userId,
                                          string sessionId,
                                          string rawSecret,
                                          DateTime expiresAt)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(rawSecret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier,
                          userId.ToString()),
                new Claim(ClaimTypes.Sid,
                          sessionId)
            };

            JwtSecurityToken token = new JwtSecurityToken(issuer: "https://localhost:4446/",
                                                          audience: "https://localhost:4444/",
                                                          claims: claims,
                                                          expires: expiresAt,
                                                          signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async Task<ClaimsPrincipal?> ValidateJwtForSession(string jwt)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();

                var token = handler.ReadJwtToken(jwt);

                Console.WriteLine("Claims in token:");
                foreach (var claim in token.Claims)
                {
                    Console.WriteLine($"   Type: {claim.Type}, Value: {claim.Value}");
                }

                string? sessionId = token.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Sid)?.Value;
                if (string.IsNullOrEmpty(sessionId))
                {
                    Console.WriteLine("No session ID in token");
                    return null;
                }

                ActiveSession? session = await _userRepository.GetSessionById(sessionId);
                if (session == null)
                {
                    Console.WriteLine($"Session not found: {sessionId}");
                    return null;
                }

                if (session.ExpiresAt < DateTime.UtcNow)
                {
                    Console.WriteLine($"Session expired: {sessionId}");
                    return null;
                }

                string rawSecret = Protector.Decrypt(session.JwtSecretEncrypted);

                var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(rawSecret));

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    ValidIssuer = "https://localhost:4446/",
                    ValidAudience = "https://localhost:4444/",

                    IssuerSigningKey = signingKey,

                    ClockSkew = TimeSpan.FromSeconds(90.0)
                };

                return handler.ValidateToken(jwt,
                                             validationParameters,
                                             out _);
            }
            catch (Exception errno)
            {
                Console.WriteLine(errno.Message);
                return null;
            }
        }
    }
}
