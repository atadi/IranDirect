using System.IO.Compression;
using System.Net;
using IranDirect.Core.CustomRoutes;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Testing.Performance.Lifecycle;

namespace IranDirect.Core.Tests.Performance.Lifecycle;

/// <summary>
/// Long-running lifecycle stress simulations. These carry both the
/// shared Stress category and an Area=Lifecycle trait so they can be
/// isolated from the executor and persistence stress suites:
///
///   dotnet test --filter "Category=Stress&amp;Area=Lifecycle"
///
/// Every scenario runs on the simulated clock against fakes, so cycle
/// counts rather than elapsed time define the workload. Resource
/// samples are collected for information only — there are no strict
/// memory, handle, or timing gates here.
/// </summary>
[Trait("Category", "Stress")]
[Trait("Area", "Lifecycle")]
public sealed class LifecycleSimulationStressTests
{
    private const string Domain = "stress-host.example";

    [Fact]
    public async Task NoOpRepair_FiveThousandCycles_RemainNoOps()
    {
        const int cycles = 5_000;

        using SimulatedRuntimeEnvironment environment = new();

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                LifecycleScenarioFixtures.ConvergedPlan(
                    "stress-noop-5000",
                    idleCycles: cycles),
                new ServiceSimulationRunOptions { SampleEvery = 500 });

        // Exactly one mutating install, everything after is a no-op.
        Assert.Equal(cycles + 1, result.Metrics.CyclesRun);
        Assert.Equal(1, result.Metrics.MutatingCycles);
        Assert.Equal(cycles, result.Metrics.NoOpCycles);
        Assert.Equal(0, result.Metrics.FailedCycles);
        Assert.Equal(0, result.Metrics.CancelledCycles);

        // Route inventory must not grow across equivalent cycles. The
        // executor issues one Add per route, so the install accounts
        // for every call (25 prefixes + 2 endpoints) and the 5,000
        // no-op cycles add nothing and delete nothing.
        Assert.Equal(27, environment.RouteTable.AddCalls);
        Assert.Equal(0, environment.RouteTable.DeleteCalls);
        Assert.Equal(27, environment.RouteTable.Snapshot().Count);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
        ServiceSimulationVerifier.AssertPerfReportsValid(environment);

        await ServiceSimulationVerifier
            .AssertConvergedToDesiredAsync(environment);
        await ServiceSimulationVerifier
            .AssertInventoryMatchesRoutesAsync(environment);
    }

    [Fact]
    public async Task CombinedOperational_OneThousandCycles_StayConsistent()
    {
        const int cycles = 1_000;

        using SimulatedRuntimeEnvironment environment = new();

        await environment.CustomRouteService.AddAsync(
            CustomRouteEntryType.Domain,
            Domain);
        environment.DnsResolver.SetAddresses(
            Domain,
            IPAddress.Parse("203.0.113.90"));

        for (int i = 0; i < cycles + 8; i++)
        {
            environment.PrefixUpdateChecker.EnqueueCurrent();
        }

        List<IServiceSimulationStep> steps =
        [
            new SeedObservationStep(
                LifecycleScenarioFixtures.PrefixPool(25),
                LifecycleScenarioFixtures.Endpoints(),
                LifecycleScenarioFixtures.Gateway),
            new SetEnabledStep(true),
            new SyncObservationFromRouteTableStep(),
            new RunCycleStep("stress-install"),
            new RepeatStep(
                cycles,
                new CompositeStep(
                    "stress-combined",
                    new SyncObservationFromRouteTableStep(),
                    new RunCycleStep("stress-cycle"),
                    new SnapshotStep(),
                    new MonitorForceCheckStep(),
                    new ResolveCustomRoutesStep(),
                    new AdvanceTimeStep(TimeSpan.FromMinutes(1)))),
            new MonitorStopStep()
        ];

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan("stress-combined-1000", steps),
                new ServiceSimulationRunOptions { SampleEvery = 100 });

        Assert.Equal(cycles + 1, result.Metrics.CyclesRun);
        Assert.Equal(cycles, result.Metrics.NoOpCycles);
        Assert.Equal(cycles, result.Metrics.SnapshotsCaptured);
        Assert.Equal(cycles, result.Metrics.MonitorChecks);
        Assert.Equal(cycles, result.Metrics.CustomRouteResolutions);
        Assert.Equal(0, result.Metrics.FailedCycles);

        // Growth invariants: one DNS record per domain, and no route
        // churn after the install (one Add per installed route).
        await ServiceSimulationVerifier
            .AssertDnsCacheRecordCountAsync(environment, Domain, 1);
        Assert.Equal(27, environment.RouteTable.AddCalls);
        Assert.Equal(0, environment.RouteTable.DeleteCalls);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertMonitorNotOverlapping(environment);
        ServiceSimulationVerifier.AssertMonitorStopped(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
        ServiceSimulationVerifier.AssertPerfReportsValid(environment);

        await ServiceSimulationVerifier
            .AssertConvergedToDesiredAsync(environment);
        await ServiceSimulationVerifier
            .AssertInventoryMatchesRoutesAsync(environment);
    }

    [Fact]
    public async Task SnapshotAndDiagnostics_OneThousandCycles_StayIsolated()
    {
        const int cycles = 1_000;

        using SimulatedRuntimeEnvironment environment = new();

        await ServiceSimulationRunner.RunAsync(
            environment,
            LifecycleScenarioFixtures.ConvergedPlan(
                "stress-snapshot-install",
                idleCycles: 0));

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan(
                    "stress-snapshot-1000",
                    [
                        new RepeatStep(
                            cycles,
                            new CompositeStep(
                                "capture",
                                new SnapshotStep(),
                                new DiagnosticsStep(),
                                new PreviewStep(),
                                new AdvanceTimeStep(
                                    TimeSpan.FromSeconds(30))))
                    ]),
                new ServiceSimulationRunOptions { SampleEvery = 100 });

        Assert.Equal(cycles, result.Metrics.SnapshotsCaptured);
        Assert.Equal(cycles, result.Metrics.DiagnosticRuns);
        Assert.Equal(cycles, result.Metrics.PreviewsBuilt);

        // Captures remain isolated instances with advancing stamps.
        RuntimeSnapshotPair pair = await CapturePairAsync(environment);
        ServiceSimulationVerifier.AssertSnapshotIsolated(
            pair.First,
            pair.Second);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
    }

    [Fact]
    public async Task SupportBundles_FiveHundredExports_AllValid()
    {
        const int exports = 500;

        using SimulatedRuntimeEnvironment environment = new();

        await ServiceSimulationRunner.RunAsync(
            environment,
            LifecycleScenarioFixtures.ConvergedPlan(
                "stress-support-install",
                idleCycles: 0));

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan(
                    "stress-support-500",
                    [new ExportSupportBundleStep(exports)]),
                new ServiceSimulationRunOptions { SampleEvery = 100 });

        Assert.Equal(exports, result.Metrics.SupportBundlesExported);

        string[] bundles = Directory.GetFiles(
            environment.Workspace.SupportDirectory,
            "*.zip");

        Assert.Equal(exports, bundles.Length);

        // Spot-check archives across the run rather than opening all
        // 500, keeping the assertion meaningful but bounded.
        foreach (int index in new[] { 0, exports / 2, exports - 1 })
        {
            string path = Path.Combine(
                environment.Workspace.SupportDirectory,
                $"bundle-{index}.zip");

            using ZipArchive archive = ZipFile.OpenRead(path);
            Assert.NotEmpty(archive.Entries);
        }

        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
    }

    [Fact]
    public async Task MonitorAndDns_ExtendedTransitionSequence_StayBounded()
    {
        const int rounds = 500;

        using SimulatedRuntimeEnvironment environment = new();

        await environment.CustomRouteService.AddAsync(
            CustomRouteEntryType.Domain,
            Domain);

        CustomRouteDnsCacheOptions options = new();

        for (int i = 0; i < rounds; i++)
        {
            // Deterministic monitor outcome rotation.
            switch (i % 4)
            {
                case 0:
                    environment.PrefixUpdateChecker.EnqueueCurrent();
                    break;
                case 1:
                    environment.PrefixUpdateChecker
                        .EnqueueUpdateAvailable();
                    break;
                case 2:
                    environment.PrefixUpdateChecker.EnqueueUnknown();
                    break;
                default:
                    environment.PrefixUpdateChecker.EnqueueFailure(
                        $"stress failure {i}");
                    break;
            }
        }

        await environment.Monitor.StartAsync();

        for (int i = 0; i < rounds; i++)
        {
            bool dnsFailing = i % 5 == 4;

            environment.DnsResolver.SetFailing(Domain, dnsFailing);

            if (!dnsFailing)
            {
                environment.DnsResolver.SetAddresses(
                    Domain,
                    IPAddress.Parse($"203.0.113.{100 + (i % 20)}"));
            }

            await environment.Monitor.ForceCheckAsync();

            environment.Time.Advance(
                options.DnsCacheDuration + TimeSpan.FromMinutes(1));

            await environment.CustomRouteResolver.ResolveAsync();
        }

        await environment.Monitor.StopAsync();

        // Forced checks are not the only source of checks: advancing
        // the simulated clock past the monitor's scheduled interval
        // also fires timer-driven checks. So the total is at least the
        // forced count, and the non-overlap invariant below is what
        // guarantees forced and scheduled checks never ran together.
        Assert.True(
            environment.PrefixUpdateChecker.CheckCount >= rounds,
            $"Expected at least {rounds} checks, saw " +
            $"{environment.PrefixUpdateChecker.CheckCount}.");

        // Bounded growth: still exactly one cache record for the
        // domain after 500 refresh/failure transitions.
        await ServiceSimulationVerifier
            .AssertDnsCacheRecordCountAsync(environment, Domain, 1);

        ServiceSimulationVerifier.AssertMonitorNotOverlapping(environment);
        ServiceSimulationVerifier.AssertMonitorStopped(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
    }

    private static async Task<RuntimeSnapshotPair> CapturePairAsync(
        SimulatedRuntimeEnvironment environment)
    {
        Core.Observability.RuntimeSnapshot first =
            await environment.SnapshotProvider.GetSnapshotAsync();

        environment.Time.Advance(TimeSpan.FromMinutes(1));

        Core.Observability.RuntimeSnapshot second =
            await environment.SnapshotProvider.GetSnapshotAsync();

        return new RuntimeSnapshotPair(first, second);
    }

    private sealed record RuntimeSnapshotPair(
        Core.Observability.RuntimeSnapshot First,
        Core.Observability.RuntimeSnapshot Second);
}
