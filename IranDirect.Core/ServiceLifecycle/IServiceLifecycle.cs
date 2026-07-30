namespace IranDirect.Core.ServiceLifecycle;

using System.Runtime.Versioning;

/// <summary>
/// Manages the Windows Service lifecycle from outside the service
/// process. The service itself never controls its own SCM state.
/// </summary>
[SupportedOSPlatform("windows")]
public interface IServiceLifecycle
{
    ServiceStatusSnapshot GetStatus();

    Task InstallAsync(CancellationToken cancellationToken = default);

    Task UninstallAsync(CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task RestartAsync(CancellationToken cancellationToken = default);
}
