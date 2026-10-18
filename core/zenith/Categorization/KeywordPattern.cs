using System.Globalization;
using System.Text;

namespace ZenithFin.Categorization
{
    public static class TextTokens
    {
        private static readonly Dictionary<char, string> FoldedLetters = BuildFoldedLetters();

        public static string[] Tokenize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();

            StringBuilder builder = new(text.Length);

            foreach (char character in text)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;

                char upper = char.ToUpperInvariant(character);

                if (FoldedLetters.TryGetValue(upper, out string? folded)) builder.Append(folded);
                else builder.Append(char.IsLetterOrDigit(upper) ? upper : ' ');
            }

            return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        private static Dictionary<char, string> BuildFoldedLetters()
        {
            (string Letters, string Folded)[] groups =
            {
                ("ÀÁÂÃÄÅĀĂĄ", "A"), ("ÆǼ", "AE"), ("ÇĆĈĊČ", "C"), ("ĎĐÐ", "D"), ("ÈÉÊËĒĔĖĘĚ", "E"), ("ĜĞĠĢ", "G"),
                ("ĤĦ", "H"), ("ÌÍÎÏĨĪĬĮİ", "I"), ("Ĵ", "J"), ("Ķ", "K"), ("ĹĻĽĿŁ", "L"), ("ÑŃŅŇ", "N"),
                ("ÒÓÔÕÖØŌŎŐ", "O"), ("Œ", "OE"), ("ŔŖŘ", "R"), ("ŚŜŞŠ", "S"), ("ẞß", "SS"), ("ŢŤŦ", "T"), ("Þ", "TH"),
                ("ÙÚÛÜŨŪŬŮŰŲ", "U"), ("Ŵ", "W"), ("ÝŶŸ", "Y"), ("ŹŻŽ", "Z")
            };

            Dictionary<char, string> folded = new();

            foreach ((string letters, string replacement) in groups)
                foreach (char letter in letters)
                    folded[letter] = replacement;

            return folded;
        }
    }

    public sealed class KeywordPattern
    {
        private readonly record struct TokenPattern(string Text, bool AnyPrefix, bool AnySuffix)
        {
            public bool Matches(string token)
            {
                return (AnyPrefix, AnySuffix) switch
                {
                    (true, true) => token.Contains(Text, StringComparison.Ordinal),
                    (true, false) => token.EndsWith(Text, StringComparison.Ordinal),
                    (false, true) => token.StartsWith(Text, StringComparison.Ordinal),
                    _ => token == Text
                };
            }
        }

        private readonly TokenPattern[] _tokens;

        public int Length => _tokens.Length;

        public KeywordPattern(string pattern)
        {
            _tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Select(token => new TokenPattern(token.Trim('*'), token.StartsWith('*'), token.EndsWith('*')))
                             .ToArray();
        }

        public bool Matches(IReadOnlyList<string> tokens)
        {
            for (int start = 0; start + _tokens.Length <= tokens.Count; start++)
            {
                int matched = 0;

                while (matched < _tokens.Length && _tokens[matched].Matches(tokens[start + matched])) matched++;

                if (matched == _tokens.Length) return true;
            }

            return false;
        }
    }
}
