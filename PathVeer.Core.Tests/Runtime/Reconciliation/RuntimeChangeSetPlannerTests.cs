using PathVeer.Core.Configuration;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Reconciliation;

namespace PathVeer.Core.Tests.Runtime.Reconciliation;

public sealed class RuntimeChangeSetPlannerTests
{
    private readonly RuntimeChangeSetPlanner _planner = new();

    [Fact]
    public void Plan_WhenDesiredAndObservedMatch_ReturnsEmptySet()
    {
        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: true,
                observedRoutes:
                [
                    EndpointObserved(),
                    PrefixObserved()
                ]);

        RuntimeChangeSet result =
            _planner.Plan(
                snapshot,
                Ownership(
                    endpoint:
                    [
                        EndpointObserved().Identity
                    ],
                    prefix:
                    [
                        PrefixObserved().Identity
                    ]));

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Plan_AddsMissingDesiredRoutes()
    {
        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: true,
                observedRoutes: []);

        RuntimeChangeSet result =
            _planner.Plan(
                snapshot,
                new RuntimeRouteOwnership());

        Assert.Equal(2, result.Count);
        Assert.Contains(
            result.Changes,
            change =>
                change.Kind ==
                RuntimeChangeKind.AddEndpointRoute);
        Assert.Contains(
            result.Changes,
            change =>
                change.Kind ==
                RuntimeChangeKind.AddPrefixRoute);
    }

    [Fact]
    public void Plan_RemovesOnlyOwnedUndesiredPrefixRoutes()
    {
        ObservedRoute owned = PrefixObserved();

        ObservedRoute foreign = new()
        {
            DestinationPrefix = "198.51.100.0/24",
            NextHop = "192.168.100.1",
            InterfaceIndex = 30,
            Metric = 5
        };

        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: false,
                observedRoutes:
                [
                    EndpointObserved(),
                    owned,
                    foreign
                ]);

        RuntimeChangeSet result =
            _planner.Plan(
                snapshot,
                Ownership(
                    endpoint: [],
                    prefix:
                    [
                        owned.Identity
                    ]));

        RuntimeChange removal =
            Assert.Single(
                result.Changes,
                change =>
                    change.Kind ==
                    RuntimeChangeKind.RemovePrefixRoute);

        Assert.Equal(owned.Identity, removal.Identity);

        Assert.DoesNotContain(
            result.Changes,
            change =>
                change.Identity ==
                foreign.Identity);
    }

    [Fact]
    public void Plan_DoesNotRemoveOwnedInventoryEntryWhenRouteIsAbsent()
    {
        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: false,
                observedRoutes:
                [
                    EndpointObserved()
                ]);

        RuntimeChangeSet result =
            _planner.Plan(
                snapshot,
                Ownership(
                    endpoint: [],
                    prefix:
                    [
                        PrefixObserved().Identity
                    ]));

        Assert.DoesNotContain(
            result.Changes,
            change =>
                change.Kind ==
                RuntimeChangeKind.RemovePrefixRoute);
    }

    [Fact]
    public void Plan_WhenBlocked_ReturnsNoChanges()
    {
        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: true,
                observedRoutes: [],
                blocked: true);

        RuntimeChangeSet result =
            _planner.Plan(
                snapshot,
                new RuntimeRouteOwnership());

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Plan_WhenDisabled_StillDesiresEndpointProtection()
    {
        RuntimePlanSnapshot snapshot =
            CreateSnapshot(
                desiredEnabled: false,
                observedRoutes: []);

        RuntimeChangeSet result =
            _planner.Plan(
                snapshot,
                new RuntimeRouteOwnership());

        RuntimeChange change =
            Assert.Single(result.Changes);

        Assert.Equal(
            RuntimeChangeKind.AddEndpointRoute,
            change.Kind);
    }

    private static RuntimePlanSnapshot CreateSnapshot(
        bool desiredEnabled,
        IReadOnlyList<ObservedRoute> observedRoutes,
        bool blocked = false)
    {
        DesiredEndpointRoute endpoint = new()
        {
            Host = "vpn.example",
            Address = "5.160.74.148",
            Port = 1409,
            Protocol = "tcp",
            DestinationPrefix = "5.160.74.148/32",
            Gateway = "192.168.100.1",
            InterfaceIndex = 30,
            Metric = 1
        };

        DesiredPrefixRoute prefix = new()
        {
            DestinationPrefix = "203.0.113.0/24",
            Gateway = "192.168.100.1",
            InterfaceIndex = 30,
            Metric = 5
        };

        DesiredRuntime desired = new()
        {
            Enabled = desiredEnabled,
            EndpointRoutes =
            [
                endpoint
            ],
            PrefixRoutes =
                desiredEnabled
                    ? [prefix]
                    : [],
            Blockers =
                blocked
                    ?
                    [
                        new RuntimeBlocker
                        {
                            Code =
                                RuntimeBlockerCode.InvalidConfiguration,
                            Message =
                                "Configuration is invalid."
                        }
                    ]
                    : []
        };

        return new RuntimePlanSnapshot
        {
            Configuration =
                ConfigurationDefaults.Create() with
                {
                    Enabled = desiredEnabled
                },
            Desired = desired,
            Observed = new ObservedRuntime
            {
                VpnProfileExists = true,
                VpnProfileValid = true,
                DirectGateway =
                    new ObservedDirectGateway
                    {
                        Address = "192.168.100.1",
                        InterfaceIndex = 30,
                        InterfaceName = "Ethernet",
                        InterfaceMetric = 10
                    },
                VpnEndpoints =
                [
                    new ObservedVpnEndpoint
                    {
                        Host = "vpn.example",
                        Address = "5.160.74.148",
                        Port = 1409,
                        Protocol = "tcp"
                    }
                ],
                Prefixes =
                [
                    "203.0.113.0/24"
                ],
                Routes = observedRoutes,
                ObservedAt = DateTimeOffset.UtcNow
            },
            PlannedAt = DateTimeOffset.UtcNow
        };
    }

    private static ObservedRoute EndpointObserved() =>
        new()
        {
            DestinationPrefix = "5.160.74.148/32",
            NextHop = "192.168.100.1",
            InterfaceIndex = 30,
            Metric = 1
        };

    private static ObservedRoute PrefixObserved() =>
        new()
        {
            DestinationPrefix = "203.0.113.0/24",
            NextHop = "192.168.100.1",
            InterfaceIndex = 30,
            Metric = 5
        };

    private static RuntimeRouteOwnership Ownership(
        IEnumerable<string> endpoint,
        IEnumerable<string> prefix) =>
        new()
        {
            EndpointRouteIdentities =
                new HashSet<string>(
                    endpoint,
                    StringComparer.OrdinalIgnoreCase),
            PrefixRouteIdentities =
                new HashSet<string>(
                    prefix,
                    StringComparer.OrdinalIgnoreCase)
        };
}