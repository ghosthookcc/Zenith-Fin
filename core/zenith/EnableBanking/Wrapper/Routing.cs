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
        }

        public class AccountsBalances
        {
            public static Route ByIdentifier(Guid id) => new($"/accounts/{id.ToString()}/balances", typeof(Response.AccountsBalances));
        }
    }
}
