namespace ZenithFin.Categorization
{
    public sealed record CategorizationInput(string? CounterpartyName,
                                             IReadOnlyList<string> RemittanceInformation,
                                             string? BankTransactionCode,
                                             string? CounterpartyAccount,
                                             string CreditDebitIndicator,
                                             string? OriginalCurrency);

    public static class RuleCategorizer
    {
        private const string FieldSeparator = "|";

        public static string Categorize(CategorizationInput input,
                                        OwnerProfile owner)
        {
            List<string> text = new();

            foreach (string? field in new[] { input.CounterpartyName }.Concat(input.RemittanceInformation))
            {
                text.Add(FieldSeparator);
                text.AddRange(TextTokens.Tokenize(field));
            }

            string[] code = TextTokens.Tokenize(input.BankTransactionCode);

            if (CategoryRules.OwnTransferCodes.Any(pattern => pattern.Matches(code)) ||
                owner.IsOwnAccount(input.CounterpartyAccount) ||
                owner.IsOwnName(text))
                return CategoryRules.Transfers;

            List<string> textAndCode = new(text) { FieldSeparator };
            textAndCode.AddRange(code);

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
