using IranDirect.Core.Models;
using IranDirect.Core.Networking;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.Vpn;

namespace IranDirect.Core.Runtime;

public sealed class IranDirectRuntimeObservationSource :
    IRuntimeObservationSource
{
    private readonly string _profilePath;
    private readonly OpenVpnEndpointProvider _vpnEndpointProvider;
    private readonly GatewayDetector _gatewayDetector;
    private readonly PrefixFileRepository _prefixRepository;
    private readonly IRouteManager _routeManager;
    private readonly RuntimeCycleProfiler _profiler;

    public IranDirectRuntimeObservationSource(
        string profilePath,
        OpenVpnEndpointProvider vpnEndpointProvider,
        GatewayDetector gatewayDetector,
        PrefixFileRepository prefixRepository,
        IRouteManager routeManager,
        RuntimeCycleProfiler? profiler = null)
    {
        _profilePath = profilePath;
        _vpnEndpointProvider = vpnEndpointProvider;
        _gatewayDetector = gatewayDetector;
        _prefixRepository = prefixRepository;
        _routeManager = routeManager;
        _profiler = profiler ?? RuntimeCycleProfiler.Noop;
    }

    public bool VpnProfileExists =>
        File.Exists(_profilePath);

    public async Task<RuntimeObservationSourceResult<
        IReadOnlyList<ObservedVpnEndpoint>>>
        ObserveVpnEndpointsAsync(
            CancellationToken cancellationToken = default)
    {
        if (!VpnProfileExists)
        {
            return RuntimeObservationSourceResult<
                IReadOnlyList<ObservedVpnEndpoint>>.Failure(
                    "The configured VPN profile does not exist.");
        }

        try
        {
            IReadOnlyList<ResolvedVpnEndpoint> endpoints;

            using (_profiler.Measure(
                RuntimePerfCategory.ObservationVpnEndpointLoad))
            {
                endpoints =
                    await _vpnEndpointProvider.GetEndpointsAsync(
                        cancellationToken);
            }

            ObservedVpnEndpoint[] observed = endpoints
                .Select(endpoint =>
                    new ObservedVpnEndpoint
                    {
                        Host = endpoint.Host,
                        Address = endpoint.Address,
                        Port = endpoint.Port,
                        Protocol = endpoint.Protocol
                    })
                .ToArray();

            return RuntimeObservationSourceResult<
                IReadOnlyList<ObservedVpnEndpoint>>.Success(
                    observed);
        }
        catch (Exception exception)
        {
            return RuntimeObservationSourceResult<
                IReadOnlyList<ObservedVpnEndpoint>>.Failure(
                    exception.Message);
        }
    }

    public RuntimeObservationSourceResult<
        ObservedDirectGateway>
        ObserveDirectGateway()
    {
        try
        {
            DirectGateway gateway;

            using (_profiler.Measure(
                RuntimePerfCategory.ObservationGatewayDetection))
            {
                gateway = _gatewayDetector.Detect();
            }

            return RuntimeObservationSourceResult<
                ObservedDirectGateway>.Success(
                    new ObservedDirectGateway
                    {
                        Address =
                            gateway.Address.ToString(),
                        InterfaceIndex =
                            gateway.InterfaceIndex,
                        InterfaceName =
                            gateway.InterfaceName,
                        InterfaceMetric =
                            gateway.InterfaceMetric
                    });
        }
        catch (Exception exception)
        {
            return RuntimeObservationSourceResult<
                ObservedDirectGateway>.Failure(
                    exception.Message);
        }
    }

    public async Task<IReadOnlyList<string>>
        ObservePrefixesAsync(
            CancellationToken cancellationToken = default)
    {
        using (_profiler.Measure(
            RuntimePerfCategory.ObservationPrefixLoad))
        {
            return await _prefixRepository.LoadAsync(
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ObservedRoute>>
        ObserveRoutesAsync(
            CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SystemRoute> routes;

        using (_profiler.Measure(
            RuntimePerfCategory.ObservationRouteTableRead))
        {
            routes =
                await _routeManager.GetIpv4RoutesAsync(
                    cancellationToken);
        }

        return routes
            .Select(route =>
                new ObservedRoute
                {
                    DestinationPrefix =
                        route.DestinationPrefix,
                    NextHop =
                        route.NextHop.ToString(),
                    InterfaceIndex =
                        route.InterfaceIndex,
                    Metric =
                        route.RouteMetric
                })
            .ToArray();
    }
}