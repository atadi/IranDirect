namespace IranDirect.Core.Ipc;

public interface INamedPipeClientFactory
{
    Task<INamedPipeClientConnection> ConnectAsync(
        CancellationToken cancellationToken);
}
