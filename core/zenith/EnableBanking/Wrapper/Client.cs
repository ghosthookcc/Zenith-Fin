using System.Net.Http.Json;

namespace ZenithFin.EnableBanking
{
    internal sealed class Client
    {
        public HttpClient Http { get; private set; }
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method,
                                                         Routing.Route route,
                                                         object? body = null,
                                                         Dictionary<string, string>? query = null,
                                                         Dictionary<string, string>? headers = null)
        {
            string uri = route.endpoint;

            if (query?.Any() == true)
            {
                uri += "?" + string.Join("&", query.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            }

            HttpRequestMessage request = new (method, uri);

            if (body != null)
            {
                request.Content = JsonContent.Create(body);
            }

            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return await Http.SendAsync(request);
        }

        public Client()
        {
            Http = new HttpClient
            {
                BaseAddress = new Uri("https://api.enablebanking.com")
            };
        }
    }
}
