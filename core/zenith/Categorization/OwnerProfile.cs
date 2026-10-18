namespace ZenithFin.Categorization
{
    public sealed class OwnerProfile
    {
        private readonly string? _firstName;
        private readonly string? _lastName;
        private readonly string[] _accountNumbers;
        private readonly string[] _localAccountNumbers;

        private const int MinimumAccountDigits = 7;
        private const int SwedishIbanPrefixDigits = 5;

        public OwnerProfile(string? fullName,
                            IEnumerable<string?> ibans)
        {
            string[] names = TextTokens.Tokenize(fullName);

            if (names.Length >= 2)
            {
                _firstName = names[0];
                _lastName = names[^1];
            }

            string?[] accounts = ibans.ToArray();

            _accountNumbers = accounts.Select(Digits)
                                      .Where(digits => digits.Length > 0)
                                      .ToArray();

            _localAccountNumbers = accounts.Where(iban => iban != null && iban.TrimStart().StartsWith("SE", StringComparison.OrdinalIgnoreCase))
                                           .Select(Digits)
                                           .Where(digits => digits.Length > SwedishIbanPrefixDigits)
                                           .Select(digits => digits[SwedishIbanPrefixDigits..].TrimStart('0'))
                                           .Where(number => number.Length >= MinimumAccountDigits)
                                           .ToArray();
        }

        public bool IsOwnAccount(string? counterpartyAccount)
        {
            string digits = Digits(counterpartyAccount).TrimStart('0');
            string key = (digits.Length > 10 ? digits[^10..] : digits).TrimStart('0');

            return (key.Length >= MinimumAccountDigits && _accountNumbers.Any(number => number.EndsWith(key, StringComparison.Ordinal))) ||
                   _localAccountNumbers.Any(number => digits.EndsWith(number, StringComparison.Ordinal));
        }

        public bool IsOwnName(IReadOnlyList<string> tokens)
        {
            if (_firstName == null || _lastName == null) return false;

            return tokens.Contains(_firstName) &&
                   tokens.Any(token => token.Length >= 4 && _lastName.StartsWith(token, StringComparison.Ordinal));
        }

        private static string Digits(string? value)
        {
            return value == null ? "" : new string(value.Where(char.IsAsciiDigit).ToArray());
        }
    }
}
