namespace PathVeer.Core.Observability;

using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Routing;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.Testing.FaultInjection;
using PathVeer.Core.Vpn;

public sealed class RuntimeSnapshotProvider : IRuntimeSnapshotProvider
{
    private readonly IranDirectController _controller;
    private readonly DesiredConfigurationService _configurationService;
    private readonly IRouteInventoryPersistence _routeInventory;
    private readonly CustomRouteDnsCacheService _dnsCacheService;
    private readonly RuntimePerfReportStore? _perfStore;
    private readonly IPrefixSourceMetadataService?
        _prefixSourceMetadataService;
    private readonly IPrefixUpdateChecker?
        _prefixUpdateChecker;
    private readonly IPrefixUpdateMonitor?
        _prefixUpdateMonitor;
    private readonly TimeProvider _timeProvider;
    private readonly IFaultInjectionPolicy _faultPolicy;

    public RuntimeSnapshotProvider(
        IranDirectController controller,
        DesiredConfigurationService configurationService,
        IRouteInventoryPersistence routeInventory,
        CustomRouteDnsCacheService dnsCacheService,
        RuntimePerfReportStore? perfStore = null,
        IPrefixSourceMetadataService?
            prefixSourceMetadataService = null,
        IPrefixUpdateChecker? prefixUpdateChecker = null,
        TimeProvider? timeProvider = null,
        IPrefixUpdateMonitor? prefixUpdateMonitor = null,
        IFaultInjectionPolicy? faultPolicy = null)
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
        _prefixSourceMetadataService =
            prefixSourceMetadataService;
        _prefixUpdateChecker = prefixUpdateChecker;
        _prefixUpdateMonitor = prefixUpdateMonitor;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
    }

    public async Task<RuntimeSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ShouldFailAt(FaultInjectionPoint.SnapshotCapture))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.SnapshotCapture);
        }

        DateTimeOffset capturedAt =
            _timeProvider.GetUtcNow();

        IranDirectStatus runtime =
            await _controller.GetStatusAsync(
                cancellationToken);

        DesiredConfiguration? configuration;
        try
        {
            configuration =
                await _configurationService.GetAsync(
                    cancellationToken);
        }
        catch (DesiredConfigurationException)
        {
            // A missing/corrupt configuration is surfaced truthfully as null
            // rather than a fabricated disabled configuration.
            configuration = null;
        }

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

        PrefixSourceMetadata? prefixSource =
            _prefixSourceMetadataService is null
                ? null
                : await _prefixSourceMetadataService
                    .GetCurrentAsync(
                        configuration?.DirectCountryCode
                            ?? DirectCountryCode.IR,
                        cancellationToken);

        // When the monitor is registered, snapshot reads are
        // served from its in-memory state only: no remote probe
        // is performed during snapshot generation.
        PrefixUpdateMonitorSnapshot? prefixUpdateMonitor =
            _prefixUpdateMonitor?.GetSnapshot();

        PrefixUpdateCheckResult? prefixUpdate =
            prefixUpdateMonitor is not null
                ? prefixUpdateMonitor.CurrentResult
                : _prefixUpdateChecker is null
                    ? null
                    : await _prefixUpdateChecker.CheckAsync(
                        cancellationToken);

        return new RuntimeSnapshot
        {
            CapturedAt = capturedAt,
            Configuration = configuration,
            Runtime = runtime,
            Operation = runtime.Operation,
            PrefixSource = prefixSource,
            PrefixUpdate = prefixUpdate,
            PrefixUpdateMonitor = prefixUpdateMonitor,
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

    private bool ShouldFailAt(FaultInjectionPoint point) =>
        FaultInjectionResolver.ShouldFail(_faultPolicy, point);
}
