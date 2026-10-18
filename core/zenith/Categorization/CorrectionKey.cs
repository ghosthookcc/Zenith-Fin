using ZenithFin.Utility;

namespace ZenithFin.Categorization
{
    public readonly record struct CorrectionKey(string CreditDebitIndicator,
                                                bool OwnTransfer,
                                                string Signature)
    {
        private const int MinimumNumberLength = 5;

        public static CorrectionKey? From(CategorizationInput input,
                                          OwnerProfile owner)
        {
            string? signature = SignatureOf(TransactionText.Describe(input.CounterpartyName, input.RemittanceInformation));

            if (signature == null) return null;

            return new CorrectionKey(input.CreditDebitIndicator, RuleCategorizer.IsOwnTransfer(input, owner), signature);
        }

        public static string? SignatureOf(string? description)
        {
            string[] tokens = TextTokens.Tokenize(description);
            string[] kept = tokens.Where(token => token.Length >= MinimumNumberLength || !token.All(char.IsDigit)).ToArray();

            if (kept.Length == 0) kept = tokens;

            return kept.Length == 0 ? null : string.Join(' ', kept);
        }
    }
}
