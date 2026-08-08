using PathVeer.Core.Runtime.Execution;

namespace PathVeer.Testing.Performance.Lifecycle;

/// <summary>
/// Shared state passed to every simulation step: the wired
/// environment, the run metrics, and the sampling hook.
/// </summary>
public sealed class ServiceSimulationContext
{
    internal ServiceSimulationContext(
        SimulatedRuntimeEnvironment environment,
        ServiceSimulationMetrics metrics,
        ServiceSimulationSampler sampler)
    {
        Environment = environment;
        Metrics = metrics;
        Sampler = sampler;
    }

    public SimulatedRuntimeEnvironment Environment { get; }

    public ServiceSimulationMetrics Metrics { get; }

    public ServiceSimulationSampler Sampler { get; }

    public RuntimeExecutionResult? LastCycleResult { get; set; }
}

/// <summary>
/// A single deterministic action within a service simulation.
/// </summary>
public interface IServiceSimulationStep
{
    string Name { get; }

    Task ExecuteAsync(
        ServiceSimulationContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// A named, ordered collection of simulation steps.
/// </summary>
public sealed class ServiceSimulationScenario
{
    public ServiceSimulationScenario(
        string name,
        IReadOnlyList<IServiceSimulationStep> steps,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        Description = description ?? "";
    }

    public string Name { get; }

    public string Description { get; }

    public IReadOnlyList<IServiceSimulationStep> Steps { get; }
}

/// <summary>
/// A validated plan produced from a <see cref="ServiceSimulationScenario"/>.
/// Validation rejects empty names and empty step lists so a plan can
/// never accidentally simulate "nothing".
/// </summary>
public sealed class ServiceSimulationPlan
{
    public ServiceSimulationPlan(
        string name,
        IReadOnlyList<IServiceSimulationStep> steps,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (steps is null || steps.Count == 0)
        {
            throw new ArgumentException(
                "A simulation plan must contain at least one step.",
                nameof(steps));
        }

        Name = name;
        Steps = steps;
        Description = description ?? "";
    }

    public string Name { get; }

    public string Description { get; }

    public IReadOnlyList<IServiceSimulationStep> Steps { get; }

    public static ServiceSimulationPlan FromScenario(
        ServiceSimulationScenario scenario) =>
        new(scenario.Name, scenario.Steps, scenario.Description);
}

/// <summary>
/// Execution options for a <see cref="ServiceSimulationRunner"/> run.
/// </summary>
public sealed class ServiceSimulationRunOptions
{
    /// <summary>
    /// When greater than zero, a resource sample is captured every
    /// N sampling boundaries (each cycle-like step counts as one
    /// boundary). Baseline and final samples are always captured.
    /// </summary>
    public int SampleEvery { get; set; }

    /// <summary>
    /// Captures a final resource sample after the last step.
    /// Defaults to true.
    /// </summary>
    public bool CaptureFinalSample { get; set; } = true;
}

/// <summary>
/// The result of a simulation run: metrics, resource samples, and
/// per-metric trend lines.
/// </summary>
public sealed class ServiceSimulationRunResult
{
    public required string PlanName { get; init; }

    public required ServiceSimulationMetrics Metrics { get; init; }

    public required IReadOnlyList<ResourceSample> Samples { get; init; }

    public required IReadOnlyList<ResourceTrendLine> Trends { get; init; }

    public RuntimeExecutionResult? LastCycleResult { get; init; }
}

/// <summary>
/// Aggregated counters and outcome sequences for a simulation run.
/// Safe to increment from parallel execution (the executor runs
/// prefix route groups concurrently).
/// </summary>
public sealed class ServiceSimulationMetrics
{
    private readonly object _gate = new();
    private int _cyclesRun;
    private int _noOpCycles;
    private int _mutatingCycles;
    private int _failedCycles;
    private int _cancelledCycles;
    private int _partiallyCompletedCycles;
    private int _snapshotsCaptured;
    private int _diagnosticRuns;
    private int _previewsBuilt;
    private int _monitorChecks;
    private int _customRouteResolutions;
    private int _supportBundlesExported;
    private int _recoverableFailuresObserved;
    private int _faultScopesEntered;
    private int _routeAddCalls;
    private int _routeDeleteCalls;

    private readonly List<RuntimeExecutionResultStatus> _cycleSequence = [];

    public int CyclesRun => Volatile.Read(ref _cyclesRun);

    public int NoOpCycles => Volatile.Read(ref _noOpCycles);

    public int MutatingCycles => Volatile.Read(ref _mutatingCycles);

    public int FailedCycles => Volatile.Read(ref _failedCycles);

    public int CancelledCycles => Volatile.Read(ref _cancelledCycles);

    public int PartiallyCompletedCycles =>
        Volatile.Read(ref _partiallyCompletedCycles);

    public int SnapshotsCaptured => Volatile.Read(ref _snapshotsCaptured);

    public int DiagnosticRuns => Volatile.Read(ref _diagnosticRuns);

    public int PreviewsBuilt => Volatile.Read(ref _previewsBuilt);

    public int MonitorChecks => Volatile.Read(ref _monitorChecks);

    public int CustomRouteResolutions =>
        Volatile.Read(ref _customRouteResolutions);

    public int SupportBundlesExported =>
        Volatile.Read(ref _supportBundlesExported);

    public int RecoverableFailuresObserved =>
        Volatile.Read(ref _recoverableFailuresObserved);

    public int FaultScopesEntered =>
        Volatile.Read(ref _faultScopesEntered);

    public int RouteAddCalls => Volatile.Read(ref _routeAddCalls);

    public int RouteDeleteCalls => Volatile.Read(ref _routeDeleteCalls);

    public IReadOnlyList<RuntimeExecutionResultStatus> CycleResultSequence
    {
        get
        {
            lock (_gate)
            {
                return _cycleSequence.ToArray();
            }
        }
    }

    public void RecordCycle(RuntimeExecutionResult result)
    {
        Interlocked.Increment(ref _cyclesRun);

        switch (result.Status)
        {
            case RuntimeExecutionResultStatus.NoExecutionRequired:
                Interlocked.Increment(ref _noOpCycles);
                break;
            case RuntimeExecutionResultStatus.Completed:
                Interlocked.Increment(ref _mutatingCycles);
                break;
            case RuntimeExecutionResultStatus.Failed:
                Interlocked.Increment(ref _failedCycles);
                break;
            case RuntimeExecutionResultStatus.Cancelled:
                Interlocked.Increment(ref _cancelledCycles);
                break;
            case RuntimeExecutionResultStatus.PartiallyCompleted:
                Interlocked.Increment(ref _partiallyCompletedCycles);
                break;
        }

        lock (_gate)
        {
            _cycleSequence.Add(result.Status);
        }
    }

    public void RecordRouteCallCounts(SimulatedRouteTable table)
    {
        Interlocked.Exchange(ref _routeAddCalls, table.AddCalls);
        Interlocked.Exchange(ref _routeDeleteCalls, table.DeleteCalls);
    }

    public void RecordSnapshot() =>
        Interlocked.Increment(ref _snapshotsCaptured);

    public void RecordDiagnosticRun() =>
        Interlocked.Increment(ref _diagnosticRuns);

    public void RecordPreview() =>
        Interlocked.Increment(ref _previewsBuilt);

    public void RecordMonitorCheck() =>
        Interlocked.Increment(ref _monitorChecks);

    public void RecordCustomRouteResolution() =>
        Interlocked.Increment(ref _customRouteResolutions);

    public void RecordSupportBundleExport() =>
        Interlocked.Increment(ref _supportBundlesExported);

    public void RecordRecoverableFailure() =>
        Interlocked.Increment(ref _recoverableFailuresObserved);

    public void RecordFaultScopeEntered() =>
        Interlocked.Increment(ref _faultScopesEntered);
}
