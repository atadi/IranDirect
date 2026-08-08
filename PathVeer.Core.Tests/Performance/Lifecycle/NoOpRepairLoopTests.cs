using PathVeer.Core.Runtime.Execution;
using PathVeer.Testing.Performance.Lifecycle;

namespace PathVeer.Core.Tests.Performance.Lifecycle;

/// <summary>
/// The no-op repair loop: after an initial install the simulated
/// platform is stable, so every subsequent repair cycle must be a
/// true no-op. Asserts exact outcome counts (deterministic, not
/// scheduling-sensitive) and the stable end state.
/// </summary>
public sealed class NoOpRepairLoopTests
{
    [Fact]
    public async Task IdlePlatform_TwoFiftyRepairCycles_RemainNoOps()
    {
        using SimulatedRuntimeEnvironment environment = new();

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                LifecycleScenarioFixtures.ConvergedPlan(
                    "noop-250",
                    idleCycles: 250));

        Assert.Equal(251, result.Metrics.CyclesRun);
        Assert.Equal(250, result.Metrics.NoOpCycles);
        Assert.Equal(1, result.Metrics.MutatingCycles);
        Assert.Equal(0, result.Metrics.FailedCycles);
        Assert.Equal(0, result.Metrics.CancelledCycles);
        Assert.Equal(0, result.Metrics.PartiallyCompletedCycles);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
        ServiceSimulationVerifier.AssertPerfReportsValid(environment);
        await ServiceSimulationVerifier.AssertConvergedToDesiredAsync(environment);
        await ServiceSimulationVerifier.AssertInventoryMatchesRoutesAsync(environment);

        Assert.Equal(27, environment.RouteTable.AddCalls);
        Assert.Equal(0, environment.RouteTable.DeleteCalls);
    }

    [Fact]
    public async Task RestartFromPersistedState_ReinstallsRoutes_ThenStaysNoOp()
    {
        string[] prefixes = LifecycleScenarioFixtures.PrefixPool(25);
        string stateDirectory = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Lifecycle",
            $"noop-persisted-{Guid.NewGuid():N}");

        try
        {
            using (SimulatedRuntimeEnvironment environment = new())
            {
                ServiceSimulationRunResult install =
                    await ServiceSimulationRunner.RunAsync(
                        environment,
                        LifecycleScenarioFixtures.ConvergedPlan(
                            "noop-install",
                            idleCycles: 25));

                Assert.Equal(1, install.Metrics.MutatingCycles);

                Directory.CreateDirectory(stateDirectory);
                CopyDirectory(
                    environment.Workspace.PathValue,
                    stateDirectory);
            }

            SimulatedRuntimeEnvironment replay = new();
            CopyDirectory(stateDirectory, replay.Workspace.PathValue);

            try
            {
                ServiceSimulationRunResult replayRun =
                    await ServiceSimulationRunner.RunAsync(
                        replay,
                        new ServiceSimulationPlan(
                            "noop-replay",
                            [
                                new SeedObservationStep(
                                    prefixes,
                                    LifecycleScenarioFixtures.Endpoints(),
                                    LifecycleScenarioFixtures.Gateway),
                                new SyncObservationFromRouteTableStep(),
                                new RunCycleStep("replay-install"),
                                new SyncObservationFromRouteTableStep(),
                                new RunCycleStep("replay-idle")
                            ]));

                Assert.Equal(2, replayRun.Metrics.CyclesRun);
                Assert.Equal(1, replayRun.Metrics.MutatingCycles);
                Assert.Equal(1, replayRun.Metrics.NoOpCycles);
                Assert.Equal(27, replay.RouteTable.AddCalls);

                ServiceSimulationVerifier.AssertNoActiveExecution(replay);
                ServiceSimulationVerifier.AssertOperationCompleted(replay);
                ServiceSimulationVerifier.AssertNoPendingTempFiles(replay);
                await ServiceSimulationVerifier.AssertConvergedToDesiredAsync(replay);
                await ServiceSimulationVerifier.AssertInventoryMatchesRoutesAsync(replay);
            }
            finally
            {
                replay.Dispose();
            }
        }
        finally
        {
            if (Directory.Exists(stateDirectory))
            {
                Directory.Delete(stateDirectory, recursive: true);
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string directory in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(
                source,
                destination,
                StringComparison.OrdinalIgnoreCase));
        }

        foreach (string file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            string target = file.Replace(
                source,
                destination,
                StringComparison.OrdinalIgnoreCase);

            File.Copy(file, target, overwrite: true);
        }
    }
}
