using System.Net;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Observability;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Testing.Performance.Lifecycle;

namespace PathVeer.Core.Tests.Performance.Lifecycle;

/// <summary>
/// The combined operational loop: a representative service cycle that
/// observes the runtime, plans and executes (or no-ops), captures a
/// runtime snapshot, runs diagnostics, builds an execution preview,
/// checks the prefix update monitor, resolves custom routes, and
/// periodically exports a support bundle. Everything runs on the
/// simulated clock against fakes, so no real route table, network,
/// DNS, HTTP, file profile, or IPC endpoint is touched.
/// </summary>
public sealed class CombinedOperationalLoopTests
{
    private const string Domain = "combined-host.example";

    [Fact]
    public async Task CombinedLoop_HundredCycles_StaysConsistent()
    {
        const int cycles = 100;

        using SimulatedRuntimeEnvironment environment = new();

        await SeedCustomRouteAsync(environment);
        SeedMonitorScript(environment, cycles + 8);

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                BuildCombinedPlan("combined-100", cycles),
                new ServiceSimulationRunOptions { SampleEvery = 25 });

        // One install cycle plus one idle cycle per loop.
        Assert.Equal(cycles + 1, result.Metrics.CyclesRun);
        Assert.Equal(1, result.Metrics.MutatingCycles);
        Assert.Equal(cycles, result.Metrics.NoOpCycles);
        Assert.Equal(0, result.Metrics.FailedCycles);
        Assert.Equal(0, result.Metrics.CancelledCycles);

        Assert.Equal(cycles, result.Metrics.SnapshotsCaptured);
        Assert.Equal(cycles, result.Metrics.DiagnosticRuns);
        Assert.Equal(cycles, result.Metrics.PreviewsBuilt);
        Assert.Equal(cycles, result.Metrics.MonitorChecks);
        Assert.Equal(cycles, result.Metrics.CustomRouteResolutions);

        // Bundles are exported on every tenth cycle.
        Assert.Equal(cycles / 10, result.Metrics.SupportBundlesExported);

        // Informational resource samples, not pass/fail gates.
        Assert.NotEmpty(result.Samples);
        Assert.NotEmpty(result.Trends);

        await AssertStableEndStateAsync(environment);
    }

    [Fact]
    public async Task TwoIndependentSimulations_DoNotInterfere()
    {
        const int cycles = 20;

        using SimulatedRuntimeEnvironment first = new();
        using SimulatedRuntimeEnvironment second = new();

        await SeedCustomRouteAsync(first);
        await SeedCustomRouteAsync(second);
        SeedMonitorScript(first, cycles + 8);
        SeedMonitorScript(second, cycles + 8);

        ServiceSimulationRunResult firstResult =
            await ServiceSimulationRunner.RunAsync(
                first,
                BuildCombinedPlan("combined-first", cycles));

        ServiceSimulationRunResult secondResult =
            await ServiceSimulationRunner.RunAsync(
                second,
                BuildCombinedPlan("combined-second", cycles));

        Assert.Equal(
            firstResult.Metrics.CyclesRun,
            secondResult.Metrics.CyclesRun);
        Assert.Equal(
            firstResult.Metrics.NoOpCycles,
            secondResult.Metrics.NoOpCycles);
        Assert.Equal(
            firstResult.Metrics.SnapshotsCaptured,
            secondResult.Metrics.SnapshotsCaptured);

        Assert.NotEqual(
            first.Workspace.PathValue,
            second.Workspace.PathValue);

        await AssertStableEndStateAsync(first);
        await AssertStableEndStateAsync(second);
    }

    [Fact]
    public async Task RepeatedSnapshots_AreCurrentAndIsolated()
    {
        using SimulatedRuntimeEnvironment environment = new();

        await ServiceSimulationRunner.RunAsync(
            environment,
            LifecycleScenarioFixtures.ConvergedPlan(
                "combined-snapshot-install",
                idleCycles: 0));

        RuntimeSnapshot first =
            await environment.SnapshotProvider.GetSnapshotAsync();

        environment.Time.Advance(TimeSpan.FromMinutes(5));

        RuntimeSnapshot second =
            await environment.SnapshotProvider.GetSnapshotAsync();

        // Distinct instances, and the newer capture carries the newer
        // deterministic timestamp.
        ServiceSimulationVerifier.AssertSnapshotIsolated(first, second);
        Assert.True(second.CapturedAt > first.CapturedAt);
    }

    [Fact]
    public async Task DiagnosticsAcrossCycles_RemainRegistrationOrdered()
    {
        const int runs = 10;

        using SimulatedRuntimeEnvironment environment = new();

        string[]? expectedOrder = null;

        for (int i = 0; i < runs; i++)
        {
            DiagnosticReport report =
                await environment.DiagnosticRunner.RunAllAsync(
                    CancellationToken.None);

            string[] order = report.Results
                .Select(entry => entry.Id)
                .ToArray();

            expectedOrder ??= order;

            // Registration order must be identical on every run.
            Assert.Equal(expectedOrder, order);

            environment.Time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.NotNull(expectedOrder);
        Assert.NotEmpty(expectedOrder!);
    }

    private static ServiceSimulationPlan BuildCombinedPlan(
        string name,
        int cycles)
    {
        List<IServiceSimulationStep> steps =
        [
            new SeedObservationStep(
                LifecycleScenarioFixtures.PrefixPool(25),
                LifecycleScenarioFixtures.Endpoints(),
                LifecycleScenarioFixtures.Gateway),
            new SetEnabledStep(true),
            new SyncObservationFromRouteTableStep(),
            new RunCycleStep("combined-install")
        ];

        for (int i = 0; i < cycles; i++)
        {
            List<IServiceSimulationStep> cycleSteps =
            [
                new SyncObservationFromRouteTableStep(),
                new RunCycleStep($"combined-cycle-{i}"),
                new SnapshotStep(),
                new DiagnosticsStep(),
                new PreviewStep(),
                new MonitorForceCheckStep(),
                new ResolveCustomRoutesStep(),
                new AdvanceTimeStep(TimeSpan.FromMinutes(1))
            ];

            // Periodic support bundle export every tenth cycle.
            if (i % 10 == 9)
            {
                cycleSteps.Add(
                    new ExportSupportBundleStep(1, $"combined-{i}"));
            }

            steps.Add(
                new CompositeStep($"combined-{i}", [.. cycleSteps]));
        }

        steps.Add(new MonitorStopStep());

        return new ServiceSimulationPlan(name, steps);
    }

    private static async Task SeedCustomRouteAsync(
        SimulatedRuntimeEnvironment environment)
    {
        await environment.CustomRouteService.AddAsync(
            CustomRouteEntryType.Domain,
            Domain);

        environment.DnsResolver.SetAddresses(
            Domain,
            IPAddress.Parse("203.0.113.77"));
    }

    private static void SeedMonitorScript(
        SimulatedRuntimeEnvironment environment,
        int count)
    {
        for (int i = 0; i < count; i++)
        {
            environment.PrefixUpdateChecker.EnqueueCurrent();
        }
    }

    private static async Task AssertStableEndStateAsync(
        SimulatedRuntimeEnvironment environment)
    {
        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
        ServiceSimulationVerifier.AssertPerfReportsValid(environment);
        ServiceSimulationVerifier.AssertMonitorNotOverlapping(environment);
        ServiceSimulationVerifier.AssertMonitorStopped(environment);

        await ServiceSimulationVerifier
            .AssertConvergedToDesiredAsync(environment);
        await ServiceSimulationVerifier
            .AssertInventoryMatchesRoutesAsync(environment);
        await ServiceSimulationVerifier
            .AssertDnsCacheRecordCountAsync(environment, Domain, 1);
    }
}
