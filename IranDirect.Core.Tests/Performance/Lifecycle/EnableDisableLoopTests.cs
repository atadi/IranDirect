using IranDirect.Testing.Performance.Lifecycle;

namespace IranDirect.Core.Tests.Performance.Lifecycle;

/// <summary>
/// Enable/disable convergence: toggling the production enable/disable
/// path must install and then fully remove every managed prefix route,
/// leave a clean inventory, and repeat identically across many loops.
/// </summary>
public sealed class EnableDisableLoopTests
{
    [Fact]
    public async Task EnableDisableLoop_FiveRounds_ConvergesEveryRound()
    {
        const int loops = 5;

        using SimulatedRuntimeEnvironment environment = new();

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                LifecycleScenarioFixtures.EnableDisablePlan(
                    "enable-disable-5",
                    loops: loops));

        int expectedMutating = 1 + (2 * loops);
        Assert.Equal(expectedMutating, result.Metrics.CyclesRun);
        Assert.Equal(expectedMutating, result.Metrics.MutatingCycles);
        Assert.Equal(0, result.Metrics.NoOpCycles);
        Assert.Equal(0, result.Metrics.FailedCycles);
        Assert.Equal(0, result.Metrics.CancelledCycles);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
        ServiceSimulationVerifier.AssertPerfReportsValid(environment);
        await ServiceSimulationVerifier.AssertConvergedToDesiredAsync(environment);
        await ServiceSimulationVerifier.AssertInventoryMatchesRoutesAsync(environment);

        // The plan ends in the ENABLED state, so the final install is
        // still on the platform and has no matching delete. Adds must
        // therefore exceed deletes by exactly the routes left installed.
        int remainingRoutes = environment.RouteTable.Snapshot().Count;

        Assert.Equal(
            environment.RouteTable.AddCalls - environment.RouteTable.DeleteCalls,
            remainingRoutes);
    }

    [Fact]
    public async Task DisableAfterConverged_RemovesAllManagedPrefixRoutes()
    {
        using SimulatedRuntimeEnvironment environment = new();

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan(
                    "disable-final",
                    [
                        new SeedObservationStep(
                            LifecycleScenarioFixtures.PrefixPool(25),
                            LifecycleScenarioFixtures.Endpoints(),
                            LifecycleScenarioFixtures.Gateway),
                        new EnableStep(),
                        new SyncObservationFromRouteTableStep(),
                        new RunCycleStep("post-enable-idle"),
                        new DisableStep()
                    ]));

        Assert.Equal(3, result.Metrics.CyclesRun);
        Assert.Equal(2, result.Metrics.MutatingCycles);
        Assert.Equal(1, result.Metrics.NoOpCycles);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
        await ServiceSimulationVerifier.AssertDisableStateAsync(environment);

        Assert.True(
            environment.RouteTable.DeleteCalls >= 1,
            "Disable never deleted any route batch.");
    }

    [Fact]
    public async Task ReenableAfterDisable_RestoresDesiredRoutesExactly()
    {
        using SimulatedRuntimeEnvironment environment = new();

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan(
                    "reenable",
                    [
                        new SeedObservationStep(
                            LifecycleScenarioFixtures.PrefixPool(25),
                            LifecycleScenarioFixtures.Endpoints(),
                            LifecycleScenarioFixtures.Gateway),
                        new EnableStep(),
                        new SyncObservationFromRouteTableStep(),
                        new DisableStep(),
                        new EnableStep(),
                        new SyncObservationFromRouteTableStep(),
                        new RunCycleStep("post-reenable-idle")
                    ]));

        Assert.Equal(4, result.Metrics.CyclesRun);
        Assert.Equal(3, result.Metrics.MutatingCycles);
        Assert.Equal(1, result.Metrics.NoOpCycles);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
        await ServiceSimulationVerifier.AssertConvergedToDesiredAsync(environment);
        await ServiceSimulationVerifier.AssertInventoryMatchesRoutesAsync(environment);
    }
}
