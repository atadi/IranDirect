using System.IO.Pipes;

namespace PathVeer.Core.Ipc;

public sealed class NamedPipeClientFactory : INamedPipeClientFactory
{
    public async Task<INamedPipeClientConnection> ConnectAsync(
        CancellationToken cancellationToken)
    {
        NamedPipeClientStream pipe = new(
            ".",
            IranDirectPipeNames.Control,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(cancellationToken);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }

        return new NamedPipeClientConnection(pipe);
    }
}
