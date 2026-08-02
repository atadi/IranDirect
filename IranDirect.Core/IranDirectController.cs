using IranDirect.Core.Configuration;
using IranDirect.Core.Models;
using IranDirect.Core.Networking;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Profiling;
using IranDirect.Core.State;
using IranDirect.Core.Vpn;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;

namespace IranDirect.Core;

public sealed class IranDirectController
{
    private const int RouteMetric = 5;

    private readonly IPrefixSource _prefixSource;
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
    private readonly RuntimeCycleProfiler _profiler;
    private readonly IPrefixSourceMetadataService?
        _prefixSourceMetadataService;
    private readonly ILogger<IranDirectController>? _logger;

    public IranDirectController(
        IPrefixSource prefixSource,
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
        RuntimeOperationStatus operationStatus,
        RuntimeCycleProfiler? profiler = null,
        IPrefixSourceMetadataService? prefixSourceMetadataService =
            null,
        ILogger<IranDirectController>? logger = null)
    {
        _prefixSource = prefixSource;
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
        _profiler = profiler ?? RuntimeCycleProfiler.Noop;
        _prefixSourceMetadataService =
            prefixSourceMetadataService;
        _logger = logger;
    }

    public async Task<int> UpdatePrefixesAsync(
        CancellationToken cancellationToken = default)
    {
        PrefixSourceFetchResult fetch;

        try
        {
            fetch = await _prefixSource.FetchAsync(
                new PrefixSourceRequest(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            await TryRecordMetadataFailureAsync(
                exception,
                cancellationToken);

            throw;
        }

        IReadOnlyList<string> previousPrefixes =
            await _prefixRepository.LoadAsync(
                cancellationToken);

        if (!fetch.NotModified)
        {
            await _prefixRepository.SaveAsync(
                fetch.Prefixes,
                cancellationToken);
        }

        await TryRecordMetadataSuccessAsync(
            fetch,
            previousPrefixes,
            cancellationToken);

        IranDirectState state =
            await _stateRepository.LoadAsync(
                cancellationToken);

        await _stateRepository.SaveAsync(
            state with
            {
                PrefixCount = fetch.Prefixes.Count,
                PrefixesUpdatedAt =
                    DateTimeOffset.UtcNow,
                LastError = null
            },
            cancellationToken);

        return fetch.Prefixes.Count;
    }

    private async Task TryRecordMetadataSuccessAsync(
        PrefixSourceFetchResult fetch,
        IReadOnlyList<string> previousPrefixes,
        CancellationToken cancellationToken)
    {
        if (_prefixSourceMetadataService is null)
        {
            return;
        }

        try
        {
            if (fetch.NotModified)
            {
                await _prefixSourceMetadataService
                    .RecordNotModifiedAsync(
                        fetch,
                        cancellationToken);
            }
            else
            {
                await _prefixSourceMetadataService
                    .RecordSuccessAsync(
                        fetch,
                        previousPrefixes,
                        cancellationToken);
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(
                "Failed to persist prefix source metadata: " +
                "{Error}",
                exception.Message);
        }
    }

    private async Task TryRecordMetadataFailureAsync(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (_prefixSourceMetadataService is null)
        {
            return;
        }

        try
        {
            await _prefixSourceMetadataService
                .RecordFailureAsync(
                    _prefixSource.Descriptor,
                    exception.Message,
                    cancellationToken);
        }
        catch (Exception metadataException)
        {
            _logger?.LogWarning(
                "Failed to persist prefix source failure " +
                "metadata: {Error}",
                metadataException.Message);
        }
    }

    public async Task<RuntimeCycleExecutionResult> EnableAsync(
        CancellationToken cancellationToken = default)
    {
        using IDisposable cycle = _profiler.BeginCycleIfNone("enable");

        _operationStatus.Begin(OperationState.Enabling, "user");

        try
        {
            await EnsurePrefixesAsync(cancellationToken);

            await _configurationService.SetEnabledAsync(
                true, cancellationToken);

            return await RunCycleCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RecordCycleOutcome(
                CycleCompletionStatus.Cancelled,
                "Operation was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            RecordCycleOutcome(
                CycleCompletionStatus.Failed,
                ex.Message);
            throw;
        }
    }

    public async Task<RuntimeCycleExecutionResult> DisableAsync(
        CancellationToken cancellationToken = default)
    {
        using IDisposable cycle = _profiler.BeginCycleIfNone("disable");

        _operationStatus.Begin(OperationState.Disabling, "user");

        try
        {
            await _configurationService.SetEnabledAsync(
                false, cancellationToken);

            return await RunCycleCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RecordCycleOutcome(
                CycleCompletionStatus.Cancelled,
                "Operation was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            RecordCycleOutcome(
                CycleCompletionStatus.Failed,
                ex.Message);
            throw;
        }
    }

    public async Task<RuntimeCycleExecutionResult> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        using IDisposable cycle = _profiler.BeginCycleIfNone("repair");

        _operationStatus.Begin(OperationState.Repairing, "cycle");

        try
        {
            return await RunCycleCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RecordCycleOutcome(
                CycleCompletionStatus.Cancelled,
                "Operation was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            RecordCycleOutcome(
                CycleCompletionStatus.Failed,
                ex.Message);
            throw;
        }
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

            RuntimeExecutionResult execution;

            using (_profiler.Measure(
                RuntimePerfCategory.ExecutionTotal))
            {
                execution =
                    await _runtimeExecutor.ExecuteAsync(
                        decision.ExecutionPlan,
                        _operationStatus,
                        cancellationToken);
            }

            _operationStatus.Complete(execution);

            await UpdateStateAsync(
                decision, execution, cancellationToken);

            RecordCycleOutcome(execution, decision);

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

    private void RecordCycleOutcome(
        RuntimeExecutionResult execution,
        RuntimeDecision decision)
    {
        _profiler.SetCycleOutcome(
            MapCompletionStatus(execution.Status),
            execution.ErrorMessage,
            decision.ExecutionPlan.Count,
            _operationStatus.CompletedSteps);
    }

    private void RecordCycleOutcome(
        CycleCompletionStatus status,
        string error)
    {
        RuntimeOperationSnapshot snapshot =
            _operationStatus.CreateSnapshot();
        _profiler.SetCycleOutcome(
            status,
            error,
            snapshot.PlannedSteps,
            snapshot.CompletedSteps);
    }

    private static CycleCompletionStatus MapCompletionStatus(
        RuntimeExecutionResultStatus status) => status switch
    {
        RuntimeExecutionResultStatus.Completed =>
            CycleCompletionStatus.Completed,
        RuntimeExecutionResultStatus.NoExecutionRequired =>
            CycleCompletionStatus.Completed,
        RuntimeExecutionResultStatus.Planned =>
            CycleCompletionStatus.Completed,
        RuntimeExecutionResultStatus.PartiallyCompleted =>
            CycleCompletionStatus.PartiallyCompleted,
        RuntimeExecutionResultStatus.Failed =>
            CycleCompletionStatus.Failed,
        RuntimeExecutionResultStatus.Cancelled =>
            CycleCompletionStatus.Cancelled,
        _ => CycleCompletionStatus.Failed
    };

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
            Operation = _operationStatus.CreateSnapshot(),
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

        bool isSuccess = execution.Status switch
        {
            RuntimeExecutionResultStatus.Completed => true,
            RuntimeExecutionResultStatus.NoExecutionRequired => true,
            RuntimeExecutionResultStatus.PartiallyCompleted => false,
            RuntimeExecutionResultStatus.Failed => false,
            RuntimeExecutionResultStatus.Cancelled => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(execution.Status), execution.Status, null)
        };

        if (isSuccess)
        {
            if (decision.Plan.Desired.Enabled)
            {
                ObservedDirectGateway? gateway =
                    decision.Plan.Observed.DirectGateway;

                IReadOnlyList<string> prefixes =
                    await _prefixRepository.LoadAsync(
                        cancellationToken);

                using (_profiler.Measure(
                    RuntimePerfCategory.PersistenceStateSave))
                {
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
            }
            else
            {
                using (_profiler.Measure(
                    RuntimePerfCategory.PersistenceStateSave))
                {
                    await _stateRepository.SaveAsync(
                        state with
                        {
                            Enabled = false,
                            LastError = null
                        },
                        cancellationToken);
                }

                if (execution.MutatedInfrastructure)
                {
                    using (_profiler.Measure(
                        RuntimePerfCategory.PersistenceInventorySave))
                    {
                        await _routeInventoryStore.ClearAsync(
                            cancellationToken);
                    }
                }
            }
        }
        else
        {
            string failureSummary = BuildFailureSummary(execution);

            using (_profiler.Measure(
                RuntimePerfCategory.PersistenceStateSave))
            {
                await _stateRepository.SaveAsync(
                    state with { LastError = failureSummary },
                    cancellationToken);
            }
        }
    }

    private static string BuildFailureSummary(
        RuntimeExecutionResult execution)
    {
        RuntimeExecutionStepResult[] failedSteps = execution.StepResults
            .Where(sr => sr.Status == RuntimeExecutionStepStatus.Failed)
            .ToArray();

        if (failedSteps.Length == 0)
            return execution.ErrorMessage ?? "Cycle failed.";

        RuntimeExecutionStepResult first = failedSteps[0];

        string target = string.IsNullOrWhiteSpace(
            first.DestinationPrefix)
                ? first.StepIdentity
                : first.DestinationPrefix;

        StringBuilder sb = new();
        sb.AppendLine($"{failedSteps.Length} step(s) failed.");
        sb.AppendLine("First failure:");
        sb.AppendLine($"Type: {first.Kind}");
        sb.AppendLine($"Target: {target}");
        sb.AppendLine($"Reason: {first.ErrorMessage ?? "Unknown error"}");

        return sb.ToString().TrimEnd();
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
