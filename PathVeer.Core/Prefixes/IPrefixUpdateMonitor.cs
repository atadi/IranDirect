namespace PathVeer.Core.Prefixes;

public interface IPrefixUpdateMonitor
{
    Task StartAsync(
        CancellationToken cancellationToken = default);

    Task StopAsync(
        CancellationToken cancellationToken = default);

    Task ForceCheckAsync(
        CancellationToken cancellationToken = default);

    PrefixUpdateMonitorSnapshot GetSnapshot();
}
