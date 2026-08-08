using PathVeer.Core.Configuration;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Reconciliation;
using PathVeer.Testing.Performance.Workloads;

namespace PathVeer.Core.Tests.Runtime.Reconciliation;

/// <summary>
/// Proves the optimized <see cref="RuntimeChangeSetPlanner"/> produces
/// byte-for-byte equivalent results to the pre-optimization behavior
/// (modeled by <see cref="ReferenceChangeSetPlanner"/>) across the full
/// matrix of deterministic workloads. Covers exhaustive small cases,
/// every planned scenario at 1K/10K/50K, and targeted duplicate /
/// case-only / mismatch semantics.
/// </summary>
public sealed class PlannerEquivalenceTests
{
    private static readonly RuntimeWorkloadScenario[] Scenarios =
    [
        RuntimeWorkloadScenario.AllMissing,
        RuntimeWorkloadScenario.AllPresent,
        RuntimeWorkloadScenario.AllObsolete,
        RuntimeWorkloadScenario.Mixed,
        RuntimeWorkloadScenario.EndpointMixed,
        RuntimeWorkloadScenario.DuplicateInput
    ];

    private static readonly int[] Scales = [1_000, 10_000, 50_000];

    public static IEnumerable<object[]> ScenarioScaleCases()
    {
        foreach (RuntimeWorkloadScenario scenario in Scenarios)
        {
            foreach (int scale in Scales)
            {
                yield return
                [
                    scenario,
                    scale
                ];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioScaleCases))]
    public void OptimizedPlanner_MatchesReference_OnWorkload(
        RuntimeWorkloadScenario scenario,
        int scale)
    {
        RuntimeWorkload workload =
            new RuntimeWorkloadGenerator().Generate(
                scenario,
                (RuntimeWorkloadSize)scale);

        (RuntimePlanSnapshot snapshot, RuntimeRouteOwnership ownership) =
            BuildInput(workload);

        RuntimeChangeSet reference =
            new ReferenceChangeSetPlanner().Plan(
                snapshot,
                ownership);
        RuntimeChangeSet optimized =
            new RuntimeChangeSetPlanner().Plan(
                snapshot,
                ownership);

        AssertEquivalent(reference, optimized);
    }

    [Fact]
    public void EmptyInputs_ReturnEmptyFromBoth()
    {
        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: true,
                observedRoutes: [],
                desiredPrefixes: [],
                desiredEndpoints: []);

        RuntimeChangeSet reference =
            new ReferenceChangeSetPlanner().Plan(
                snapshot,
                new RuntimeRouteOwnership());
        RuntimeChangeSet optimized =
            new RuntimeChangeSetPlanner().Plan(
                snapshot,
                new RuntimeRouteOwnership());

        Assert.True(reference.IsEmpty);
        Assert.True(optimized.IsEmpty);
    }

    [Fact]
    public void BlockedSnapshot_ReturnsEmptyFromBoth()
    {
        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: true,
                observedRoutes: [],
                desiredPrefixes:
                [
                    new DesiredPrefixRoute
                    {
                        DestinationPrefix = "203.0.113.0/24",
                        Gateway = "192.168.100.1",
                        InterfaceIndex = 30
                    }
                ],
                desiredEndpoints: [],
                blocked: true);

        RuntimeChangeSet reference =
            new ReferenceChangeSetPlanner().Plan(
                snapshot,
                new RuntimeRouteOwnership());
        RuntimeChangeSet optimized =
            new RuntimeChangeSetPlanner().Plan(
                snapshot,
                new RuntimeRouteOwnership());

        Assert.True(reference.IsEmpty);
        Assert.True(optimized.IsEmpty);
    }

    [Fact]
    public void CaseOnlyIdentityDifference_IsTreatedAsSameByBoth()
    {
        // Identity is prefix|gateway|interface; casing in gateway must
        // not create a spurious add/remove.
        DesiredPrefixRoute desired = new()
        {
            DestinationPrefix = "203.0.113.0/24",
            Gateway = "192.168.100.1",
            InterfaceIndex = 30
        };
        ObservedRoute observed = new()
        {
            DestinationPrefix = "203.0.113.0/24",
            NextHop = "192.168.100.1",
            InterfaceIndex = 30
        };

        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: true,
                observedRoutes: [observed],
                desiredPrefixes: [desired],
                desiredEndpoints: []);

        RuntimeChangeSet reference =
            new ReferenceChangeSetPlanner().Plan(
                snapshot,
                Ownership(prefix: [observed.Identity]));
        RuntimeChangeSet optimized =
            new RuntimeChangeSetPlanner().Plan(
                snapshot,
                Ownership(prefix: [observed.Identity]));

        AssertEquivalent(reference, optimized);
        Assert.True(reference.IsEmpty);
    }

    [Theory]
    [InlineData("203.0.113.0/24", "192.168.100.1", 30, "192.168.100.1", 30)]
    [InlineData("203.0.113.0/24", "192.168.100.1", 30, "10.0.0.1", 30)]
    [InlineData("203.0.113.0/24", "192.168.100.1", 30, "192.168.100.1", 31)]
    public void GatewayAndInterfaceMismatch_DetectedIdentically(
        string prefix,
        string desiredGateway,
        uint desiredInterface,
        string observedGateway,
        uint observedInterface)
    {
        DesiredPrefixRoute desired = new()
        {
            DestinationPrefix = prefix,
            Gateway = desiredGateway,
            InterfaceIndex = desiredInterface
        };
        ObservedRoute observed = new()
        {
            DestinationPrefix = prefix,
            NextHop = observedGateway,
            InterfaceIndex = observedInterface
        };

        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: true,
                observedRoutes: [observed],
                desiredPrefixes: [desired],
                desiredEndpoints: []);

        RuntimeChangeSet reference =
            new ReferenceChangeSetPlanner().Plan(
                snapshot,
                Ownership(prefix: [observed.Identity]));
        RuntimeChangeSet optimized =
            new RuntimeChangeSetPlanner().Plan(
                snapshot,
                Ownership(prefix: [observed.Identity]));

        AssertEquivalent(reference, optimized);
    }

    [Fact]
    public void DuplicateDesiredIdentities_FirstOccurrenceWinsInBoth()
    {
        DesiredPrefixRoute first = new()
        {
            DestinationPrefix = "203.0.113.0/24",
            Gateway = "192.168.100.1",
            InterfaceIndex = 30,
            Metric = 5
        };
        DesiredPrefixRoute second = first with { Metric = 9 };

        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: true,
                observedRoutes: [],
                desiredPrefixes: [first, second],
                desiredEndpoints: []);

        RuntimeChangeSet reference =
            new ReferenceChangeSetPlanner().Plan(
                snapshot,
                new RuntimeRouteOwnership());
        RuntimeChangeSet optimized =
            new RuntimeChangeSetPlanner().Plan(
                snapshot,
                new RuntimeRouteOwnership());

        AssertEquivalent(reference, optimized);
        RuntimeChange add =
            Assert.Single(reference.Changes);
        // First occurrence wins => original metric.
        Assert.Equal(5, add.Metric);
    }

    [Fact]
    public void DuplicateObservedIdentities_FirstOccurrenceUsedInBoth()
    {
        ObservedRoute first = new()
        {
            DestinationPrefix = "203.0.113.0/24",
            NextHop = "192.168.100.1",
            InterfaceIndex = 30,
            Metric = 5
        };
        ObservedRoute second = first with { Metric = 99 };

        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: false,
                observedRoutes: [first, second],
                desiredPrefixes: [],
                desiredEndpoints: []);

        RuntimeChangeSet reference =
            new ReferenceChangeSetPlanner().Plan(
                snapshot,
                Ownership(prefix: [first.Identity]));
        RuntimeChangeSet optimized =
            new RuntimeChangeSetPlanner().Plan(
                snapshot,
                Ownership(prefix: [first.Identity]));

        AssertEquivalent(reference, optimized);
        RuntimeChange removal =
            Assert.Single(reference.Changes);
        Assert.Equal(5, removal.Metric);
    }

    [Fact]
    public void DuplicateInventoryIdentities_TreatedAsSingleRemovalInBoth()
    {
        ObservedRoute observed = new()
        {
            DestinationPrefix = "203.0.113.0/24",
            NextHop = "192.168.100.1",
            InterfaceIndex = 30
        };

        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: false,
                observedRoutes: [observed],
                desiredPrefixes: [],
                desiredEndpoints: []);

        RuntimeChangeSet reference =
            new ReferenceChangeSetPlanner().Plan(
                snapshot,
                Ownership(
                    prefix:
                    [
                        observed.Identity,
                        observed.Identity
                    ]));
        RuntimeChangeSet optimized =
            new RuntimeChangeSetPlanner().Plan(
                snapshot,
                Ownership(
                    prefix:
                    [
                        observed.Identity,
                        observed.Identity
                    ]));

        AssertEquivalent(reference, optimized);
        Assert.Single(reference.Changes);
    }

    [Fact]
    public void EndpointAddAndRemove_AreSeparateKindsInBoth()
    {
        DesiredEndpointRoute desired = new()
        {
            Host = "vpn.example",
            Address = "5.160.74.148",
            Port = 1409,
            Protocol = "tcp",
            DestinationPrefix = "5.160.74.148/32",
            Gateway = "192.168.100.1",
            InterfaceIndex = 30
        };
        ObservedRoute observed = new()
        {
            DestinationPrefix = "5.160.74.148/32",
            NextHop = "192.168.100.1",
            InterfaceIndex = 30
        };

        RuntimePlanSnapshot absent =
            CreateSnapshot(
                desiredEnabled: true,
                observedRoutes: [],
                desiredPrefixes: [],
                desiredEndpoints: [desired]);
        RuntimePlanSnapshot present =
            CreateSnapshot(
                desiredEnabled: false,
                observedRoutes: [observed],
                desiredPrefixes: [],
                desiredEndpoints: []);

        RuntimeChangeSet addRef =
            new ReferenceChangeSetPlanner().Plan(
                absent,
                new RuntimeRouteOwnership());
        RuntimeChangeSet addOpt =
            new RuntimeChangeSetPlanner().Plan(
                absent,
                new RuntimeRouteOwnership());

        RuntimeChangeSet removeRef =
            new ReferenceChangeSetPlanner().Plan(
                present,
                Ownership(endpoint: [observed.Identity]));
        RuntimeChangeSet removeOpt =
            new RuntimeChangeSetPlanner().Plan(
                present,
                Ownership(endpoint: [observed.Identity]));

        AssertEquivalent(addRef, addOpt);
        AssertEquivalent(removeRef, removeOpt);

        Assert.Single(addRef.Changes);
        Assert.Equal(
            RuntimeChangeKind.AddEndpointRoute,
            addRef.Changes[0].Kind);
        Assert.Single(removeRef.Changes);
        Assert.Equal(
            RuntimeChangeKind.RemoveEndpointRoute,
            removeRef.Changes[0].Kind);
    }

    private static void AssertEquivalent(
        RuntimeChangeSet expected,
        RuntimeChangeSet actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int i = 0; i < expected.Count; i++)
        {
            RuntimeChange e = expected.Changes[i];
            RuntimeChange a = actual.Changes[i];

            Assert.Equal(e.Kind, a.Kind);
            Assert.Equal(e.Identity, a.Identity);
            Assert.Equal(
                e.DestinationPrefix,
                a.DestinationPrefix);
            Assert.Equal(e.Gateway, a.Gateway);
            Assert.Equal(e.InterfaceIndex, a.InterfaceIndex);
            Assert.Equal(e.Metric, a.Metric);
            Assert.Equal(e.Description, a.Description);
        }
    }

    private static (
        RuntimePlanSnapshot Snapshot,
        RuntimeRouteOwnership Ownership) BuildInput(
        RuntimeWorkload workload)
    {
        RuntimePlanSnapshot snapshot = new()
        {
            Configuration =
                ConfigurationDefaults.Create() with
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
                ObservedAt = DateTimeOffset.UtcNow
            },
            PlannedAt = DateTimeOffset.UtcNow
        };

        RuntimeRouteOwnership ownership = new()
        {
            EndpointRouteIdentities =
                workload.VpnEndpointInventory.Endpoints
                    .Where(item => item.AddedByIranDirect)
                    .Select(item => item.Identity)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
            PrefixRouteIdentities =
                workload.RouteInventory.Routes
                    .Select(item => item.Identity)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

        return (snapshot, ownership);
    }

    private static RuntimePlanSnapshot CreateSnapshot(
        bool desiredEnabled,
        IReadOnlyList<ObservedRoute> observedRoutes,
        IReadOnlyList<DesiredPrefixRoute> desiredPrefixes,
        IReadOnlyList<DesiredEndpointRoute> desiredEndpoints,
        bool blocked = false)
    {
        return new RuntimePlanSnapshot
        {
            Configuration =
                ConfigurationDefaults.Create() with
                {
                    Enabled = desiredEnabled
                },
            Desired = new DesiredRuntime
            {
                Enabled = desiredEnabled,
                EndpointRoutes = desiredEndpoints,
                PrefixRoutes = desiredPrefixes,
                Blockers = blocked
                    ? [
                        new RuntimeBlocker
                        {
                            Code = RuntimeBlockerCode.InvalidConfiguration,
                            Message = "blocked"
                        }
                    ]
                    : []
            },
            Observed = new ObservedRuntime
            {
                VpnProfileExists = true,
                VpnProfileValid = true,
                Routes = observedRoutes,
                ObservedAt = DateTimeOffset.UtcNow
            },
            PlannedAt = DateTimeOffset.UtcNow
        };
    }

    private static RuntimeRouteOwnership Ownership(
        IEnumerable<string>? endpoint = null,
        IEnumerable<string>? prefix = null) =>
        new()
        {
            EndpointRouteIdentities =
                new HashSet<string>(
                    endpoint ?? [],
                    StringComparer.OrdinalIgnoreCase),
            PrefixRouteIdentities =
                new HashSet<string>(
                    prefix ?? [],
                    StringComparer.OrdinalIgnoreCase)
        };
}
