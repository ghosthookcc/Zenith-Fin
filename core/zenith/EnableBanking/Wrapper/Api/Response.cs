using static ZenithFin.EnableBanking.EnableBankingEntities;

namespace ZenithFin.EnableBanking
{
    public static class Response
    {
        public sealed record Sessions(string sessionId,
            IReadOnlyList<AccountData> accounts,
            Aspsp aspsp,
            string psuType,
            Access access) : Base;
        
        public sealed record AccountsBalances(IReadOnlyList<Balance> balances) : Base;
        
        public sealed record SessionData(string status, 
                                         IReadOnlyList<Guid> accounts,
                                         IReadOnlyList<SessionAccount> accountsData,
                                         Aspsp aspsp,
                                         string psuType,
                                         string psuIdHash,
                                         Access access,
                                         DateTimeOffset created,
                                         DateTimeOffset? authorized,
                                         DateTimeOffset? closed) : Base;

        public sealed record SessionAccount(Guid uid, 
                                            string identificationHash,
                                            IReadOnlyList<string> identificationHashes) : Base;

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
        
        internal sealed record Aspsps(IReadOnlyList<AspspDetailed> aspsps) : Base;

        internal sealed record AuthorizedAspsps() : Base;
    }
}