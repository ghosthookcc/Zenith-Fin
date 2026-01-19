namespace ZenithFin.EnableBanking
{
    public static class Response
    {
        internal sealed record Application(string name,
                                           string? description,
                                           string kid,
                                           string environment,
                                           IReadOnlyList<string> redirectUrls,
                                           bool active,
                                           IReadOnlyList<string> countries,
                                           IReadOnlyList<string> services) : EnableBankingDtos.Base;
        internal sealed record Authenticate(string url,
                                            string authenticationId,
                                            string psuIdHash) : EnableBankingDtos.Base;
        internal sealed record Sessions(string sessionId,
                                        IReadOnlyList<EnableBankingDtos.AccountData> accounts,
                                        EnableBankingDtos.Aspsp aspsp,
                                        string psuType,
                                        EnableBankingDtos.Access access) : EnableBankingDtos.Base;
        internal sealed record AccountsBalances(IReadOnlyList<EnableBankingDtos.Balance> balances) : EnableBankingDtos.Base;
    }

}
