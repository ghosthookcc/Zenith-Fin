using ZenithFin.EnableBanking;

internal sealed class RequestBuilder
{
    private readonly Client _client;

    private readonly HttpMethod _method;
    private readonly string _route;

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

    public Task<HttpResponseMessage> SendAsync() => _client.SendAsync(_method, _route, _body, _query, _headers);

    public RequestBuilder(Client client, HttpMethod method, string route)
    {
        _client = client;
        _method = method;
        _route = route;
    }
}