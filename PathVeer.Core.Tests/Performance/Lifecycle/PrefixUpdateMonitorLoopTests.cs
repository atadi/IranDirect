using PathVeer.Core.Prefixes;
using PathVeer.Testing.Performance.Lifecycle;

namespace PathVeer.Core.Tests.Performance.Lifecycle;

/// <summary>
/// Prefix update monitor loop: replays deterministic
/// Current / UpdateAvailable / Unknown / Failed sequences through the
/// production <see cref="PrefixUpdateMonitor"/> and asserts the
/// consecutive-failure counter accumulates and resets per the
/// production contract, that forced and scheduled checks never
/// overlap, and that the monitor always stops cleanly.
/// </summary>
public sealed class PrefixUpdateMonitorLoopTests
{
    [Fact]
    public async Task RepeatedStates_TransitionAndStopCleanly()
    {
        using SimulatedRuntimeEnvironment environment = new();

        environment.PrefixUpdateChecker.EnqueueCurrent();
        environment.PrefixUpdateChecker.EnqueueUpdateAvailable();
        environment.PrefixUpdateChecker.EnqueueUnknown();
        environment.PrefixUpdateChecker.EnqueueCurrent();

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan(
                    "monitor-states",
                    [
                        new MonitorStartStep(),
                        new MonitorForceCheckStep(4),
                        new MonitorStopStep()
                    ]));

        Assert.Equal(4, result.Metrics.MonitorChecks);

        PrefixUpdateMonitorSnapshot snapshot =
            environment.Monitor.GetSnapshot();

        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            snapshot.CurrentResult!.Status);

        // None of the four scripted outcomes is Failed, so the
        // consecutive-failure counter must still be zero.
        Assert.Equal(0, snapshot.ConsecutiveFailures);

        ServiceSimulationVerifier.AssertMonitorNotOverlapping(environment);
        ServiceSimulationVerifier.AssertMonitorStopped(environment);
    }

    [Fact]
    public async Task ConsecutiveFailures_AccumulateThenResetOnSuccess()
    {
        const int failures = 5;

        using SimulatedRuntimeEnvironment environment = new();

        for (int i = 0; i < failures; i++)
        {
            environment.PrefixUpdateChecker.EnqueueFailure(
                $"scripted failure {i}");
        }

        await environment.Monitor.StartAsync();

        for (int i = 0; i < failures; i++)
        {
            await environment.Monitor.ForceCheckAsync();

            // The counter must advance by exactly one per failure.
            Assert.Equal(
                i + 1,
                environment.Monitor.GetSnapshot().ConsecutiveFailures);
        }

        // A single success must reset the counter to zero.
        environment.PrefixUpdateChecker.EnqueueCurrent();
        await environment.Monitor.ForceCheckAsync();

        PrefixUpdateMonitorSnapshot snapshot =
            environment.Monitor.GetSnapshot();

        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            snapshot.CurrentResult!.Status);
        Assert.NotNull(snapshot.LastSuccessfulCheckAt);

        await environment.Monitor.StopAsync();

        ServiceSimulationVerifier.AssertMonitorNotOverlapping(environment);
        ServiceSimulationVerifier.AssertMonitorStopped(environment);
    }

    [Fact]
    public async Task UnknownStatus_CountsAsSuccessAndDoesNotIncrementFailures()
    {
        using SimulatedRuntimeEnvironment environment = new();

        environment.PrefixUpdateChecker.EnqueueFailure("first failure");
        environment.PrefixUpdateChecker.EnqueueUnknown();

        await environment.Monitor.StartAsync();

        await environment.Monitor.ForceCheckAsync();
        Assert.Equal(
            1,
            environment.Monitor.GetSnapshot().ConsecutiveFailures);

        // Production treats any non-Failed status (including Unknown)
        // as a success for counter purposes.
        await environment.Monitor.ForceCheckAsync();
        Assert.Equal(
            0,
            environment.Monitor.GetSnapshot().ConsecutiveFailures);

        await environment.Monitor.StopAsync();
        ServiceSimulationVerifier.AssertMonitorStopped(environment);
    }

    [Fact]
    public async Task LongTransitionSequence_NeverOverlapsAndStopsClean()
    {
        const int rounds = 120;

        using SimulatedRuntimeEnvironment environment = new();

        // Deterministic rotation across all four outcomes.
        for (int i = 0; i < rounds; i++)
        {
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
                        $"scripted failure {i}");
                    break;
            }
        }

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan(
                    "monitor-long",
                    [
                        new MonitorStartStep(),
                        new MonitorForceCheckStep(rounds),
                        new MonitorStopStep()
                    ]));

        Assert.Equal(rounds, result.Metrics.MonitorChecks);
        Assert.Equal(
            rounds,
            environment.PrefixUpdateChecker.CheckCount);

        // The final scripted outcome (index 119, 119 % 4 == 3) is a
        // failure, so exactly one consecutive failure is pending.
        Assert.Equal(
            1,
            environment.Monitor.GetSnapshot().ConsecutiveFailures);

        ServiceSimulationVerifier.AssertMonitorNotOverlapping(environment);
        ServiceSimulationVerifier.AssertMonitorStopped(environment);
    }

    [Fact]
    public async Task CheckerException_IsContainedAndMonitorKeepsRunning()
    {
        using SimulatedRuntimeEnvironment environment = new();

        environment.PrefixUpdateChecker.EnqueueException(
            "scripted checker explosion");
        environment.PrefixUpdateChecker.EnqueueCurrent();

        await environment.Monitor.StartAsync();

        // The production monitor must contain a throwing checker
        // rather than letting it escape to the caller.
        await environment.Monitor.ForceCheckAsync();

        // A later clean check still succeeds, proving no stuck state.
        await environment.Monitor.ForceCheckAsync();

        PrefixUpdateMonitorSnapshot snapshot =
            environment.Monitor.GetSnapshot();

        Assert.Equal(
            PrefixUpdateCheckStatus.Current,
            snapshot.CurrentResult!.Status);
        Assert.False(snapshot.Checking);

        await environment.Monitor.StopAsync();

        ServiceSimulationVerifier.AssertMonitorNotOverlapping(environment);
        ServiceSimulationVerifier.AssertMonitorStopped(environment);
    }
}
