namespace ZenithFin.Ledger
{
    public sealed class AccountNumberIndex
    {
        private const int MinimumDigits = 6;
        private const int SwedishIbanPrefixDigits = 5;

        private readonly List<(string Key, string Digits, string? Local)> _numbers = new();

        public AccountNumberIndex(IEnumerable<(string Key, string? Number)> accounts)
        {
            foreach ((string key, string? number) in accounts)
            {
                string digits = Digits(number);

                if (digits.Length < MinimumDigits) continue;

                string? local = null;

                if (number!.TrimStart().StartsWith("SE", StringComparison.OrdinalIgnoreCase) && digits.Length > SwedishIbanPrefixDigits)
                {
                    string candidate = digits[SwedishIbanPrefixDigits..].TrimStart('0');

                    if (candidate.Length >= MinimumDigits) local = candidate;
                }

                _numbers.Add((key, digits, local));
            }
        }

        public string? Find(string? counterpartyAccount)
        {
            string digits = Digits(counterpartyAccount).TrimStart('0');

            if (digits.Length < MinimumDigits) return null;

            string[] keys = _numbers.Where(entry => entry.Digits.EndsWith(digits, StringComparison.Ordinal) ||
                                                    (entry.Local != null && digits.EndsWith(entry.Local, StringComparison.Ordinal)))
                                    .Select(entry => entry.Key)
                                    .Distinct()
                                    .ToArray();

            return keys.Length == 1 ? keys[0] : null;
        }

        public static string Digits(string? value)
        {
            return value == null ? "" : new string(value.Where(char.IsAsciiDigit).ToArray());
        }
    }
}
