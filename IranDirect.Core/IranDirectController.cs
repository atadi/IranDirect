using IranDirect.Core.Models;
using IranDirect.Core.Networking;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.State;
using IranDirect.Core.Vpn;
using System.Net;

namespace IranDirect.Core;

public sealed class IranDirectController
{
    private const int RouteMetric = 5;

    private readonly IranPrefixProvider _prefixProvider;
    private readonly PrefixFileRepository _prefixRepository;
    private readonly GatewayDetector _gatewayDetector;
    private readonly RouteReconciler _routeReconciler;
    private readonly StateRepository _stateRepository;
    private readonly RouteInventoryStore _routeInventoryStore;
    private readonly OpenVpnEndpointProvider _vpnEndpointProvider;
    private readonly VpnEndpointRouteManager _vpnEndpointRouteManager;
    private readonly VpnEndpointInventoryStore
        _vpnEndpointInventoryStore;

    public IranDirectController(
        IranPrefixProvider prefixProvider,
        PrefixFileRepository prefixRepository,
        GatewayDetector gatewayDetector,
        RouteReconciler routeReconciler,
        StateRepository stateRepository,
        RouteInventoryStore routeInventoryStore,
        OpenVpnEndpointProvider vpnEndpointProvider,
        VpnEndpointRouteManager vpnEndpointRouteManager,
        VpnEndpointInventoryStore vpnEndpointInventoryStore)
    {
        _prefixProvider = prefixProvider;
        _prefixRepository = prefixRepository;
        _gatewayDetector = gatewayDetector;
        _routeReconciler = routeReconciler;
        _stateRepository = stateRepository;
        _routeInventoryStore = routeInventoryStore;
        _vpnEndpointProvider = vpnEndpointProvider;
        _vpnEndpointRouteManager = vpnEndpointRouteManager;
        _vpnEndpointInventoryStore =
            vpnEndpointInventoryStore;
    }

    public async Task<int> UpdatePrefixesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> prefixes =
            await _prefixProvider.DownloadIpv4PrefixesAsync(
                cancellationToken);

        await _prefixRepository.SaveAsync(
            prefixes,
            cancellationToken);

        IranDirectState state =
            await _stateRepository.LoadAsync(
                cancellationToken);

        await _stateRepository.SaveAsync(
            state with
            {
                PrefixCount = prefixes.Count,
                PrefixesUpdatedAt =
                    DateTimeOffset.UtcNow,
                LastError = null
            },
            cancellationToken);

        return prefixes.Count;
    }

    public async Task<ReconciliationResult> EnableAsync(
        CancellationToken cancellationToken = default)
    {
        DirectGateway gateway =
            _gatewayDetector.Detect();

        IReadOnlyList<ResolvedVpnEndpoint> endpoints =
            await _vpnEndpointProvider.GetEndpointsAsync(
                cancellationToken);

        VpnEndpointProtectionResult endpointProtection =
            await _vpnEndpointRouteManager
                .EnsureProtectedAsync(
                    endpoints,
                    gateway,
                    cancellationToken);

        await SaveEndpointInventoryAsync(
            endpoints,
            endpointProtection,
            gateway,
            cancellationToken);

        IReadOnlyList<string> prefixes =
            await EnsurePrefixesAsync(
                cancellationToken);

        ReconciliationResult result =
            await _routeReconciler.EnableAsync(
                prefixes,
                gateway.Address,
                gateway.InterfaceIndex,
                RouteMetric,
                cancellationToken);

        HashSet<string> addedIdentities =
            result.AddedRouteIdentities.ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        ManagedRoute[] desiredRoutes = prefixes
            .Select(prefix => new ManagedRoute
            {
                DestinationPrefix = prefix,
                Gateway = gateway.Address,
                InterfaceIndex = gateway.InterfaceIndex,
                Metric = RouteMetric
            })
            .ToArray();

        RouteInventory inventory =
            await _routeInventoryStore.LoadAsync(
                cancellationToken);

        RouteInventoryItem[] ownedRoutes =
            inventory.Routes
                .Concat(
                    desiredRoutes
                        .Where(route =>
                            addedIdentities.Contains(
                                route.Identity))
                        .Select(ToInventoryItem))
                .GroupBy(
                    route => route.Identity,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

        await _routeInventoryStore.SaveAsync(
            inventory with
            {
                Routes = ownedRoutes
            },
            cancellationToken);

        IranDirectState previousState =
            await _stateRepository.LoadAsync(
                cancellationToken);

        await _stateRepository.SaveAsync(
            previousState with
            {
                Enabled = true,
                Gateway =
                    gateway.Address.ToString(),
                InterfaceIndex =
                    gateway.InterfaceIndex,
                InterfaceName =
                    gateway.InterfaceName,
                PrefixCount = prefixes.Count,
                EnabledAt =
                    previousState.EnabledAt
                    ?? DateTimeOffset.UtcNow,
                PrefixesUpdatedAt =
                    _prefixRepository.GetLastModified(),
                LastError = null
            },
            cancellationToken);

        return result;
    }

    public async Task<ReconciliationResult> DisableAsync(
        CancellationToken cancellationToken = default)
    {
        IranDirectState state =
            await _stateRepository.LoadAsync(
                cancellationToken);

        RouteInventory inventory =
            await _routeInventoryStore.LoadAsync(
                cancellationToken);

        ManagedRoute[] ownedRoutes =
            ParseInventory(inventory.Routes);

        ReconciliationResult result;

        if (ownedRoutes.Length > 0)
        {
            result =
                await _routeReconciler.DisableAsync(
                    ownedRoutes,
                    cancellationToken);

            await _routeInventoryStore.ClearAsync(
                cancellationToken);
        }
        else
        {
            result = await DisableLegacyRoutesAsync(
                state,
                cancellationToken);
        }

        await _stateRepository.SaveAsync(
            state with
            {
                Enabled = false,
                LastError = null
            },
            cancellationToken);

        return result;
    }

    public async Task<IranDirectStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        IranDirectState state =
            await _stateRepository.LoadAsync(
                cancellationToken);

        IReadOnlyList<string> prefixes =
            await _prefixRepository.LoadAsync(
                cancellationToken);

        RouteInventory inventory =
            await _routeInventoryStore.LoadAsync(
                cancellationToken);

        ManagedRoute[] ownedRoutes =
            ParseInventory(inventory.Routes);

        int installed;

        if (ownedRoutes.Length > 0)
        {
            installed =
                await _routeReconciler
                    .CountMatchingRoutesAsync(
                        ownedRoutes,
                        cancellationToken);
        }
        else
        {
            installed =
                await CountLegacyRoutesAsync(
                    state,
                    prefixes,
                    cancellationToken);
        }

        return new IranDirectStatus
        {
            Enabled = state.Enabled,
            Gateway = state.Gateway,
            InterfaceIndex =
                state.InterfaceIndex,
            InterfaceName =
                state.InterfaceName,
            PrefixCount =
                prefixes.Count,
            InstalledRouteCount =
                installed,
            PrefixesUpdatedAt =
                state.PrefixesUpdatedAt,
            LastError =
                state.LastError
        };
    }

    public async Task RepairAsync(
        CancellationToken cancellationToken = default)
    {
        IranDirectState state =
            await _stateRepository.LoadAsync(
                cancellationToken);

        if (!state.Enabled)
        {
            return;
        }

        await EnableAsync(cancellationToken);
    }

    private async Task SaveEndpointInventoryAsync(
        IReadOnlyCollection<ResolvedVpnEndpoint> endpoints,
        VpnEndpointProtectionResult protection,
        DirectGateway gateway,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ResolvedVpnEndpoint> endpointsByPrefix =
            endpoints
                .GroupBy(
                    endpoint => $"{endpoint.Address}/32",
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        VpnEndpointInventory inventory =
            await _vpnEndpointInventoryStore.LoadAsync(
                cancellationToken);

        VpnEndpointInventoryItem[] protectedEndpoints =
            protection.ProtectedRoutes
                .Select(route =>
                {
                    ResolvedVpnEndpoint endpoint =
                        endpointsByPrefix[
                            route.DestinationPrefix];

                    return new VpnEndpointInventoryItem
                    {
                        Host = endpoint.Host,
                        Address = endpoint.Address,
                        Port = endpoint.Port,
                        Protocol = endpoint.Protocol,
                        DestinationPrefix =
                            route.DestinationPrefix,
                        Gateway =
                            gateway.Address.ToString(),
                        InterfaceIndex =
                            gateway.InterfaceIndex,
                        Metric = route.Metric,
                        AddedByIranDirect =
                            protection.AddedRouteIdentities
                                .Contains(route.Identity),
                        ProtectedAt =
                            DateTimeOffset.UtcNow
                    };
                })
                .ToArray();

        await _vpnEndpointInventoryStore.SaveAsync(
            inventory with
            {
                Endpoints = protectedEndpoints
            },
            cancellationToken);
    }

    private async Task<ReconciliationResult>
        DisableLegacyRoutesAsync(
            IranDirectState state,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<string> prefixes =
            await _prefixRepository.LoadAsync(
                cancellationToken);

        if (!IPAddress.TryParse(
                state.Gateway,
                out IPAddress? gateway))
        {
            return new ReconciliationResult
            {
                DesiredCount = prefixes.Count
            };
        }

        ManagedRoute[] routes = prefixes
            .Select(prefix => new ManagedRoute
            {
                DestinationPrefix = prefix,
                Gateway = gateway,
                InterfaceIndex = state.InterfaceIndex,
                Metric = RouteMetric
            })
            .ToArray();

        return await _routeReconciler.DisableAsync(
            routes,
            cancellationToken);
    }

    private async Task<int> CountLegacyRoutesAsync(
        IranDirectState state,
        IReadOnlyCollection<string> prefixes,
        CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(
                state.Gateway,
                out IPAddress? gateway)
            || state.InterfaceIndex == 0)
        {
            return 0;
        }

        return await _routeReconciler
            .CountMatchingRoutesAsync(
                prefixes,
                gateway,
                state.InterfaceIndex,
                cancellationToken);
    }

    private static RouteInventoryItem ToInventoryItem(
        ManagedRoute route)
    {
        return new RouteInventoryItem
        {
            DestinationPrefix =
                route.DestinationPrefix,
            Gateway =
                route.Gateway.ToString(),
            InterfaceIndex =
                route.InterfaceIndex,
            Metric = route.Metric
        };
    }

    private static ManagedRoute[] ParseInventory(
        IReadOnlyCollection<RouteInventoryItem> routes)
    {
        List<ManagedRoute> parsed = [];

        foreach (RouteInventoryItem route in routes)
        {
            if (!IPAddress.TryParse(
                    route.Gateway,
                    out IPAddress? gateway))
            {
                continue;
            }

            parsed.Add(
                new ManagedRoute
                {
                    DestinationPrefix =
                        route.DestinationPrefix,
                    Gateway = gateway,
                    InterfaceIndex =
                        route.InterfaceIndex,
                    Metric = route.Metric
                });
        }

        return parsed.ToArray();
    }

    private async Task<IReadOnlyList<string>>
        EnsurePrefixesAsync(
            CancellationToken cancellationToken)
    {
        IReadOnlyList<string> prefixes =
            await _prefixRepository.LoadAsync(
                cancellationToken);

        if (prefixes.Count > 0)
        {
            return prefixes;
        }

        await UpdatePrefixesAsync(
            cancellationToken);

        prefixes =
            await _prefixRepository.LoadAsync(
                cancellationToken);

        if (prefixes.Count == 0)
        {
            throw new InvalidOperationException(
                "No Iranian IPv4 prefixes are available.");
        }

        return prefixes;
    }
}