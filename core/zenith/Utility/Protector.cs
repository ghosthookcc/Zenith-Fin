using Microsoft.AspNetCore.DataProtection;

namespace ZenithFin.Utility
{
    public static class ProtectorPurpose
    {
        public const string UserSession = "jwt-user-session";
    }
    public sealed class Protector
    {
        private readonly IDataProtector _protector;

        public Protector(IDataProtectionProvider provider,
                         string purpose)
        {
            _protector = provider.CreateProtector(purpose);
        }

        public string Encrypt(string raw) => _protector.Protect(raw);
        public string Decrypt(string encrypted) => _protector.Unprotect(encrypted);
    }
}
