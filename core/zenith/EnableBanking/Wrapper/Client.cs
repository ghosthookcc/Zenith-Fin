using System.Net.Http.Headers;
using System.Text.Json;

namespace ZenithFin.EnableBanking
{
    internal sealed class Client
    {
        public HttpClient Http { get; private set; }
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method,
                                                         Routing.Route route,
                                                         object? body = null,
                                                         Dictionary<string, string>? query = null,
                                                         Dictionary<string, string>? headers = null,
                                                         JsonSerializerOptions? options = null)
        {
            string uri = route.endpoint;

            if (query?.Any() == true)
            {
                uri += "?" + string.Join("&", query.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            }

            HttpRequestMessage request = new (method, uri);

            if (body != null)
            {
                options = options ?? new JsonSerializerOptions();
                request.Content = JsonContent.Create(body, 
                                                     mediaType: new MediaTypeHeaderValue("application/json"),
                                                     options: options);
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
