using System.IO.Pipes;

namespace PathVeer.Core.Ipc;

/// <summary>
/// Client-side pipe connection factory with staged-upgrade fallback.
///
/// A PathVeer client prefers the primary <see cref="PathVeerPipeNames.PrimaryPipeName"/>
/// pipe, but if that pipe cannot be opened (service not yet upgraded, or the
/// primary pipe is unavailable) it falls back to the legacy
/// <see cref="PathVeerPipeNames.LegacyPipeName"/> pipe.
///
/// Fallback is performed ONLY at connect time. A failed connect means no bytes
/// were sent, so reconnecting on the legacy pipe is unambiguous and safe — even
/// for mutation commands (exactly-once: the command never reached any service).
/// If a connection is established but the command send or response fails, this
/// factory does NOT replay on the other pipe; that ambiguous outcome must not
/// cause a command to execute twice. The caller (see <see cref="PathVeerServiceClient"/>)
/// owns that boundary.
/// </summary>
public sealed class PathVeerNamedPipeClientFactory : INamedPipeClientFactory
{
    public async Task<INamedPipeClientConnection> ConnectAsync(
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        foreach (string pipeName in PathVeerPipeNames.AllListenNames)
        {
            try
            {
                NamedPipeClientStream pipe = new(
                    ".",
                    pipeName,
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
            catch (Exception ex)
                when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            "PathVeer Service is unavailable: neither the primary nor the " +
            "legacy control pipe could be opened.",
            lastError);
    }
}
