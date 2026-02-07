using static ZenithFin.EnableBanking.EnableBankingEntities;

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
                                           IReadOnlyList<string> services) : Base;
        internal sealed record Authenticate(string url,
                                            string authenticationId,
                                            string psuIdHash) : Base;
        public sealed record Sessions(string sessionId,
                                        IReadOnlyList<AccountData> accounts,
                                        Aspsp aspsp,
                                        string psuType,
                                        Access access) : Base;
        internal sealed record AccountsBalances(IReadOnlyList<Balance> balances) : Base;

        internal sealed record Aspsps(IReadOnlyList<AspspDetailed> aspsps) : Base;

        internal sealed record AuthorizedAspsps() : Base;
    }

}
