using System.IO.Pipes;
using System.Text;

namespace PathVeer.Core.Ipc;

public sealed class NamedPipeClientConnection :
    INamedPipeClientConnection
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    public NamedPipeClientConnection(
        NamedPipeClientStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        _pipe = pipe;
        _reader = new StreamReader(
            pipe,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        _writer = new StreamWriter(
            pipe,
            new UTF8Encoding(false),
            bufferSize: 4096,
            leaveOpen: true)
        {
            AutoFlush = true
        };
    }

    public Task WriteRequestLineAsync(
        string requestJson,
        CancellationToken cancellationToken) =>
        _writer.WriteLineAsync(
            requestJson.AsMemory(),
            cancellationToken);

    public ValueTask<string?> ReadResponseLineAsync(
        CancellationToken cancellationToken) =>
        _reader.ReadLineAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        _writer.Dispose();
        await _pipe.DisposeAsync();
    }
}
