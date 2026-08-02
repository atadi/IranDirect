namespace IranDirect.Core.Observability;

using IranDirect.Core.Configuration;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Vpn;

public sealed class RuntimeSnapshotProvider : IRuntimeSnapshotProvider
{
    private readonly IranDirectController _controller;
    private readonly DesiredConfigurationService _configurationService;
    private readonly IRouteInventoryPersistence _routeInventory;
    private readonly CustomRouteDnsCacheService _dnsCacheService;
    private readonly RuntimePerfReportStore? _perfStore;
    private readonly TimeProvider _timeProvider;

    public RuntimeSnapshotProvider(
        IranDirectController controller,
        DesiredConfigurationService configurationService,
        IRouteInventoryPersistence routeInventory,
        CustomRouteDnsCacheService dnsCacheService,
        RuntimePerfReportStore? perfStore = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(configurationService);
        ArgumentNullException.ThrowIfNull(routeInventory);
        ArgumentNullException.ThrowIfNull(dnsCacheService);

        _controller = controller;
        _configurationService = configurationService;
        _routeInventory = routeInventory;
        _dnsCacheService = dnsCacheService;
        _perfStore = perfStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<RuntimeSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset capturedAt =
            _timeProvider.GetUtcNow();

        IranDirectStatus runtime =
            await _controller.GetStatusAsync(
                cancellationToken);

        DesiredConfiguration configuration =
            await _configurationService.GetAsync(
                cancellationToken);

        RouteInventory inventory =
            await _routeInventory.LoadAsync(
                cancellationToken);

        IReadOnlyList<CustomRouteDnsCacheStatus> dnsCache =
            await _dnsCacheService.GetStatusAsync(
                cancellationToken);

        RuntimeCyclePerfReport? performance =
            _perfStore is null
                ? null
                : await _perfStore.ReadLatestAsync(
                    cancellationToken);

        return new RuntimeSnapshot
        {
            CapturedAt = capturedAt,
            Configuration = configuration,
            Runtime = runtime,
            Operation = runtime.Operation,
            PrefixCount = runtime.PrefixCount,
            InstalledRouteCount =
                runtime.InstalledRouteCount,
            RouteInventoryCount =
                inventory.Routes.Count,
            VpnEndpointHealth =
                new VpnEndpointProtectionHealth
                {
                    CurrentEndpointCount =
                        runtime.VpnEndpointCount,
                    ProtectedEndpointCount =
                        runtime.ProtectedVpnEndpointCount
                },
            DnsCache = dnsCache,
            Performance = performance,
            LastError = runtime.LastError
        };
    }
}
