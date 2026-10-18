using System.Globalization;
using System.Text;

namespace ZenithFin.Categorization
{
    public static class TextTokens
    {
        public static string[] Tokenize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();

            string decomposed = text.Normalize(NormalizationForm.FormD);
            StringBuilder builder = new(decomposed.Length);

            foreach (char character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;

                builder.Append(char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : ' ');
            }

            return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
