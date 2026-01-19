using ZenithFin.Utility;
using static ZenithFin.EnableBanking.Response;

namespace ZenithFin.EnableBanking
{
    public static class Request
    {
        internal sealed record Authenticate(EnableBankingDtos.Access access,
                                            EnableBankingDtos.Aspsp aspsp,
                                            string state,
                                            string redirectUrl,
                                            string psuType);

        internal sealed record Sessions(string code);
    }
}
