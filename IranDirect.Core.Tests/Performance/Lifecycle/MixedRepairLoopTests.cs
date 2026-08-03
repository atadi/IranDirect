using IranDirect.Core.Routing;
using IranDirect.Core.Runtime;
using IranDirect.Core.Testing.FaultInjection;
using IranDirect.Testing.Performance.Lifecycle;

namespace IranDirect.Core.Tests.Performance.Lifecycle;

/// <summary>
/// Mixed repair loop: the desired prefix list grows one prefix per
/// iteration, a foreign (unowned) route drifts onto the platform, and
/// the route-create operation fails once under a scripted fault before
/// the next cycle repairs it. Asserts that the runtime converges to the
/// growing desired set, never touches foreign routes, and keeps the
/// inventory exactly aligned with the owned routes.
/// </summary>
public sealed class MixedRepairLoopTests
{
    [Fact]
    public async Task FaultyGrowth_ThreeRounds_RepairsAndIgnoresForeignDrift()
    {
        const int initialPrefixCount = 25;
        const int rounds = 3;

        string[] initialPrefixes =
            LifecycleScenarioFixtures.PrefixPool(initialPrefixCount);
        string[] driftPrefixes = new string[rounds];

        for (int i = 0; i < rounds; i++)
        {
            driftPrefixes[i] = $"10.99.{i}.0/24";
        }

        using SimulatedRuntimeEnvironment environment = new();

        List<IServiceSimulationStep> steps =
        [
            new SeedObservationStep(
                initialPrefixes,
                LifecycleScenarioFixtures.Endpoints(),
                LifecycleScenarioFixtures.Gateway),
            new SetEnabledStep(true),
            new SyncObservationFromRouteTableStep(),
            new RunCycleStep("install")
        ];

        for (int i = 0; i < rounds; i++)
        {
            int captured = i;

            steps.Add(new RotateObservationStep(index =>
            {
                ObservedVpnEndpoint[] endpoints =
                    LifecycleScenarioFixtures.Endpoints();
                ObservedVpnEndpoint[] rotated =
                    [.. endpoints, LifecycleScenarioFixtures.Endpoint(2 + captured)];

                return new ObservedRotation(
                    LifecycleScenarioFixtures.PrefixPool(
                        initialPrefixCount + captured + 1),
                    Endpoints: rotated,
                    Gateway: LifecycleScenarioFixtures.Gateway,
                    DriftRoutes:
                    [
                        LifecycleScenarioFixtures.Route(
                            driftPrefixes[captured],
                            "192.168.99.1",
                            interfaceIndex: 22,
                            metric: 5)
                    ]);
            }));

            steps.Add(new SyncObservationFromRouteTableStep());
            steps.Add(new FaultScopeStep(
                FaultInjectionPoint.RouteCreate,
                new RunCycleStep($"attempt-{i}")));
            steps.Add(new SyncObservationFromRouteTableStep());
            steps.Add(new RunCycleStep($"repair-{i}"));
        }

        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                new ServiceSimulationPlan("mixed-repair", steps));

        int expectedCycles = 1 + (2 * rounds);
        Assert.Equal(expectedCycles, result.Metrics.CyclesRun);
        Assert.Equal(1 + rounds, result.Metrics.MutatingCycles);
        Assert.Equal(rounds, result.Metrics.FailedCycles);
        Assert.Equal(0, result.Metrics.NoOpCycles);
        Assert.Equal(0, result.Metrics.CancelledCycles);
        Assert.Equal(rounds, result.Metrics.FaultScopesEntered);

        ServiceSimulationVerifier.AssertNoActiveExecution(environment);
        ServiceSimulationVerifier.AssertOperationCompleted(environment);
        ServiceSimulationVerifier.AssertNoPendingTempFiles(environment);
        ServiceSimulationVerifier.AssertPerfReportsValid(environment);

        await AssertRepairedAndForeignRoutesUntouchedAsync(
            environment,
            driftPrefixes);
    }

    private static async Task AssertRepairedAndForeignRoutesUntouchedAsync(
        SimulatedRuntimeEnvironment environment,
        IReadOnlyList<string> driftPrefixes)
    {
        IReadOnlyList<SystemRoute> table =
            environment.RouteTable.Snapshot();

        foreach (string prefix in environment.Observation.Prefixes)
        {
            string identity =
                LifecycleScenarioFixtures.PrefixRouteIdentity(prefix);

            Assert.Contains(
                table,
                route => ToIdentity(route) == identity);
        }

        foreach (string drift in driftPrefixes)
        {
            Assert.Contains(
                table,
                route =>
                    route.DestinationPrefix.Equals(
                        drift,
                        StringComparison.OrdinalIgnoreCase)
                    && route.NextHop.ToString() == "192.168.99.1");
        }

        RouteInventory inventory =
            await environment.RouteInventoryStore.LoadAsync();

        Assert.Equal(
            environment.Observation.Prefixes.Count,
            inventory.Routes.Count);

        foreach (RouteInventoryItem item in inventory.Routes)
        {
            Assert.DoesNotContain(
                driftPrefixes,
                drift => item.DestinationPrefix.Equals(
                    drift,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string ToIdentity(SystemRoute route) =>
        $"{route.DestinationPrefix}|{route.NextHop}|{route.InterfaceIndex}";
}
