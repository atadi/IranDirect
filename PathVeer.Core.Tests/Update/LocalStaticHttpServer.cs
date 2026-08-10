namespace PathVeer.Core.Tests.Update;

using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// Minimal dependency-free static HTTP/1.1 file server for distribution tests.
/// Uses TcpListener on 127.0.0.1 so it does NOT require an HttpListener URL ACL
/// (which needs admin / netsh reservation on Windows). Serves files from a root
/// directory with correct content types and a real 404 for missing paths. This
/// models the static release-distribution surface (CDN/object-storage origin)
/// without any external infrastructure.
/// </summary>
public sealed class LocalStaticHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    public int Port { get; }

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".json"] = "application/json",
        [".exe"] = "application/octet-stream",
        [".zip"] = "application/octet-stream",
        [".html"] = "text/html; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
    };

    public LocalStaticHttpServer(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(RootDirectory);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _thread = new Thread(Loop) { IsBackground = true, Name = "LocalStaticHttpServer" };
        _thread.Start();
    }

    public string RootDirectory { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    private async void Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                // Await the accept directly. There is exactly one pending accept at a
                // time; on Dispose, _listener.Stop() makes this throw and the loop ends.
                client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            catch (Exception) { break; }
            _ = Task.Run(() => HandleClient(client));
        }
    }

    private void HandleClient(TcpClient client)
    {
        try
        {
            using var ns = client.GetStream();
            using var reader = new StreamReader(ns, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            var requestLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(requestLine))
                return;
            // Read headers until blank line.
            while (!string.IsNullOrWhiteSpace(reader.ReadLine())) { }

            string body = "Not Found";
            int code = 404;
            string contentType = "text/plain; charset=utf-8";
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

            if (requestLine.StartsWith("GET ", StringComparison.Ordinal))
            {
                var path = requestLine.Split(' ')[1];
                if (path.StartsWith("/", StringComparison.Ordinal))
                    path = path[1..];
                path = Uri.UnescapeDataString(path.Replace('\\', '/'));
                var full = Path.GetFullPath(Path.Combine(RootDirectory, path));
                // Prevent directory traversal outside the root.
                if (full.StartsWith(RootDirectory, StringComparison.Ordinal) && File.Exists(full))
                {
                    var ext = Path.GetExtension(full);
                    contentType = ContentTypes.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";
                    bodyBytes = File.ReadAllBytes(full);
                    // Strip a leading UTF-8 BOM so JSON parsers don't reject the body.
                    if (bodyBytes.Length >= 3 && bodyBytes[0] == 0xEF && bodyBytes[1] == 0xBB && bodyBytes[2] == 0xBF)
                        bodyBytes = bodyBytes[3..];
                    code = 200;
                }
            }

            var header = new StringBuilder();
            header.Append($"HTTP/1.1 {code} {(code == 200 ? "OK" : "Not Found")}\r\n");
            header.Append($"Content-Type: {contentType}\r\n");
            header.Append($"Content-Length: {bodyBytes.Length}\r\n");
            header.Append("Connection: close\r\n");
            header.Append("\r\n");
            var headerBytes = Encoding.UTF8.GetBytes(header.ToString());
            ns.Write(headerBytes, 0, headerBytes.Length);
            ns.Write(bodyBytes, 0, bodyBytes.Length);
            ns.Flush();
        }
        catch
        {
            // Ignore client errors; server stays up.
        }
        finally
        {
            try { client.Close(); } catch { }
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { if (_thread.IsAlive) _thread.Join(500); } catch { }
        try { _cts.Dispose(); } catch { }
    }
}
