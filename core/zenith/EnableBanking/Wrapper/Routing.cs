namespace ZenithFin.EnableBanking
{
    internal static class Routing
    {
        public static class Application
        {
            public const string Root = "/application";
            public static string ByIdentifier(string identifier) => $"/application/{identifier}";
        }

        public static class Authentication
        {
            public const string Authenticate = "/auth";
            public const string Sessions = "/sessions";
        }
    }
}
