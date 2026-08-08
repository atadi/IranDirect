using PathVeer.Core.Diagnostics;
using PathVeer.Core.Observability;
using PathVeer.Testing.Performance.Lifecycle;

namespace PathVeer.Core.Tests.Performance.Lifecycle;

/// <summary>
/// Snapshot and diagnostics loops: repeated snapshot captures through
/// the production provider must produce isolated, monotonic snapshots
/// without a single instance being shared, and repeated diagnostic runs
/// must stay stable and non-failing against the converged simulation.
/// </summary>
public sealed class SnapshotDiagnosticsLoopTests
{
    [Fact]
    public async Task SnapshotLoop_HundredCaptures_AreIsolatedAndMonotonic()
    {
        const int captures = 100;

        using SimulatedRuntimeEnvironment environment = new();

        await ServiceSimulationRunner.RunAsync(
            environment,
            LifecycleScenarioFixtures.ConvergedPlan(
                "snapshot-install",
                idleCycles: 0));

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan(
                    "snapshot-loop",
                    [
                        new SnapshotStep(captures)
                    ]));

        Assert.Equal(captures, result.Metrics.SnapshotsCaptured);
        Assert.Equal(0, result.Metrics.DiagnosticRuns);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
        ServiceSimulationVerifier.AssertPerfReportsValid(environment);

        RuntimeSnapshot first =
            await environment.SnapshotProvider.GetSnapshotAsync();
        RuntimeSnapshot second =
            await environment.SnapshotProvider.GetSnapshotAsync();

        ServiceSimulationVerifier.AssertSnapshotIsolated(first, second);
        Assert.True(
            first.CapturedAt <= second.CapturedAt,
            "Snapshot timestamps regressed.");
    }

    [Fact]
    public async Task DiagnosticsLoop_FiftyRuns_StayStableAndNonFailing()
    {
        const int runs = 50;

        using SimulatedRuntimeEnvironment environment = new();

        await ServiceSimulationRunner.RunAsync(
            environment,
            LifecycleScenarioFixtures.ConvergedPlan(
                "diagnostics-install",
                idleCycles: 0));

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan(
                    "diagnostics-loop",
                    [
                        new DiagnosticsStep(runs)
                    ]));

        Assert.Equal(runs, result.Metrics.DiagnosticRuns);
        Assert.Equal(0, result.Metrics.SnapshotsCaptured);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);

        DiagnosticReport report =
            await environment.DiagnosticRunner.RunAllAsync(
                CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal(4, report.Results.Count);
        Assert.DoesNotContain(
            report.Results,
            r => r.Status == DiagnosticStatus.Failed);
    }

    [Fact]
    public async Task SnapshotAfterFault_RecoversOnNextCapture()
    {
        using SimulatedRuntimeEnvironment environment = new();

        await ServiceSimulationRunner.RunAsync(
            environment,
            LifecycleScenarioFixtures.ConvergedPlan(
                "snapshot-fault-install",
                idleCycles: 0));

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan(
                    "snapshot-fault",
                    [
                        new FaultScopeStep(
                            PathVeer.Core.Testing.FaultInjection
                                .FaultInjectionPoint.SnapshotCapture,
                            new SnapshotStep(1)),
                        new SnapshotStep(1)
                    ]));

        Assert.Equal(1, result.Metrics.FaultScopesEntered);
        Assert.Equal(1, result.Metrics.RecoverableFailuresObserved);

        // The faulted capture throws inside SnapshotStep before
        // RecordSnapshot() runs, so it is counted as a recoverable
        // failure rather than a snapshot. Only the post-fault recovery
        // capture increments SnapshotsCaptured, which is what proves
        // the provider recovered on the very next call.
        Assert.Equal(1, result.Metrics.SnapshotsCaptured);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
    }
}
