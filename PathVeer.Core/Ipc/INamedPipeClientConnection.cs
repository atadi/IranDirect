namespace PathVeer.Core.Ipc;

public interface INamedPipeClientConnection : IAsyncDisposable
{
    Task WriteRequestLineAsync(
        string requestJson,
        CancellationToken cancellationToken);

    ValueTask<string?> ReadResponseLineAsync(
        CancellationToken cancellationToken);
}
