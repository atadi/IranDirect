using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Models;
using PathVeer.Core.Networking;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Routing;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.Vpn;

namespace PathVeer.Core.Runtime;

public sealed class IranDirectRuntimeObservationSource :
    IRuntimeObservationSource
{
    private readonly string _profilePath;
    private readonly OpenVpnEndpointProvider _vpnEndpointProvider;
    private readonly GatewayDetector _gatewayDetector;
    private readonly CountryPrefixStore _prefixStore;
    private readonly Func<DirectCountryCode> _countryResolver;
    private readonly IRouteManager _routeManager;
    private readonly RuntimeCycleProfiler _profiler;
    private readonly ICustomRouteResolver? _customRouteResolver;

    public IranDirectRuntimeObservationSource(
        string profilePath,
        OpenVpnEndpointProvider vpnEndpointProvider,
        GatewayDetector gatewayDetector,
        CountryPrefixStore prefixStore,
        Func<DirectCountryCode> countryResolver,
        IRouteManager routeManager,
        RuntimeCycleProfiler? profiler = null,
        ICustomRouteResolver? customRouteResolver = null)
    {
        _profilePath = profilePath;
        _vpnEndpointProvider = vpnEndpointProvider;
        _gatewayDetector = gatewayDetector;
        _prefixStore = prefixStore;
        _countryResolver = countryResolver;
        _routeManager = routeManager;
        _profiler = profiler ?? RuntimeCycleProfiler.Noop;
        _customRouteResolver = customRouteResolver;
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
            DirectCountryCode country = _countryResolver();
            IReadOnlyList<string> prefixes =
                await _prefixStore.LoadPrefixesAsync(
                    country,
                    cancellationToken);

            if (_customRouteResolver is null)
            {
                return prefixes;
            }

            CustomRouteResolutionResult customResult =
                await _customRouteResolver.ResolveAsync(
                    cancellationToken);

            if (customResult.Prefixes.Count == 0)
            {
                return prefixes;
            }

            return prefixes
                .Concat(customResult.Prefixes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
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