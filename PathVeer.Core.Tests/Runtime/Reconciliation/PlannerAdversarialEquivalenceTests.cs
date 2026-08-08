using PathVeer.Core.Configuration;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Reconciliation;

namespace PathVeer.Core.Tests.Runtime.Reconciliation;

/// <summary>
/// Phase 30.2 adversarial equivalence coverage. The second-pass
/// optimization folded the desired-identity pre-pass into the add passes
/// and replaced the global <c>OrderBy(Kind).ThenBy(Identity)</c> with
/// per-kind partition sorts. These cases stress exactly the assumptions
/// that change relies on: extreme duplicate ratios, all four kinds
/// present at once, and identities that collide only by case.
/// </summary>
public sealed class PlannerAdversarialEquivalenceTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1_000)]
    public void HighDuplicateRatio_MatchesReference(int duplicateFactor)
    {
        const int uniqueCount = 200;

        List<DesiredPrefixRoute> desired = [];
        for (int copy = 0; copy < duplicateFactor; copy++)
        {
            for (int i = 0; i < uniqueCount; i++)
            {
                desired.Add(
                    new DesiredPrefixRoute
                    {
                        DestinationPrefix = $"203.0.{i / 256}.{i % 256}/32",
                        Gateway = "192.168.100.1",
                        InterfaceIndex = 30,

                        // Later copies carry a different metric so a
                        // first-occurrence violation is observable.
                        Metric = 1 + copy
                    });
            }
        }

        AssertPlannersAgree(
            CreateSnapshot(
                observedRoutes: [],
                desiredPrefixes: desired,
                desiredEndpoints: []),
            new RuntimeRouteOwnership());
    }

    [Fact]
    public void AllDuplicatesOfSingleIdentity_MatchesReference()
    {
        DesiredPrefixRoute route = new()
        {
            DestinationPrefix = "203.0.113.0/24",
            Gateway = "192.168.100.1",
            InterfaceIndex = 30,
            Metric = 7
        };

        List<DesiredPrefixRoute> desired = [];
        for (int i = 0; i < 10_000; i++)
        {
            desired.Add(route with { Metric = 7 + i });
        }

        AssertPlannersAgree(
            CreateSnapshot(
                observedRoutes: [],
                desiredPrefixes: desired,
                desiredEndpoints: []),
            new RuntimeRouteOwnership());
    }

    [Fact]
    public void DuplicatesDifferingOnlyByCase_MatchesReference()
    {
        List<DesiredPrefixRoute> desired = [];
        for (int i = 0; i < 500; i++)
        {
            desired.Add(
                new DesiredPrefixRoute
                {
                    DestinationPrefix = $"2001:DB8:{i:X}::/64",
                    Gateway = "FE80::1",
                    InterfaceIndex = 30,
                    Metric = 1
                });
            desired.Add(
                new DesiredPrefixRoute
                {
                    DestinationPrefix = $"2001:db8:{i:x}::/64",
                    Gateway = "fe80::1",
                    InterfaceIndex = 30,
                    Metric = 2
                });
        }

        AssertPlannersAgree(
            CreateSnapshot(
                observedRoutes: [],
                desiredPrefixes: desired,
                desiredEndpoints: []),
            new RuntimeRouteOwnership());
    }

    [Fact]
    public void AllFourKindsTogether_OrderingMatchesReference()
    {
        // Endpoint add + endpoint remove + prefix add + prefix remove in
        // one plan, with identities deliberately interleaved across kinds
        // so a per-partition sort that leaked between kinds would show up.
        List<DesiredEndpointRoute> endpoints = [];
        List<DesiredPrefixRoute> prefixes = [];
        List<ObservedRoute> observed = [];
        HashSet<string> ownedEndpoints =
            new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> ownedPrefixes =
            new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < 400; i++)
        {
            // Desired-but-absent endpoint => AddEndpointRoute.
            endpoints.Add(
                new DesiredEndpointRoute
                {
                    Host = $"vpn{i}.example",
                    Address = $"5.160.{i / 256}.{i % 256}",
                    Port = 1409,
                    Protocol = "tcp",
                    DestinationPrefix = $"5.160.{i / 256}.{i % 256}/32",
                    Gateway = "192.168.100.1",
                    InterfaceIndex = 30
                });

            // Desired-but-absent prefix => AddPrefixRoute.
            prefixes.Add(
                new DesiredPrefixRoute
                {
                    DestinationPrefix = $"198.51.{i / 256}.{i % 256}/32",
                    Gateway = "192.168.100.1",
                    InterfaceIndex = 30
                });

            // Owned + observed + undesired endpoint => RemoveEndpointRoute.
            ObservedRoute staleEndpoint = new()
            {
                DestinationPrefix = $"5.161.{i / 256}.{i % 256}/32",
                NextHop = "192.168.100.1",
                InterfaceIndex = 30,
                Metric = 3
            };
            observed.Add(staleEndpoint);
            ownedEndpoints.Add(staleEndpoint.Identity);

            // Owned + observed + undesired prefix => RemovePrefixRoute.
            ObservedRoute stalePrefix = new()
            {
                DestinationPrefix = $"198.52.{i / 256}.{i % 256}/32",
                NextHop = "192.168.100.1",
                InterfaceIndex = 30,
                Metric = 4
            };
            observed.Add(stalePrefix);
            ownedPrefixes.Add(stalePrefix.Identity);
        }

        RuntimeChangeSet result = AssertPlannersAgree(
            CreateSnapshot(
                observedRoutes: observed,
                desiredPrefixes: prefixes,
                desiredEndpoints: endpoints),
            new RuntimeRouteOwnership
            {
                EndpointRouteIdentities = ownedEndpoints,
                PrefixRouteIdentities = ownedPrefixes
            });

        // All four kinds must actually be represented, otherwise this
        // test would silently stop covering the partition boundaries.
        Assert.Equal(1_600, result.Count);
        Assert.Contains(
            result.Changes,
            change => change.Kind == RuntimeChangeKind.AddEndpointRoute);
        Assert.Contains(
            result.Changes,
            change => change.Kind == RuntimeChangeKind.RemoveEndpointRoute);
        Assert.Contains(
            result.Changes,
            change => change.Kind == RuntimeChangeKind.AddPrefixRoute);
        Assert.Contains(
            result.Changes,
            change => change.Kind == RuntimeChangeKind.RemovePrefixRoute);
    }

    [Fact]
    public void DuplicateDesiredThatIsAlsoObserved_MatchesReference()
    {
        // Exercises the reordered guard: the identity must still enter
        // the desired membership set even when the observed check
        // suppresses the add, so the removal pass stays suppressed too.
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

        AssertPlannersAgree(
            CreateSnapshot(
                observedRoutes: [observed],
                desiredPrefixes: [desired, desired, desired],
                desiredEndpoints: []),
            new RuntimeRouteOwnership
            {
                PrefixRouteIdentities = new HashSet<string>(
                    [observed.Identity],
                    StringComparer.OrdinalIgnoreCase)
            });
    }

    [Fact]
    public void InputCollectionsAreNotMutated()
    {
        List<DesiredPrefixRoute> prefixes =
        [
            new DesiredPrefixRoute
            {
                DestinationPrefix = "203.0.113.0/24",
                Gateway = "192.168.100.1",
                InterfaceIndex = 30
            },
            new DesiredPrefixRoute
            {
                DestinationPrefix = "198.51.100.0/24",
                Gateway = "192.168.100.1",
                InterfaceIndex = 30
            }
        ];
        List<ObservedRoute> observed =
        [
            new ObservedRoute
            {
                DestinationPrefix = "203.0.113.0/24",
                NextHop = "192.168.100.1",
                InterfaceIndex = 30
            }
        ];

        DesiredPrefixRoute[] prefixSnapshot = [.. prefixes];
        ObservedRoute[] observedSnapshot = [.. observed];

        _ = new RuntimeChangeSetPlanner().Plan(
            CreateSnapshot(
                observedRoutes: observed,
                desiredPrefixes: prefixes,
                desiredEndpoints: []),
            new RuntimeRouteOwnership());

        Assert.Equal(prefixSnapshot, prefixes);
        Assert.Equal(observedSnapshot, observed);
    }

    private static RuntimeChangeSet AssertPlannersAgree(
        RuntimePlanSnapshot snapshot,
        RuntimeRouteOwnership ownership)
    {
        RuntimeChangeSet reference =
            new ReferenceChangeSetPlanner().Plan(snapshot, ownership);
        RuntimeChangeSet optimized =
            new RuntimeChangeSetPlanner().Plan(snapshot, ownership);

        Assert.Equal(reference.Count, optimized.Count);

        for (int i = 0; i < reference.Count; i++)
        {
            RuntimeChange e = reference.Changes[i];
            RuntimeChange a = optimized.Changes[i];

            Assert.Equal(e.Kind, a.Kind);
            Assert.Equal(e.Identity, a.Identity);
            Assert.Equal(e.DestinationPrefix, a.DestinationPrefix);
            Assert.Equal(e.Gateway, a.Gateway);
            Assert.Equal(e.InterfaceIndex, a.InterfaceIndex);
            Assert.Equal(e.Metric, a.Metric);
            Assert.Equal(e.Description, a.Description);
        }

        return optimized;
    }

    private static RuntimePlanSnapshot CreateSnapshot(
        IReadOnlyList<ObservedRoute> observedRoutes,
        IReadOnlyList<DesiredPrefixRoute> desiredPrefixes,
        IReadOnlyList<DesiredEndpointRoute> desiredEndpoints) =>
        new()
        {
            Configuration = ConfigurationDefaults.Create() with
            {
                Enabled = true
            },
            Desired = new DesiredRuntime
            {
                Enabled = true,
                EndpointRoutes = desiredEndpoints,
                PrefixRoutes = desiredPrefixes,
                Blockers = []
            },
            Observed = new ObservedRuntime
            {
                VpnProfileExists = true,
                VpnProfileValid = true,
                Routes = observedRoutes,
                ObservedAt = DateTimeOffset.UnixEpoch
            },
            PlannedAt = DateTimeOffset.UnixEpoch
        };
}
