using System.Text.Json;

namespace ZenithFin.EnableBanking
{
    internal static class Wrapper
    {
        public static class POST
        {
            public static RequestBuilder Authentication(Client client)
            {
                return new (client, HttpMethod.Post, Routing.Authentication.Authenticate);
            }

            public static RequestBuilder Sessions(Client client)
            {
                return new(client, HttpMethod.Post, Routing.Authentication.Sessions);
            }
        }
        public static class GET
        {
            public static RequestBuilder Application(Client client)
            {
                return new (client, HttpMethod.Get, Routing.Application.Root);
            }
        }
    }
}
