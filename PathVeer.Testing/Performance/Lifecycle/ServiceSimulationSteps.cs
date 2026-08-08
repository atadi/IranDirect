using System.Net;
using PathVeer.Core.Routing;
using PathVeer.Core.Runtime;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Testing.Performance.Lifecycle;

/// <summary>
/// A run cycle step: drives the production
/// <see cref="PathVeer.Core.IranDirectController.RunCycleAsync"/>,
/// records the outcome, and marks a sampling boundary.
/// </summary>
public sealed class RunCycleStep : IServiceSimulationStep
{
    public RunCycleStep(string name = "cycle")
    {
        Name = name;
    }

    public string Name { get; }

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        RuntimeCycleExecutionResult cycle =
            await context.Environment.RunCycleAsync(cancellationToken);

        context.LastCycleResult = cycle.Execution;
        context.Metrics.RecordCycle(cycle.Execution);
        context.Metrics.RecordRouteCallCounts(
            context.Environment.RouteTable);
        await context.Sampler.MaybeSampleBoundaryAsync(cancellationToken);
    }
}

/// <summary>
/// Sets the desired Enabled flag through the production
/// <see cref="PathVeer.Core.Configuration.DesiredConfigurationService"/>.
/// </summary>
public sealed class SetEnabledStep : IServiceSimulationStep
{
    private readonly bool _enabled;

    public SetEnabledStep(bool enabled)
    {
        _enabled = enabled;
        Name = _enabled ? "set-enabled" : "set-disabled";
    }

    public string Name { get; }

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken) =>
        await context.Environment.ConfigurationService
            .SetEnabledAsync(_enabled, cancellationToken);
}

/// <summary>
/// Runs the full production enable path
/// (<see cref="PathVeer.Core.IranDirectController.EnableAsync"/>),
/// including prefix discovery, and records the resulting cycle like a
/// <see cref="RunCycleStep"/>.
/// </summary>
public sealed class EnableStep : IServiceSimulationStep
{
    public string Name => "enable";

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        RuntimeCycleExecutionResult cycle =
            await context.Environment.EnableAsync(cancellationToken);

        context.LastCycleResult = cycle.Execution;
        context.Metrics.RecordCycle(cycle.Execution);
        context.Metrics.RecordRouteCallCounts(
            context.Environment.RouteTable);
        await context.Sampler.MaybeSampleBoundaryAsync(cancellationToken);
    }
}

/// <summary>
/// Runs the full production disable path
/// (<see cref="PathVeer.Core.IranDirectController.DisableAsync"/>)
/// and records the resulting cycle like a <see cref="RunCycleStep"/>.
/// </summary>
public sealed class DisableStep : IServiceSimulationStep
{
    public string Name => "disable";

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        RuntimeCycleExecutionResult cycle =
            await context.Environment.DisableAsync(cancellationToken);

        context.LastCycleResult = cycle.Execution;
        context.Metrics.RecordCycle(cycle.Execution);
        context.Metrics.RecordRouteCallCounts(
            context.Environment.RouteTable);
        await context.Sampler.MaybeSampleBoundaryAsync(cancellationToken);
    }
}

/// <summary>
/// A deterministic observation view produced by
/// <see cref="RotateObservationStep"/>. The step applies it to the
/// observation state and re-synchronizes observed routes from the
/// simulated route table (after any drift routes are applied).
/// </summary>
public sealed record ObservedRotation(
    IReadOnlyList<string> Prefixes,
    IReadOnlyList<ObservedVpnEndpoint>? Endpoints = null,
    ObservedDirectGateway? Gateway = null,
    IReadOnlyList<ObservedRoute>? DriftRoutes = null);

/// <summary>
/// Applies an index-produced <see cref="ObservedRotation"/> to the
/// observation state. Drift routes are added to the simulated route
/// table first (as if a foreign process created them), then observed
/// routes are re-synced from the table so the next cycle sees exactly
/// what the simulation left on the platform.
/// </summary>
public sealed class RotateObservationStep : IServiceSimulationStep
{
    private readonly Func<int, ObservedRotation> _producer;
    private int _executions;

    public RotateObservationStep(Func<int, ObservedRotation> producer)
    {
        _producer = producer ?? throw new ArgumentNullException(
            nameof(producer));
    }

    public string Name => "rotate-observation";

    public Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int index = Interlocked.Increment(ref _executions) - 1;
        ObservedRotation rotation = _producer(index);

        SimulatedRuntimeEnvironment environment = context.Environment;

        if (rotation.DriftRoutes is { Count: > 0 })
        {
            foreach (ObservedRoute route in rotation.DriftRoutes)
            {
                environment.RouteTable.Add(
                [
                    new ManagedRoute
                    {
                        DestinationPrefix = route.DestinationPrefix,
                        Gateway = IPAddress.Parse(route.NextHop),
                        InterfaceIndex = route.InterfaceIndex,
                        Metric = route.Metric
                    }
                ]);
            }
        }

        ObservationState observation = environment.Observation;

        if (rotation.Endpoints is not null)
        {
            observation.VpnEndpoints = rotation.Endpoints;
        }

        if (rotation.Gateway is not null)
        {
            observation.DirectGateway = rotation.Gateway;
        }

        observation.Prefixes = rotation.Prefixes;

        observation.Routes = environment
            .RouteTable
            .Snapshot()
            .Select(route => new ObservedRoute
            {
                DestinationPrefix = route.DestinationPrefix,
                NextHop = route.NextHop.ToString(),
                InterfaceIndex = route.InterfaceIndex,
                Metric = route.RouteMetric
            })
            .ToArray();

        return Task.CompletedTask;
    }
}

/// <summary>
/// Advances the simulated clock by the given duration, firing any
/// due monitor timers deterministically.
/// </summary>
public sealed class AdvanceTimeStep : IServiceSimulationStep
{
    private readonly TimeSpan _duration;

    public AdvanceTimeStep(TimeSpan duration)
    {
        _duration = duration;
    }

    public string Name => $"advance-{_duration.TotalMinutes:0}m";

    public Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Environment.Time.Advance(_duration);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Seeds the observation state (prefixes, VPN endpoints, gateway)
/// and writes the prefix file used by status. Routes observed on the
/// platform are set to the current simulated route table.
/// </summary>
public sealed class SeedObservationStep : IServiceSimulationStep
{
    private readonly IReadOnlyList<string> _prefixes;
    private readonly IReadOnlyList<ObservedVpnEndpoint> _endpoints;
    private readonly ObservedDirectGateway _gateway;
    private readonly bool _writePrefixFile;

    public SeedObservationStep(
        IReadOnlyList<string> prefixes,
        IReadOnlyList<ObservedVpnEndpoint>? endpoints = null,
        ObservedDirectGateway? gateway = null,
        bool writePrefixFile = true)
    {
        _prefixes = prefixes;
        _endpoints = endpoints ?? [];
        _gateway = gateway ?? new ObservedDirectGateway
        {
            Address = "192.168.1.1",
            InterfaceIndex = 11,
            InterfaceName = "Ethernet"
        };
        _writePrefixFile = writePrefixFile;
    }

    public string Name => "seed-observation";

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        ObservationState observation = context.Environment.Observation;

        observation.VpnProfileExists = true;
        observation.DirectGateway = _gateway;
        observation.VpnEndpoints = _endpoints;
        observation.Prefixes = _prefixes;
        observation.Routes = ToObservedRoutes(
            context.Environment.RouteTable.Snapshot());

        if (_writePrefixFile)
        {
            await context.Environment.PrefixStore.SavePrefixesAsync(
                context.Environment.Country,
                _prefixes,
                cancellationToken);
        }
    }

    private static IReadOnlyList<ObservedRoute> ToObservedRoutes(
        IReadOnlyList<SystemRoute> routes) =>
        routes
            .Select(route => new ObservedRoute
            {
                DestinationPrefix = route.DestinationPrefix,
                NextHop = route.NextHop.ToString(),
                InterfaceIndex = route.InterfaceIndex,
                Metric = route.RouteMetric
            })
            .ToArray();
}

/// <summary>
/// Re-observes the platform route table: copies the simulated route
/// table into the observation state so the next cycle sees what the
/// previous cycle applied (plus any injected drift).
/// </summary>
public sealed class SyncObservationFromRouteTableStep : IServiceSimulationStep
{
    public string Name => "sync-observation";

    public Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        context.Environment.Observation.Routes = context.Environment
            .RouteTable
            .Snapshot()
            .Select(route => new ObservedRoute
            {
                DestinationPrefix = route.DestinationPrefix,
                NextHop = route.NextHop.ToString(),
                InterfaceIndex = route.InterfaceIndex,
                Metric = route.RouteMetric
            })
            .ToArray();

        return Task.CompletedTask;
    }
}

/// <summary>
/// Injects platform drift: adds routes to the simulated route table
/// as if a foreign process created them, then re-syncs observation.
/// </summary>
public sealed class InjectRouteDriftStep : IServiceSimulationStep
{
    private readonly IReadOnlyList<ObservedRoute> _routes;

    public InjectRouteDriftStep(IReadOnlyList<ObservedRoute> routes)
    {
        _routes = routes;
    }

    public string Name => "inject-drift";

    public Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (ObservedRoute route in _routes)
        {
            context.Environment.RouteTable.Add(
            [
                new ManagedRoute
                {
                    DestinationPrefix = route.DestinationPrefix,
                    Gateway = IPAddress.Parse(route.NextHop),
                    InterfaceIndex = route.InterfaceIndex,
                    Metric = route.Metric
                }
            ]);
        }

        context.Environment.Observation.Routes = context.Environment
            .RouteTable
            .Snapshot()
            .Select(item => new ObservedRoute
            {
                DestinationPrefix = item.DestinationPrefix,
                NextHop = item.NextHop.ToString(),
                InterfaceIndex = item.InterfaceIndex,
                Metric = item.RouteMetric
            })
            .ToArray();

        return Task.CompletedTask;
    }
}

/// <summary>
/// Captures one or more runtime snapshots through the production
/// <see cref="PathVeer.Core.Observability.RuntimeSnapshotProvider"/>.
/// </summary>
public sealed class SnapshotStep : IServiceSimulationStep
{
    private readonly int _count;

    public SnapshotStep(int count = 1)
    {
        _count = count;
    }

    public string Name => "snapshot";

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < _count; i++)
        {
            await context.Environment.SnapshotProvider
                .GetSnapshotAsync(cancellationToken);
            context.Metrics.RecordSnapshot();
        }

        await context.Sampler.MaybeSampleBoundaryAsync(cancellationToken);
    }
}

/// <summary>
/// Runs all diagnostics one or more times through the production
/// <see cref="PathVeer.Core.Diagnostics.DiagnosticRunner"/>.
/// </summary>
public sealed class DiagnosticsStep : IServiceSimulationStep
{
    private readonly int _count;

    public DiagnosticsStep(int count = 1)
    {
        _count = count;
    }

    public string Name => "diagnostics";

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < _count; i++)
        {
            await context.Environment.DiagnosticRunner
                .RunAllAsync(cancellationToken);
            context.Metrics.RecordDiagnosticRun();
        }

        await context.Sampler.MaybeSampleBoundaryAsync(cancellationToken);
    }
}

/// <summary>
/// Builds execution previews one or more times through the production
/// <see cref="PathVeer.Core.Planning.RuntimePreviewPlanner"/>.
/// </summary>
public sealed class PreviewStep : IServiceSimulationStep
{
    private readonly int _count;

    public PreviewStep(int count = 1)
    {
        _count = count;
    }

    public string Name => "preview";

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < _count; i++)
        {
            await context.Environment.PreviewPlanner
                .BuildPreviewAsync(cancellationToken);
            context.Metrics.RecordPreview();
        }

        await context.Sampler.MaybeSampleBoundaryAsync(cancellationToken);
    }
}

/// <summary>
/// Starts the production <see cref="PathVeer.Core.Prefixes.PrefixUpdateMonitor"/>.
/// </summary>
public sealed class MonitorStartStep : IServiceSimulationStep
{
    public string Name => "monitor-start";

    public Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken) =>
        context.Environment.Monitor.StartAsync(cancellationToken);
}

/// <summary>
/// Stops the production prefix update monitor.
/// </summary>
public sealed class MonitorStopStep : IServiceSimulationStep
{
    public string Name => "monitor-stop";

    public Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken) =>
        context.Environment.Monitor.StopAsync(cancellationToken);
}

/// <summary>
/// Forces one or more prefix update checks through the production
/// monitor, including the deterministic path where the simulated
/// clock advances so a scheduled check fires.
/// </summary>
public sealed class MonitorForceCheckStep : IServiceSimulationStep
{
    private readonly int _count;

    public MonitorForceCheckStep(int count = 1)
    {
        _count = count;
    }

    public string Name => "monitor-check";

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < _count; i++)
        {
            await context.Environment.Monitor.ForceCheckAsync(
                cancellationToken);
            context.Metrics.RecordMonitorCheck();
        }

        await context.Sampler.MaybeSampleBoundaryAsync(cancellationToken);
    }
}

/// <summary>
/// Resolves custom routes one or more times through the production
/// <see cref="PathVeer.Core.CustomRoutes.CustomRouteResolver"/>.
/// </summary>
public sealed class ResolveCustomRoutesStep : IServiceSimulationStep
{
    private readonly int _count;

    public ResolveCustomRoutesStep(int count = 1)
    {
        _count = count;
    }

    public string Name => "resolve-custom-routes";

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < _count; i++)
        {
            await context.Environment.CustomRouteResolver
                .ResolveAsync(cancellationToken);
            context.Metrics.RecordCustomRouteResolution();
        }

        await context.Sampler.MaybeSampleBoundaryAsync(cancellationToken);
    }
}

/// <summary>
/// Exports one or more support bundles to the workspace support
/// directory through the production
/// <see cref="PathVeer.Core.Support.SupportBundleExporter"/>.
/// </summary>
public sealed class ExportSupportBundleStep : IServiceSimulationStep
{
    private readonly int _count;
    private readonly string _fileNamePrefix;

    public ExportSupportBundleStep(
        int count = 1,
        string fileNamePrefix = "bundle")
    {
        _count = count;
        _fileNamePrefix = fileNamePrefix;
    }

    public string Name => "export-support-bundle";

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < _count; i++)
        {
            string path = Path.Combine(
                context.Environment.Workspace.SupportDirectory,
                $"{_fileNamePrefix}-{i}.zip");

            await context.Environment.SupportBundleExporter
                .ExportAsync(path, cancellationToken);

            context.Metrics.RecordSupportBundleExport();
        }

        await context.Sampler.MaybeSampleBoundaryAsync(cancellationToken);
    }
}

/// <summary>
/// Runs the wrapped step inside a
/// <see cref="FaultInjectionScope"/> for one fault point. Expected
/// (non-cancellation) exceptions are recorded as recoverable failures
/// and swallowed so the scenario can continue and recover.
/// </summary>
public sealed class FaultScopeStep : IServiceSimulationStep
{
    private readonly FaultInjectionPoint _point;
    private readonly IServiceSimulationStep _inner;

    public FaultScopeStep(
        FaultInjectionPoint point,
        IServiceSimulationStep inner)
    {
        _point = point;
        _inner = inner;
    }

    public string Name => $"fault-{_point}-{_inner.Name}";

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        context.Metrics.RecordFaultScopeEntered();

        using (FaultInjectionScope.Fail(_point))
        {
            try
            {
                await _inner.ExecuteAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                context.Metrics.RecordRecoverableFailure();
            }
        }
    }
}

/// <summary>
/// Runs a list of steps in order.
/// </summary>
public sealed class CompositeStep : IServiceSimulationStep
{
    private readonly IReadOnlyList<IServiceSimulationStep> _steps;

    public CompositeStep(
        string name,
        params IServiceSimulationStep[] steps)
    {
        Name = name;
        _steps = steps;
    }

    public string Name { get; }

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        foreach (IServiceSimulationStep step in _steps)
        {
            await step.ExecuteAsync(context, cancellationToken);
        }
    }
}

/// <summary>
/// Runs a step repeatedly.
/// </summary>
public sealed class RepeatStep : IServiceSimulationStep
{
    private readonly int _count;
    private readonly IServiceSimulationStep _inner;

    public RepeatStep(int count, IServiceSimulationStep inner)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "Repeat count must be at least 1.");
        }

        _count = count;
        _inner = inner;
    }

    public string Name => $"repeat-{_count}-{_inner.Name}";

    public async Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < _count; i++)
        {
            await _inner.ExecuteAsync(context, cancellationToken);
        }
    }
}

