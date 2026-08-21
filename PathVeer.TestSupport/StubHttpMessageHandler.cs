using System.Net;
using System.Net.Http;
using System.Text;

namespace PathVeer.TestSupport.Http;

/// <summary>
/// Records the last request and returns a canned response. Used to assert the
/// exact wire contract (method, path, headers, body) without a live server.
/// Shared by Core and Service test projects.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    private readonly object _lock = new();
    private HttpRequestMessage? _lastRequest;
    private byte[]? _lastBody;

    public StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public HttpRequestMessage? LastRequest
    {
        get { lock (_lock) return _lastRequest; }
    }

    public string? LastRequestBody
    {
        get
        {
            lock (_lock)
            {
                return _lastBody is null
                    ? null
                    : Encoding.UTF8.GetString(_lastBody);
            }
        }
    }

    public string? LastAuthorizationHeader
    {
        get
        {
            HttpRequestMessage? req = LastRequest;
            return req?.Headers.Authorization?.ToString();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        byte[]? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(
                cancellationToken);
        }

        lock (_lock)
        {
            _lastRequest = request;
            _lastBody = body;
        }

        return _responder(request);
    }

    public static HttpResponseMessage Json(
        HttpStatusCode status,
        string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
    }

    public static HttpResponseMessage NoContent(HttpStatusCode status) =>
        new(status);
}
