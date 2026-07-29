using IranDirect.Core.Configuration;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Reconciliation;

namespace IranDirect.Core.Tests.Runtime.Reconciliation;

public sealed class RuntimeReconcilerTests
{
    private readonly RuntimeChangeSetPlanner _planner = new();

    [Fact]
    public async Task ReconcileAsync_BlockedPlan_ReturnsBlocked()
    {
        RuntimeReconciler reconciler = CreateReconciler();
        RuntimePlanSnapshot snapshot = CreateSnapshot(blocked: true);

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            result.Status);
        Assert.False(result.Succeeded);
        Assert.False(result.MutatedInfrastructure);
        Assert.Contains(
            "Configuration is invalid.",
            result.Messages);
    }

    [Fact]
    public async Task ReconcileAsync_BlockedPlan_DoesNotLoadOwnership()
    {
        TrackingSource source = new();
        RuntimeReconciler reconciler = CreateReconciler(source);
        RuntimePlanSnapshot snapshot = CreateSnapshot(blocked: true);

        await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(0, source.CallCount);
    }

    [Fact]
    public async Task ReconcileAsync_MatchingRuntime_ReturnsNoChangesRequired()
    {
        RuntimeReconciler reconciler = CreateReconciler(
            endpointIdentities:
            [
                EndpointObserved().Identity
            ],
            prefixIdentities:
            [
                PrefixObserved().Identity
            ]);

        RuntimePlanSnapshot snapshot = CreateSnapshot(
            observedRoutes:
            [
                EndpointObserved(),
                PrefixObserved()
            ]);

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(
            RuntimeReconciliationStatus.NoChangesRequired,
            result.Status);
        Assert.True(result.Succeeded);
        Assert.False(result.MutatedInfrastructure);
        Assert.True(result.ChangeSet.IsEmpty);
    }

    [Fact]
    public async Task ReconcileAsync_WithDifferences_ReturnsChangesPlanned()
    {
        RuntimeReconciler reconciler = CreateReconciler();
        RuntimePlanSnapshot snapshot = CreateSnapshot(
            observedRoutes: []);

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(
            RuntimeReconciliationStatus.ChangesPlanned,
            result.Status);
        Assert.True(result.Succeeded);
        Assert.False(result.MutatedInfrastructure);
        Assert.False(result.ChangeSet.IsEmpty);
    }

    [Fact]
    public async Task ReconcileAsync_ChangesPlanned_ContainsExactChangeSet()
    {
        RuntimeReconciler reconciler = CreateReconciler();
        RuntimePlanSnapshot snapshot = CreateSnapshot(
            observedRoutes: []);

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.Contains(
            result.ChangeSet.Changes,
            change =>
                change.Kind ==
                RuntimeChangeKind.AddEndpointRoute);

        Assert.Contains(
            result.ChangeSet.Changes,
            change =>
                change.Kind ==
                RuntimeChangeKind.AddPrefixRoute);
    }

    [Fact]
    public async Task ReconcileAsync_ChangesPlanned_DoesNotReportMutation()
    {
        RuntimeReconciler reconciler = CreateReconciler();
        RuntimePlanSnapshot snapshot = CreateSnapshot(
            observedRoutes: []);

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.False(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ReconcileAsync_LoadsOwnershipExactlyOnce()
    {
        TrackingSource source = new();
        RuntimeReconciler reconciler = CreateReconciler(source);
        RuntimePlanSnapshot snapshot = CreateSnapshot();

        await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(2, source.CallCount);
    }

    [Fact]
    public async Task ReconcileAsync_Cancellation_Propagates()
    {
        CancellationTokenSource cts = new();
        RuntimeReconciler reconciler = CreateReconciler(
            new CancellingSource(cts.Token));
        RuntimePlanSnapshot snapshot = CreateSnapshot();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => reconciler.ReconcileAsync(
                snapshot, cts.Token));
    }

    [Fact]
    public async Task ReconcileAsync_WhenOwnershipFails_ReturnsFailed()
    {
        RuntimeReconciler reconciler = CreateReconciler(
            new ThrowingSource());
        RuntimePlanSnapshot snapshot = CreateSnapshot();

        RuntimeReconciliationResult result =
            await reconciler.ReconcileAsync(snapshot);

        Assert.Equal(
            RuntimeReconciliationStatus.Failed,
            result.Status);
        Assert.False(result.Succeeded);
        Assert.False(result.MutatedInfrastructure);
    }

    [Fact]
    public async Task ReconcileAsync_WhenSnapshotIsNull_Throws()
    {
        RuntimeReconciler reconciler = CreateReconciler();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reconciler.ReconcileAsync(null!));
    }

    [Fact]
    public void PlannedFactory_RequiresNonNullChangeSet()
    {
        Assert.Throws<ArgumentNullException>(
            () => RuntimeReconciliationResult.Planned(null!));
    }

    [Fact]
    public void PlannedFactory_RequiresNonEmptyChangeSet()
    {
        Assert.Throws<ArgumentException>(
            () => RuntimeReconciliationResult.Planned(
                new RuntimeChangeSet()));
    }

    [Fact]
    public void PlannedFactory_WithChanges_SucceedsWithoutMutation()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                new RuntimeChange
                {
                    Kind = RuntimeChangeKind.AddEndpointRoute,
                    Identity = "5.160.74.148/32|192.168.100.1|30",
                    DestinationPrefix = "5.160.74.148/32",
                    Gateway = "192.168.100.1",
                    InterfaceIndex = 30,
                    Metric = 1,
                    Description = "Test change."
                }
            ]
        };

        RuntimeReconciliationResult result =
            RuntimeReconciliationResult.Planned(changeSet);

        Assert.True(result.Succeeded);
        Assert.False(result.MutatedInfrastructure);
        Assert.Equal(
            RuntimeReconciliationStatus.ChangesPlanned,
            result.Status);
        Assert.Single(result.ChangeSet.Changes);
    }

    private static RuntimeReconciler CreateReconciler(
        IReadOnlyCollection<string>? endpointIdentities = null,
        IReadOnlyCollection<string>? prefixIdentities = null)
    {
        TrackingSource source = new()
        {
            EndpointIdentities =
                endpointIdentities ?? [],
            PrefixIdentities =
                prefixIdentities ?? []
        };

        return CreateReconciler(source);
    }

    private static RuntimeReconciler CreateReconciler(
        IRuntimeRouteOwnershipSource source)
    {
        RuntimeRouteOwnershipProvider provider = new(source);
        return new RuntimeReconciler(
            provider, new RuntimeChangeSetPlanner());
    }

    private static RuntimePlanSnapshot CreateSnapshot(
        bool desiredEnabled = true,
        IReadOnlyList<ObservedRoute>? observedRoutes = null,
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
            EndpointRoutes = [endpoint],
            PrefixRoutes =
                desiredEnabled ? [prefix] : [],
            Blockers =
                blocked
                    ?
                    [
                        new RuntimeBlocker
                        {
                            Code =
                                RuntimeBlockerCode
                                    .InvalidConfiguration,
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
                Routes =
                    observedRoutes ?? [],
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

    private sealed class TrackingSource :
        IRuntimeRouteOwnershipSource
    {
        public int CallCount { get; private set; }

        public IReadOnlyCollection<string>
            EndpointIdentities { get; init; } = [];

        public IReadOnlyCollection<string>
            PrefixIdentities { get; init; } = [];

        public Task<IReadOnlyCollection<string>>
            LoadEndpointRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(EndpointIdentities);
        }

        public Task<IReadOnlyCollection<string>>
            LoadPrefixRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PrefixIdentities);
        }
    }

    private sealed class CancellingSource :
        IRuntimeRouteOwnershipSource
    {
        private readonly CancellationToken _cancelToken;

        public CancellingSource(
            CancellationToken cancelToken)
        {
            _cancelToken = cancelToken;
        }

        public Task<IReadOnlyCollection<string>>
            LoadEndpointRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            CancellationTokenSource linked = CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken, _cancelToken);

            linked.Token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyCollection<string>>(
                []);
        }

        public Task<IReadOnlyCollection<string>>
            LoadPrefixRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            CancellationTokenSource linked = CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken, _cancelToken);

            linked.Token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyCollection<string>>(
                []);
        }
    }

    private sealed class ThrowingSource :
        IRuntimeRouteOwnershipSource
    {
        public Task<IReadOnlyCollection<string>>
            LoadEndpointRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Inventory corruption detected.");
        }

        public Task<IReadOnlyCollection<string>>
            LoadPrefixRouteIdentitiesAsync(
                CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Inventory corruption detected.");
        }
    }
}
