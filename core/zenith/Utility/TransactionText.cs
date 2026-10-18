using System.Text.RegularExpressions;

namespace ZenithFin.Utility
{
    public static class TransactionText
    {
        private static readonly Regex ControlCharacters = new(@"\p{Cc}", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex CardPurchasePrefix = new(@"^Kortköp\s+\d{6}\s*",
                                                               RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string? Clean(string? value)
        {
            if (value == null) return null;

            string cleaned = WhitespaceRun.Replace(ControlCharacters.Replace(value, " "), " ").Trim();

            return cleaned.Length == 0 ? null : cleaned;
        }

        public static string? Describe(string? counterpartyName,
                                       IReadOnlyList<string> remittanceInformation)
        {
            string? name = Clean(counterpartyName);

            if (name != null) return name;

            foreach (string line in remittanceInformation)
            {
                string? text = Clean(CardPurchasePrefix.Replace(line, ""));

                if (text != null && text.Any(char.IsLetter)) return text;
            }

            return null;
        }
    }
}
