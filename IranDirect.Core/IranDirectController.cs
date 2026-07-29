using IranDirect.Core.Configuration;
using IranDirect.Core.Models;
using IranDirect.Core.Networking;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
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
    private readonly StateRepository _stateRepository;
    private readonly RouteInventoryStore _routeInventoryStore;
    private readonly OpenVpnEndpointProvider _vpnEndpointProvider;
    private readonly VpnEndpointRouteManager _vpnEndpointRouteManager;
    private readonly VpnEndpointInventoryStore
        _vpnEndpointInventoryStore;
    private readonly IRouteManager _routeManager;
    private readonly RuntimeCycleCoordinator
        _runtimeCycleCoordinator;
    private readonly IRuntimeExecutor _runtimeExecutor;
    private readonly DesiredConfigurationService
        _configurationService;
    private readonly RuntimeOperationStatus _operationStatus;

    public IranDirectController(
        IranPrefixProvider prefixProvider,
        PrefixFileRepository prefixRepository,
        GatewayDetector gatewayDetector,
        IRouteManager routeManager,
        StateRepository stateRepository,
        RouteInventoryStore routeInventoryStore,
        OpenVpnEndpointProvider vpnEndpointProvider,
        VpnEndpointRouteManager vpnEndpointRouteManager,
        VpnEndpointInventoryStore vpnEndpointInventoryStore,
        RuntimeCycleCoordinator runtimeCycleCoordinator,
        IRuntimeExecutor runtimeExecutor,
        DesiredConfigurationService configurationService,
        RuntimeOperationStatus operationStatus)
    {
        _prefixProvider = prefixProvider;
        _prefixRepository = prefixRepository;
        _gatewayDetector = gatewayDetector;
        _stateRepository = stateRepository;
        _routeInventoryStore = routeInventoryStore;
        _vpnEndpointProvider = vpnEndpointProvider;
        _vpnEndpointRouteManager = vpnEndpointRouteManager;
        _vpnEndpointInventoryStore =
            vpnEndpointInventoryStore;
        _routeManager = routeManager;
        _runtimeCycleCoordinator = runtimeCycleCoordinator;
        _runtimeExecutor = runtimeExecutor;
        _configurationService = configurationService;
        _operationStatus = operationStatus;
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

    public async Task<RuntimeCycleExecutionResult> EnableAsync(
        CancellationToken cancellationToken = default)
    {
        _operationStatus.Begin(OperationState.Enabling, "user");

        await EnsurePrefixesAsync(cancellationToken);

        await _configurationService.SetEnabledAsync(
            true, cancellationToken);

        return await RunCycleCoreAsync(cancellationToken);
    }

    public async Task<RuntimeCycleExecutionResult> DisableAsync(
        CancellationToken cancellationToken = default)
    {
        _operationStatus.Begin(OperationState.Disabling, "user");

        await _configurationService.SetEnabledAsync(
            false, cancellationToken);

        return await RunCycleCoreAsync(cancellationToken);
    }

    public async Task<RuntimeCycleExecutionResult> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        _operationStatus.Begin(OperationState.Repairing, "cycle");
        return await RunCycleCoreAsync(cancellationToken);
    }

    private async Task<RuntimeCycleExecutionResult> RunCycleCoreAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            RuntimeDecision decision =
                await _runtimeCycleCoordinator.RunCycleAsync(
                    cancellationToken);

            _operationStatus.SetPlannedSteps(decision.ExecutionPlan.Count);

            RuntimeExecutionResult execution =
                await _runtimeExecutor.ExecuteAsync(
                    decision.ExecutionPlan,
                    _operationStatus,
                    cancellationToken);

            _operationStatus.Complete(execution);

            await UpdateStateAsync(
                decision, execution, cancellationToken);

            return new RuntimeCycleExecutionResult
            {
                Decision = decision,
                Execution = execution
            };
        }
        catch (OperationCanceledException)
        {
            _operationStatus.Fail("Operation was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _operationStatus.Fail(ex.Message);
            throw;
        }
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

        installed = await CountOwnedRoutesAsync(
            ownedRoutes, cancellationToken);

        VpnEndpointInventory endpointInventory =
            await _vpnEndpointInventoryStore.LoadAsync(
                cancellationToken);

        VpnEndpointProtectionHealth endpointHealth =
            await _vpnEndpointRouteManager.GetHealthAsync(
                endpointInventory.Endpoints,
                cancellationToken);

        DesiredConfiguration config =
            await _configurationService.GetAsync(
                cancellationToken);

        return new IranDirectStatus
        {
            Enabled = state.Enabled,
            DesiredEnabled = config.Enabled,
            Operation = _operationStatus,
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
                state.LastError,
            VpnEndpointCount =
                endpointHealth.CurrentEndpointCount,
            ProtectedVpnEndpointCount =
                endpointHealth.ProtectedEndpointCount,
            VpnEndpointsProtected =
                endpointHealth.IsProtected
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

        await RunCycleAsync(cancellationToken);
    }

    private async Task UpdateStateAsync(
        RuntimeDecision decision,
        RuntimeExecutionResult execution,
        CancellationToken cancellationToken)
    {
        IranDirectState state =
            await _stateRepository.LoadAsync(
                cancellationToken);

        if (execution.Status is
                RuntimeExecutionResultStatus.Completed
                or RuntimeExecutionResultStatus
                    .NoExecutionRequired)
        {
            if (decision.Plan.Desired.Enabled)
            {
                ObservedDirectGateway? gateway =
                    decision.Plan.Observed.DirectGateway;

                IReadOnlyList<string> prefixes =
                    await _prefixRepository.LoadAsync(
                        cancellationToken);

                await _stateRepository.SaveAsync(
                    state with
                    {
                        Enabled = true,
                        Gateway = gateway?.Address
                            ?? state.Gateway,
                        InterfaceIndex = gateway?.InterfaceIndex
                            ?? state.InterfaceIndex,
                        InterfaceName = gateway?.InterfaceName
                            ?? state.InterfaceName,
                        PrefixCount = prefixes.Count,
                        EnabledAt = state.EnabledAt
                            ?? DateTimeOffset.UtcNow,
                        PrefixesUpdatedAt =
                            _prefixRepository.GetLastModified(),
                        LastError = null
                    },
                    cancellationToken);
            }
            else
            {
                await _stateRepository.SaveAsync(
                    state with
                    {
                        Enabled = false,
                        LastError = null
                    },
                    cancellationToken);

                if (execution.MutatedInfrastructure)
                {
                    await _routeInventoryStore.ClearAsync(
                        cancellationToken);
                }
            }
        }
        else
        {
            await _stateRepository.SaveAsync(
                state with
                {
                    LastError = execution.ErrorMessage
                        ?? "Cycle failed."
                },
                cancellationToken);
        }
    }

    private static string ToIdentity(SystemRoute route) =>
        $"{route.DestinationPrefix}|" +
        $"{route.NextHop}|" +
        $"{route.InterfaceIndex}";

    private async Task<int> CountOwnedRoutesAsync(
        ManagedRoute[] ownedRoutes,
        CancellationToken cancellationToken)
    {
        if (ownedRoutes.Length == 0)
            return 0;

        IReadOnlyList<SystemRoute> actual =
            await _routeManager.GetIpv4RoutesAsync(
                cancellationToken);

        HashSet<string> actualIdentities = actual
            .Select(ToIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ownedRoutes.Count(route =>
            actualIdentities.Contains(route.Identity));
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
