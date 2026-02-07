using static ZenithFin.EnableBanking.EnableBankingEntities;

namespace ZenithFin.EnableBanking
{
    public static class Request
    {
        internal sealed record Authenticate(Access access,
                                            Aspsp aspsp,
                                            string state,
                                            string redirectUrl,
                                            string psuType);

        internal sealed record Sessions(string code);
    }
}
