using System.Net;
using System.Text;

namespace YoutubeCulvert.Testing;

/// <summary>A request captured by <see cref="FakeHttpHandler"/>.</summary>
public sealed record RecordedRequest(string Method, string Url, string? Body);

/// <summary>
/// Records requests and answers them from a route table keyed by a URL
/// substring. Unmatched requests get HTTP 404 so tests fail loudly.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<Route> _routes = [];
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
                return _requests.ToArray();
        }
    }

    public FakeHttpHandler Map(
        string urlContains,
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string contentType = "application/json") =>
        Map(urlContains, _ => Json(json, statusCode, contentType));

    public FakeHttpHandler Map(
        string urlContains,
        Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urlContains);
        ArgumentNullException.ThrowIfNull(factory);

        lock (_gate)
            _routes.Add(new Route(urlContains, factory));

        return this;
    }

    public FakeHttpHandler MapFile(string urlContains, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Map(urlContains, File.ReadAllText(filePath));
    }

    public int Count(string urlContains)
    {
        lock (_gate)
            return _requests.Count(request =>
                request.Url.Contains(urlContains, StringComparison.OrdinalIgnoreCase));
    }

    public RecordedRequest? LastRequest(string urlContains)
    {
        lock (_gate)
        {
            return _requests.LastOrDefault(request =>
                request.Url.Contains(urlContains, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static HttpResponseMessage Json(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string contentType = "application/json") =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Route? route;

        lock (_gate)
        {
            _requests.Add(new RecordedRequest(request.Method.Method, url, body));
            route = _routes.FirstOrDefault(candidate =>
                url.Contains(candidate.UrlContains, StringComparison.OrdinalIgnoreCase));
        }

        if (route is null)
        {
            return Json(
                $"{{\"error\":\"No fake route matches {url.Replace("\\", "\\\\")}\"}}",
                HttpStatusCode.NotFound);
        }

        return route.Factory(request);
    }

    private sealed record Route(
        string UrlContains,
        Func<HttpRequestMessage, HttpResponseMessage> Factory);
}
