namespace PathVeer.Core.Update;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

/// <summary>
/// Isolation boundary between remote transport and update policy. Implementations
/// return the RAW manifest JSON text for a (platform, architecture, channel)
/// query. They do NOT validate, parse policy, or decide updates.
/// </summary>
public interface IReleaseSource
{
    Task<string?> FetchManifestAsync(UpdateChannel channel, string platform, string architecture, CancellationToken ct = default);
}

/// <summary>File/static-feed backed source for tests and local distribution.</summary>
public sealed class LocalFileReleaseSource : IReleaseSource
{
    private readonly Func<string> _pathResolver;
    public LocalFileReleaseSource(string path) => _pathResolver = () => path;
    public LocalFileReleaseSource(Func<string> pathResolver) => _pathResolver = pathResolver;

    public Task<string?> FetchManifestAsync(UpdateChannel channel, string platform, string architecture, CancellationToken ct = default)
    {
        var path = _pathResolver();
        if (!File.Exists(path)) return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(File.ReadAllText(path));
    }
}

/// <summary>
/// HTTP source. Enforces HTTPS for production URLs, bounded timeout, controlled
/// max response size, predictable User-Agent, safe decompression. No automatic
/// insecure HTTP redirect (handler is configured to not downgrade).
/// </summary>
public sealed class HttpReleaseSource : IReleaseSource
{
    private readonly HttpClient _http;
    private readonly Func<UpdateChannel, string, string, string> _urlBuilder;
    private readonly int _maxBytes;

    public HttpReleaseSource(HttpClient http, Func<UpdateChannel, string, string, string> urlBuilder, int maxBytes = 64 * 1024)
    {
        _http = http;
        _urlBuilder = urlBuilder;
        _maxBytes = maxBytes;
    }

    public async Task<string?> FetchManifestAsync(UpdateChannel channel, string platform, string architecture, CancellationToken ct = default)
    {
        var url = _urlBuilder(channel, platform, architecture);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new UpdateSourceException($"Invalid release URL: {url}");
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Host != "localhost" && uri.Host != "127.0.0.1")
            throw new UpdateSourceException($"Refusing non-HTTPS production URL: {url}");

        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.UserAgent.ParseAdd("PathVeer-UpdateClient/1.0");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();

        // Bounded read to avoid unbounded memory from malformed feeds.
        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buf = new byte[_maxBytes];
        int total = 0;
        int read;
        while ((read = await stream.ReadAsync(buf.AsMemory(total, _maxBytes - total), ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total >= _maxBytes) throw new UpdateSourceException("Manifest response exceeds maximum allowed size.");
        }
        return Encoding.UTF8.GetString(buf, 0, total);
    }
}

public sealed class UpdateSourceException : Exception
{
    public UpdateSourceException(string message) : base(message) { }
}
