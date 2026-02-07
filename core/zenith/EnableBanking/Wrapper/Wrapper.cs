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
                return new (client, HttpMethod.Post, Routing.Authentication.Sessions);
            }

            public static RequestBuilder AuthorizedAspsps(Client client,
                                                          string identifier)
            {
                return new(client, HttpMethod.Post, Routing.Authentication.AuthorizedAspspsByIdentifer(identifier));
            }
        }
        public static class GET
        {
            public static RequestBuilder Application(Client client)
            {
                return new (client, HttpMethod.Get, Routing.Application.Root);
            }

            public static RequestBuilder AccountsBalancesById(Client client, 
                                                              Guid id,
                                                              string ip = "127.0.0.1",
                                                              string agent = "ZenithFin/1.0")
            {
                RequestBuilder request = new (client, HttpMethod.Get, Routing.AccountsBalances.ByIdentifier(id));
                request.WithHeader("Psu-Ip-Address", ip);
                request.WithHeader("Psu-User-Agent", agent);
                return request;
            }

            public static RequestBuilder Aspsps(Client client)
            {
                return new (client, HttpMethod.Get, Routing.Authentication.Aspsps);
            }
        }
    }
}
