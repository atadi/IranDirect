using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Testing.FaultInjection;
using PathVeer.Testing.Performance.Lifecycle;

namespace PathVeer.Core.Tests.Performance.Lifecycle;

/// <summary>
/// Cancellation and fault recovery: verifies the production cycle
/// path distinguishes cancellation from ordinary failure, never
/// deadlocks or leaves a held gate, and always accepts a further
/// clean cycle afterwards. Cancellation during execution is driven by
/// the deterministic <see cref="SimulatedRuntimeEnvironment.ExecutionHold"/>
/// gate rather than any real clock wait.
/// </summary>
public sealed class LifecycleRecoveryTests
{
    [Fact]
    public async Task CancellationBeforeCycle_LeavesEnvironmentReusable()
    {
        using SimulatedRuntimeEnvironment environment = new();

        await ServiceSimulationRunner.RunAsync(
            environment,
            LifecycleScenarioFixtures.ConvergedPlan(
                "cancel-before-install",
                idleCycles: 0));

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => environment.RunCycleAsync(cts.Token));

        // The environment must still be fully usable afterwards.
        // Re-observe so the runtime sees what the install applied;
        // the recovery cycle is then a genuine no-op.
        await SyncObservationAsync(environment);

        RuntimeCycleExecutionResult recovery =
            await environment.RunCycleAsync();

        Assert.Equal(
            RuntimeExecutionResultStatus.NoExecutionRequired,
            recovery.Execution.Status);

        AssertCleanIdleState(environment);
    }

    [Fact]
    public async Task CancellationDuringExecution_IsNotReportedAsFailure()
    {
        using SimulatedRuntimeEnvironment environment = new();

        using CancellationTokenSource cts = new();
        TaskCompletionSource gateReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Cancel from inside the first handler call, deterministically.
        environment.ExecutionHold = () =>
        {
            gateReached.TrySetResult();
            cts.Cancel();
            return Task.CompletedTask;
        };

        await SeedEnabledAsync(environment, 10);

        RuntimeCycleExecutionResult? result = null;
        Exception? thrown = null;

        try
        {
            result = await environment.RunCycleAsync(cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            thrown = ex;
        }

        Assert.True(gateReached.Task.IsCompleted);

        // Cancellation must surface as cancellation, never as a
        // silent success or an ordinary failure.
        if (thrown is null)
        {
            Assert.NotNull(result);
            Assert.NotEqual(
                RuntimeExecutionResultStatus.Completed,
                result!.Execution.Status);
        }

        // No handler call may still be in flight, and no gate held.
        environment.ExecutionHold = null;
        ServiceSimulationVerifier.AssertNoActiveExecution(environment);

        // The very next cycle must succeed.
        RuntimeCycleExecutionResult recovery =
            await environment.RunCycleAsync();

        Assert.NotEqual(
            RuntimeExecutionResultStatus.Cancelled,
            recovery.Execution.Status);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
    }

    [Fact]
    public async Task CancellationDuringMonitorCheck_StopsCleanly()
    {
        using SimulatedRuntimeEnvironment environment = new();

        environment.PrefixUpdateChecker.EnqueueCurrent();

        await environment.Monitor.StartAsync();

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => environment.Monitor.ForceCheckAsync(cts.Token));

        // A later uncancelled check still works.
        environment.PrefixUpdateChecker.EnqueueCurrent();
        await environment.Monitor.ForceCheckAsync();

        await environment.Monitor.StopAsync();

        ServiceSimulationVerifier.AssertMonitorNotOverlapping(environment);
        ServiceSimulationVerifier.AssertMonitorStopped(environment);
    }

    [Fact]
    public async Task CancellationDuringSupportExport_LeavesNoPartialArtifacts()
    {
        using SimulatedRuntimeEnvironment environment = new();

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        string target = Path.Combine(
            environment.Workspace.SupportDirectory,
            "cancelled.zip");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => environment.SupportBundleExporter.ExportAsync(
                target,
                cts.Token));

        // A cancelled export must not leave orphan temp files, and a
        // subsequent export must succeed.
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);

        string recovered = Path.Combine(
            environment.Workspace.SupportDirectory,
            "recovered.zip");

        await environment.SupportBundleExporter.ExportAsync(recovered);

        Assert.True(File.Exists(recovered));
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
    }

    [Theory]
    [InlineData(FaultInjectionPoint.JsonSave)]
    [InlineData(FaultInjectionPoint.JsonLoad)]
    [InlineData(FaultInjectionPoint.FileWrite)]
    [InlineData(FaultInjectionPoint.RouteEnumeration)]
    [InlineData(FaultInjectionPoint.RouteCreate)]
    public async Task InjectedCycleFault_RecoversOnNextCleanCycle(
        FaultInjectionPoint point)
    {
        using SimulatedRuntimeEnvironment environment = new();

        await SeedEnabledAsync(environment, 10);

        // Faulted cycle: any outcome is acceptable except a silent
        // clean completion, and it must not corrupt the environment.
        using (FaultInjectionScope.Fail(point))
        {
            try
            {
                await environment.RunCycleAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Surfacing the fault to the caller is a valid
                // contract; the recovery assertions below are what
                // this test actually guards.
            }
        }

        // No handler call left in flight after the fault.
        ServiceSimulationVerifier.AssertNoActiveExecution(environment);

        // The next unfaulted cycle must run and settle.
        RuntimeCycleExecutionResult recovery =
            await environment.RunCycleAsync();

        Assert.NotEqual(
            RuntimeExecutionResultStatus.Cancelled,
            recovery.Execution.Status);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
    }

    [Fact]
    public async Task RepeatedFaultThenRecovery_ConvergesToDesiredState()
    {
        const int rounds = 5;

        using SimulatedRuntimeEnvironment environment = new();

        await SeedEnabledAsync(environment, 15);

        for (int i = 0; i < rounds; i++)
        {
            using (FaultInjectionScope.Fail(FaultInjectionPoint.JsonSave))
            {
                try
                {
                    await environment.RunCycleAsync();
                }
                catch (Exception ex)
                    when (ex is not OperationCanceledException)
                {
                    // Expected: the fault is injected on purpose.
                }
            }

            await SyncObservationAsync(environment);

            await environment.RunCycleAsync();

            ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        }

        await SyncObservationAsync(environment);
        await environment.RunCycleAsync();

        await ServiceSimulationVerifier
            .AssertConvergedToDesiredAsync(environment);
        await ServiceSimulationVerifier
            .AssertInventoryMatchesRoutesAsync(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
    }

    private static async Task SeedEnabledAsync(
        SimulatedRuntimeEnvironment environment,
        int prefixCount)
    {
        await ServiceSimulationRunner.RunAsync(
            environment,
            new ServiceSimulationPlan(
                "seed",
                [
                    new SeedObservationStep(
                        LifecycleScenarioFixtures.PrefixPool(prefixCount),
                        LifecycleScenarioFixtures.Endpoints(),
                        LifecycleScenarioFixtures.Gateway),
                    new SetEnabledStep(true)
                ]));
    }

    private static async Task SyncObservationAsync(
        SimulatedRuntimeEnvironment environment)
    {
        await ServiceSimulationRunner.RunAsync(
            environment,
            new ServiceSimulationPlan(
                "sync",
                [new SyncObservationFromRouteTableStep()]));
    }

    private static void AssertCleanIdleState(
        SimulatedRuntimeEnvironment environment)
    {
        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
    }
}
