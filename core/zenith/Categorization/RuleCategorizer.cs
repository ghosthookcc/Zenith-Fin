namespace ZenithFin.Categorization
{
    public sealed record CategorizationInput(string? CounterpartyName,
                                             IReadOnlyList<string> RemittanceInformation,
                                             string? BankTransactionCode,
                                             string? CounterpartyAccount,
                                             string CreditDebitIndicator,
                                             string? OriginalCurrency);

    public sealed record UserTagRule(long TagId,
                                     IReadOnlyList<KeywordPattern> Patterns);

    public readonly record struct CategoryMatch(long? UserTagId,
                                                string? Key);

    public static class RuleCategorizer
    {
        private const string FieldSeparator = "|";

        public static CategoryMatch Categorize(CategorizationInput input,
                                               OwnerProfile owner,
                                               IReadOnlyList<UserTagRule> userRules)
        {
            List<string> text = Text(input);
            string[] code = TextTokens.Tokenize(input.BankTransactionCode);

            List<string> textAndCode = new(text) { FieldSeparator };
            textAndCode.AddRange(code);

            long? userTagId = null;
            int userMatchLength = 0;

            foreach (UserTagRule rule in userRules)
            {
                foreach (KeywordPattern pattern in rule.Patterns)
                {
                    if (pattern.Length <= userMatchLength || !pattern.Matches(textAndCode)) continue;

                    userTagId = rule.TagId;
                    userMatchLength = pattern.Length;
                }
            }

            if (userTagId != null) return new CategoryMatch(userTagId, null);

            return new CategoryMatch(null, CategorizeBuiltIn(input, owner, text, code, textAndCode));
        }

        public static bool IsOwnTransfer(CategorizationInput input,
                                         OwnerProfile owner)
        {
            return IsOwnTransfer(input, owner, Text(input), TextTokens.Tokenize(input.BankTransactionCode));
        }

        private static List<string> Text(CategorizationInput input)
        {
            List<string> text = new();

            foreach (string? field in new[] { input.CounterpartyName }.Concat(input.RemittanceInformation))
            {
                text.Add(FieldSeparator);
                text.AddRange(TextTokens.Tokenize(field));
            }

            return text;
        }

        private static bool IsOwnTransfer(CategorizationInput input,
                                          OwnerProfile owner,
                                          IReadOnlyList<string> text,
                                          IReadOnlyList<string> code)
        {
            return CategoryRules.OwnTransferCodes.Any(pattern => pattern.Matches(code)) ||
                   owner.IsOwnAccount(input.CounterpartyAccount) ||
                   owner.IsOwnName(text);
        }

        private static string CategorizeBuiltIn(CategorizationInput input,
                                                OwnerProfile owner,
                                                IReadOnlyList<string> text,
                                                IReadOnlyList<string> code,
                                                IReadOnlyList<string> textAndCode)
        {
            if (IsOwnTransfer(input, owner, text, code)) return CategoryRules.Transfers;

            if (input.CreditDebitIndicator == "CRDT" &&
                CategoryRules.IncomeKeywords.Any(pattern => pattern.Matches(textAndCode)))
                return CategoryRules.Income;

            string? best = null;
            int bestLength = 0;

            foreach ((string key, KeywordPattern[] patterns) in CategoryRules.Keywords)
            {
                foreach (KeywordPattern pattern in patterns)
                {
                    if (pattern.Length <= bestLength || !pattern.Matches(textAndCode)) continue;

                    best = key;
                    bestLength = pattern.Length;
                }
            }

            if (best != null) return best;

            return input.OriginalCurrency != null ? CategoryRules.Travel : CategoryRules.Other;
        }
    }
}
