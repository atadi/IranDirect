using System.Net;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Testing.Performance.Lifecycle;

namespace PathVeer.Core.Tests.Performance.Lifecycle;

/// <summary>
/// Shared deterministic inputs for the lifecycle simulations: prefix
/// pools, VPN endpoints, the direct gateway, and plan builders. All
/// prefixes/endpoints are RFC 5737 / private ranges so nothing can
/// collide with a real interface.
/// </summary>
internal static class LifecycleScenarioFixtures
{
    public const int EndpointCount = 2;
    public const int DefaultPrefixCount = 25;

    public static ObservedDirectGateway Gateway { get; } = new()
    {
        Address = "192.168.1.1",
        InterfaceIndex = 11,
        InterfaceName = "Ethernet"
    };

    public static string[] PrefixPool(int count)
    {
        string[] prefixes = new string[count];
        for (int i = 0; i < count; i++)
        {
            prefixes[i] = $"10.{i % 256}.{(i / 256) % 256}.0/24";
        }
        return prefixes;
    }

    public static ObservedVpnEndpoint[] Endpoints(
        int count = EndpointCount)
    {
        ObservedVpnEndpoint[] endpoints = new ObservedVpnEndpoint[count];
        for (int i = 0; i < count; i++)
        {
            endpoints[i] = new ObservedVpnEndpoint
            {
                Host = $"endpoint-{i}.vpn.example",
                Address = $"10.200.{(i % 256)}.1",
                Port = 1194,
                Protocol = "udp"
            };
        }
        return endpoints;
    }

    public static ObservedVpnEndpoint Endpoint(int index) =>
        Endpoints(index + 1)[index];

    public static string PrefixRouteIdentity(string prefix) =>
        $"{prefix}|{Gateway.Address}|{Gateway.InterfaceIndex}";

    public static string EndpointRouteIdentity(string address) =>
        $"{address}/32|{Gateway.Address}|{Gateway.InterfaceIndex}";

    public static ObservedRoute Route(
        string destinationPrefix,
        string nextHop,
        uint interfaceIndex,
        int metric = 5) =>
        new()
        {
            DestinationPrefix = destinationPrefix,
            NextHop = nextHop,
            InterfaceIndex = interfaceIndex,
            Metric = metric
        };

    /// <summary>
    /// Builds a deterministic "install once, then idle" plan:
    /// seed observation, enable, install one cycle, then repeat
    /// idle cycles against a re-synced observation.
    /// </summary>
    public static ServiceSimulationPlan ConvergedPlan(
        string name,
        int prefixCount = DefaultPrefixCount,
        int endpointCount = EndpointCount,
        int idleCycles = 250)
    {
        string[] prefixes = PrefixPool(prefixCount);

        List<IServiceSimulationStep> steps =
        [
            new SeedObservationStep(
                prefixes,
                Endpoints(endpointCount),
                Gateway),
            new SetEnabledStep(true),
            new SyncObservationFromRouteTableStep(),
            new RunCycleStep("install")
        ];

        // idleCycles: 0 is a valid "install only" plan; RepeatStep
        // requires a count of at least 1, so only append the idle loop
        // when at least one idle cycle was requested.
        if (idleCycles > 0)
        {
            steps.Add(
                new RepeatStep(
                    idleCycles,
                    new CompositeStep(
                        "idle",
                        new SyncObservationFromRouteTableStep(),
                        new RunCycleStep("idle-cycle"))));
        }

        return new ServiceSimulationPlan(name, steps);
    }

    public static ServiceSimulationPlan EnableDisablePlan(
        string name,
        int prefixCount = DefaultPrefixCount,
        int endpointCount = EndpointCount,
        int loops = 5)
    {
        string[] prefixes = PrefixPool(prefixCount);

        List<IServiceSimulationStep> steps =
        [
            new SeedObservationStep(
                prefixes,
                Endpoints(endpointCount),
                Gateway),
            new EnableStep(),
            new SyncObservationFromRouteTableStep()
        ];

        for (int i = 0; i < loops; i++)
        {
            steps.Add(new DisableStep());
            steps.Add(new SyncObservationFromRouteTableStep());
            steps.Add(new EnableStep());
            steps.Add(new SyncObservationFromRouteTableStep());
        }

        return new ServiceSimulationPlan(name, steps);
    }

    public static async Task<RuntimeExecutionResult> RunAsync(
        SimulatedRuntimeEnvironment environment,
        ServiceSimulationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ServiceSimulationRunResult result =
            await ServiceSimulationRunner.RunAsync(
                environment,
                plan,
                cancellationToken: cancellationToken);

        Assert.NotNull(result);
        Assert.Equal(plan.Name, result.PlanName);
        Assert.NotEmpty(result.Samples);
        return result.LastCycleResult!;
    }
}
