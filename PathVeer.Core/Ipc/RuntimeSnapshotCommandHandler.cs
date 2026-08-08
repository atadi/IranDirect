using PathVeer.Core.Observability;

namespace PathVeer.Core.Ipc;

public sealed class RuntimeSnapshotCommandHandler
{
    private readonly IRuntimeSnapshotProvider _provider;

    public RuntimeSnapshotCommandHandler(
        IRuntimeSnapshotProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _provider = provider;
    }

    public async Task<ServiceResponse> GetAsync(
        CancellationToken cancellationToken = default)
    {
        RuntimeSnapshot snapshot =
            await _provider.GetSnapshotAsync(
                cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message = "Runtime snapshot captured.",
            Snapshot = snapshot
        };
    }
}
