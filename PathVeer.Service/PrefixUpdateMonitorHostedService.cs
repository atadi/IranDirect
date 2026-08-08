using PathVeer.Core.Prefixes;
using Microsoft.Extensions.Hosting;

namespace PathVeer.Service;

public sealed class PrefixUpdateMonitorHostedService :
    IHostedService
{
    private readonly IPrefixUpdateMonitor _monitor;

    public PrefixUpdateMonitorHostedService(
        IPrefixUpdateMonitor monitor)
    {
        _monitor = monitor;
    }

    public Task StartAsync(
        CancellationToken cancellationToken) =>
        _monitor.StartAsync(cancellationToken);

    public Task StopAsync(
        CancellationToken cancellationToken) =>
        _monitor.StopAsync(cancellationToken);
}
