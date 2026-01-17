namespace ZenithFin.EnableBanking
{
    public static class Response
    {
        internal record Base();
        internal sealed record Application(string name,
                                           string description,
                                           string kid,
                                           string environment,
                                           List<string> redirect_urls,
                                           bool active,
                                           List<string> countries,
                                           List<string> services) : Base;
        internal sealed record Authenticate(string url,
                                            string authenticationId,
                                            string psuIdHash) : Base;
    }

}
