using PathVeer.Core.Prefixes;

namespace PathVeer.Core.Ipc;

public sealed class PrefixUpdateCheckCommandHandler
{
    private readonly IPrefixUpdateMonitor _monitor;

    public PrefixUpdateCheckCommandHandler(
        IPrefixUpdateMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        _monitor = monitor;
    }

    public async Task<ServiceResponse> CheckNowAsync(
        CancellationToken cancellationToken = default)
    {
        await _monitor.ForceCheckAsync(cancellationToken);

        PrefixUpdateMonitorSnapshot snapshot =
            _monitor.GetSnapshot();

        PrefixUpdateCheckStatus? status =
            snapshot.CurrentResult?.Status;

        return new ServiceResponse
        {
            Success = true,
            Message = status is null
                ? "Prefix update check completed."
                : $"Prefix update check completed: {status}.",
            PrefixUpdateMonitor = snapshot
        };
    }
}
