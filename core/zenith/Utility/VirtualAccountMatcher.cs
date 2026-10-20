namespace ZenithFin.Utility
{
    public static class VirtualAccountMatcher
    {
        private const int MinimumMatchDigits = 6;

        public static string[] Keys(string accountNumber,
                                    string? clearingNumber,
                                    string? iban)
        {
            List<string> keys = new() { SignificantDigits(accountNumber) };

            if (!string.IsNullOrEmpty(clearingNumber)) keys.Add(SignificantDigits(clearingNumber + accountNumber));

            string compactIban = new string((iban ?? "").Where(char.IsAsciiLetterOrDigit).ToArray());

            if (compactIban.Length > 4) keys.Add(SignificantDigits(compactIban[4..]));

            return keys.Where(key => key.Length >= MinimumMatchDigits).ToArray();
        }

        public static bool Matches(string? counterpartyAccount,
                                   IReadOnlyCollection<string> keys)
        {
            string counterparty = SignificantDigits(counterpartyAccount);

            return counterparty.Length >= MinimumMatchDigits &&
                   keys.Any(key => counterparty.EndsWith(key, StringComparison.Ordinal) ||
                                   key.EndsWith(counterparty, StringComparison.Ordinal));
        }

        private static string SignificantDigits(string? value)
        {
            return new string((value ?? "").Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
        }
    }
}
