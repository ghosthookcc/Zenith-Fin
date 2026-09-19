namespace ZenithFin.EnableBanking
{
    internal static class Routing
    {
        internal record Route (string endpoint, Type response);

        public class Application
        {
            public readonly static Route Root = new ("/application", typeof(Response.Application));
        }

        public class Authentication
        {
            public readonly static Route Authenticate = new ("/auth", typeof(Response.Authenticate));
            public readonly static Route Sessions = new ("/sessions", typeof(Response.Sessions));
            public static Route SessionByIdentifier(Guid id) => new($"/sessions/{id}", typeof(Response.SessionData));
            public readonly static Route Aspsps = new ("/aspsps", typeof(Response.Aspsps));
            public static Route AuthorizedAspspsByIdentifer(string identifier) => new($"/applications/{identifier}/aspsps", typeof(Response.AuthorizedAspsps));
        }

        public class Account
        {
            public static Route DetailsByIdentifier(Guid id) => new($"/accounts/{id}/details", typeof(EnableBankingEntities.AccountData));
            public static Route BalancesByIdentifier(Guid id) => new ($"/accounts/{id.ToString()}/balances", typeof(Response.AccountsBalances));
        }
    }
}
