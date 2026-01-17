using Org.BouncyCastle.Asn1.Ocsp;
using System.Net;
using System.Text.Json;
using ZenithFin.EnableBanking;

internal sealed class RequestBuilder
{
    private readonly Client _client;

    private readonly HttpMethod _method;
    private readonly Routing.Route _route;

    private object? _body;
    private Dictionary<string, string>? _query;
    private Dictionary<string, string>? _headers;

    public RequestBuilder WithBody(object body)
    {
        _body = body;
        return this;
    }

    public RequestBuilder WithQuery(string key, string value)
    {
        _query ??= new();
        _query[key] = value;
        return this;
    }

    public RequestBuilder WithHeader(string key, string value)
    {
        _headers ??= new();
        _headers[key] = value;
        return this;
    }

    public RequestBuilder WithBearer(string token)
    {
        return WithHeader("Authorization", $"Bearer {token}");
    }

    public async Task<dynamic> SendAsync()
    {
        HttpResponseMessage response = await _client.SendAsync(_method, _route, _body, _query, _headers);
        string json = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize(json, _route.response)!;
    }
    public async Task<HttpResponseMessage> SendAsyncRaw()
    {
        return await _client.SendAsync(_method, _route, _body, _query, _headers);
    }

    public RequestBuilder(Client client, HttpMethod method, Routing.Route route)
    {
        _client = client;

        _method = method;
        _route = route;
    }
}