using PathVeer.Core.Configuration;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Reconciliation;
using PathVeer.Testing.Performance.Workloads;

namespace PathVeer.Core.Tests.Performance.Workloads;

public sealed class RuntimeWorkloadPlannerIntegrationTests
{
    private readonly RuntimeWorkloadGenerator _generator = new();
    private readonly RuntimeChangeSetPlanner _planner = new();

    public static IEnumerable<object[]> PlannerScenariosAndSizes() =>
        from scenario in Enum.GetValues<RuntimeWorkloadScenario>()
        from size in new[]
        {
            RuntimeWorkloadSize.Scale1K,
            RuntimeWorkloadSize.Scale50K
        }
        select new object[] { scenario, size };

    [Theory]
    [MemberData(nameof(PlannerScenariosAndSizes))]
    public void Planner_ChangeCounts_MatchWorkloadExpectedCounts(
        RuntimeWorkloadScenario scenario,
        RuntimeWorkloadSize size)
    {
        RuntimeWorkload workload = _generator.Generate(scenario, size);

        RuntimeChangeSet result = _planner.Plan(
            BuildSnapshot(workload),
            BuildOwnership(workload));

        var observedIdentities = workload.ObservedRoutes
            .Select(route => route.Identity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desiredPrefixIdentities = workload.DesiredPrefixes
            .Select(route => route.Identity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desiredEndpointIdentities = workload.DesiredEndpointRoutes
            .Select(route => route.Identity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int expectedPrefixAdds = workload.DesiredPrefixes
            .Select(route => route.Identity)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(identity => !observedIdentities.Contains(identity));
        int expectedEndpointAdds = workload.DesiredEndpointRoutes
            .Select(route => route.Identity)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(identity => !observedIdentities.Contains(identity));
        int expectedPrefixRemoves = workload.RouteInventory.Routes
            .Select(item => item.Identity)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(identity =>
                !desiredPrefixIdentities.Contains(identity) &&
                observedIdentities.Contains(identity));
        int expectedEndpointRemoves = workload.VpnEndpointInventory.Endpoints
            .Where(item => item.AddedByIranDirect)
            .Select(item => item.Identity)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(identity =>
                !desiredEndpointIdentities.Contains(identity) &&
                observedIdentities.Contains(identity));

        Assert.Equal(
            expectedPrefixAdds,
            result.Changes.Count(
                change => change.Kind == RuntimeChangeKind.AddPrefixRoute));
        Assert.Equal(
            expectedEndpointAdds,
            result.Changes.Count(
                change => change.Kind == RuntimeChangeKind.AddEndpointRoute));
        Assert.Equal(
            expectedPrefixRemoves,
            result.Changes.Count(
                change => change.Kind == RuntimeChangeKind.RemovePrefixRoute));
        Assert.Equal(
            expectedEndpointRemoves,
            result.Changes.Count(
                change => change.Kind == RuntimeChangeKind.RemoveEndpointRoute));

        Assert.Equal(
            workload.ExpectedAddedCount,
            expectedPrefixAdds + expectedEndpointAdds);
        Assert.Equal(
            workload.ExpectedRemovedCount,
            expectedPrefixRemoves + expectedEndpointRemoves);
        Assert.Equal(
            workload.ExpectedAddedCount + workload.ExpectedRemovedCount,
            result.Count);
    }

    [Fact]
    public void Planner_AllPresent_ProducesNoChanges()
    {
        RuntimeWorkload workload =
            _generator.Generate(RuntimeWorkloadScenario.AllPresent, RuntimeWorkloadSize.Scale5K);

        RuntimeChangeSet result = _planner.Plan(
            BuildSnapshot(workload),
            BuildOwnership(workload));

        Assert.True(result.IsEmpty);
        Assert.Equal(0, workload.ExpectedAddedCount);
        Assert.Equal(0, workload.ExpectedRemovedCount);
        Assert.Equal(5_000, workload.ExpectedUnchangedCount);
    }

    [Fact]
    public void Planner_DuplicateInput_DeduplicatesDesiredIdentities()
    {
        RuntimeWorkload workload =
            _generator.Generate(RuntimeWorkloadScenario.DuplicateInput, RuntimeWorkloadSize.Scale1K);

        RuntimeChangeSet result = _planner.Plan(
            BuildSnapshot(workload),
            BuildOwnership(workload));

        Assert.Equal(
            1_000,
            result.Changes.Count(
                change => change.Kind == RuntimeChangeKind.AddPrefixRoute));
        Assert.Equal(
            0,
            result.Changes.Count(
                change => change.Kind == RuntimeChangeKind.AddEndpointRoute));
        Assert.Equal(1_000, result.Count);
    }

    private static RuntimePlanSnapshot BuildSnapshot(RuntimeWorkload workload) =>
        new()
        {
            Configuration = ConfigurationDefaults.Create() with
            {
                Enabled = true
            },
            Desired = new DesiredRuntime
            {
                Enabled = true,
                EndpointRoutes = workload.DesiredEndpointRoutes,
                PrefixRoutes = workload.DesiredPrefixes,
                Blockers = []
            },
            Observed = new ObservedRuntime
            {
                VpnProfileExists = true,
                VpnProfileValid = true,
                VpnEndpoints = workload.VpnEndpoints,
                Routes = workload.ObservedRoutes,
                ObservedAt = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero)
            },
            PlannedAt = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero)
        };

    private static RuntimeRouteOwnership BuildOwnership(RuntimeWorkload workload) =>
        new()
        {
            EndpointRouteIdentities = workload.VpnEndpointInventory.Endpoints
                .Where(item => item.AddedByIranDirect)
                .Select(item => item.Identity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            PrefixRouteIdentities = workload.RouteInventory.Routes
                .Select(item => item.Identity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
        };
}
