namespace PathVeer.Core.Update;

using System.IO;
using System.Net.Http;

/// <summary>
/// Bounded, streaming download of a release installer into a staging
/// <c>.partial</c> file. Designed so the ~130 MB installer is never fully
/// buffered in memory and a cancelled/failed download leaves only an
/// incomplete <c>.partial</c> that is never promoted to an executable path.
///
/// This type performs TRANSPORT and SIZE sanity only. Content trust
/// (hash + Authenticode + publisher) is the responsibility of
/// <see cref="InstallerDownloadVerifier"/> and the
/// <see cref="UpdateApplyCoordinator"/> that owns the full pipeline.
/// </summary>
public sealed class UpdateDownloader
{
    // ~400 MB ceiling: well above any realistic PathVeer installer while
    // protecting against a hostile/buggy feed reporting an absurd size.
    public const long MaxDownloadBytes = 400L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly long _maxBytes;

    public UpdateDownloader(HttpClient http, long maxBytes = MaxDownloadBytes)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _maxBytes = maxBytes <= 0 ? MaxDownloadBytes : maxBytes;
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="partialPath"/> using a
    /// bounded streaming copy. Enforces HTTPS (except localhost for tests) and the
    /// configured byte ceiling. Throws <see cref="UpdateDownloadException"/> on
    /// transport/size failure; the caller is responsible for cleanup of the
    /// <c>.partial</c> file.
    /// </summary>
    public async Task DownloadAsync(
        string url,
        string partialPath,
        long expectedSize,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("url required", nameof(url));
        if (string.IsNullOrWhiteSpace(partialPath)) throw new ArgumentException("partialPath required", nameof(partialPath));

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new UpdateDownloadException($"Invalid download URL: {url}");
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Host != "localhost" && uri.Host != "127.0.0.1")
            throw new UpdateDownloadException($"Refusing non-HTTPS download URL: {url}");

        // Reject absurd advertised sizes before any network work.
        if (expectedSize > _maxBytes)
            throw new UpdateDownloadException(
                $"Advertised installer size {expectedSize} exceeds the maximum allowed {_maxBytes}.");

        var dir = Path.GetDirectoryName(partialPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.UserAgent.ParseAdd("PathVeer-UpdateClient/1.0");

        using var resp = await _http
            .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new UpdateDownloadException($"Installer not found at {url} (404).");
        if (!resp.IsSuccessStatusCode)
            throw new UpdateDownloadException($"Download failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");

        // Honor a server Content-Length above the ceiling even if the manifest
        // size was absent/zero.
        var contentLength = resp.Content.Headers.ContentLength;
        if (contentLength is { } cl && cl > _maxBytes)
            throw new UpdateDownloadException(
                $"Server reports size {cl} exceeding the maximum allowed {_maxBytes}.");

        var written = 0L;
        await using (var outStream = new FileStream(
            partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
        await using (var inStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await inStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                written += read;
                if (written > _maxBytes)
                {
                    await outStream.DisposeAsync().ConfigureAwait(false);
                    throw new UpdateDownloadException(
                        $"Download exceeded the maximum allowed {_maxBytes} bytes.");
                }
                await outStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                progress?.Report(written);
            }
        }

        if (expectedSize > 0 && written != expectedSize)
        {
            // The coordinator re-verifies via hash; this is an early, cheap guard.
            throw new UpdateDownloadException(
                $"Downloaded size {written} does not match expected {expectedSize}.");
        }
    }
}

public sealed class UpdateDownloadException : Exception
{
    public UpdateDownloadException(string message) : base(message) { }
}
