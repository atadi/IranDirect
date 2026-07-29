using IranDirect.Core.Configuration;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Reconciliation;

namespace IranDirect.Core.Tests.Runtime;

public sealed class RuntimeCycleCoordinatorTests
{
    [Fact]
    public async Task RunCycleAsync_BuildsPlanExactlyOnce()
    {
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new();
        RuntimeCycleCoordinator coordinator = new(
            planCoordinator, reconciler);

        await coordinator.RunCycleAsync();

        Assert.Equal(1, planCoordinator.BuildCallCount);
    }

    [Fact]
    public async Task RunCycleAsync_PassesExactPlanToReconciler()
    {
        RuntimePlanSnapshot snapshot = CreatePlanSnapshot();
        FakePlanCoordinator planCoordinator = new(snapshot);
        FakeReconciler reconciler = new();
        RuntimeCycleCoordinator coordinator = new(
            planCoordinator, reconciler);

        await coordinator.RunCycleAsync();

        Assert.Same(snapshot, reconciler.ReceivedSnapshot);
    }

    [Fact]
    public async Task RunCycleAsync_PreservesReconciliationResult()
    {
        RuntimeReconciliationResult expected =
            RuntimeReconciliationResult.NoChanges(
                "all good");
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(expected);
        RuntimeCycleCoordinator coordinator = new(
            planCoordinator, reconciler);

        RuntimeCycleResult result =
            await coordinator.RunCycleAsync();

        Assert.Same(expected, result.Reconciliation);
    }

    [Fact]
    public async Task RunCycleAsync_ForwardsTokenToPlanCoordinator()
    {
        using CancellationTokenSource cts = new();
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new();
        RuntimeCycleCoordinator coordinator = new(
            planCoordinator, reconciler);

        await coordinator.RunCycleAsync(cts.Token);

        Assert.Equal(cts.Token, planCoordinator.ReceivedToken);
    }

    [Fact]
    public async Task RunCycleAsync_ForwardsTokenToReconciler()
    {
        using CancellationTokenSource cts = new();
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new();
        RuntimeCycleCoordinator coordinator = new(
            planCoordinator, reconciler);

        await coordinator.RunCycleAsync(cts.Token);

        Assert.Equal(cts.Token, reconciler.ReceivedToken);
    }

    [Fact]
    public async Task RunCycleAsync_CancellationDuringPlanning_Propagates()
    {
        using CancellationTokenSource cts = new();
        FakePlanCoordinator planCoordinator = new(
            cancelOnBuild: true);
        FakeReconciler reconciler = new();
        RuntimeCycleCoordinator coordinator = new(
            planCoordinator, reconciler);

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => coordinator.RunCycleAsync(cts.Token));
    }

    [Fact]
    public async Task RunCycleAsync_CancellationDuringReconciliation_Propagates()
    {
        using CancellationTokenSource cts = new();
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(
            cancelOnReconcile: true);
        RuntimeCycleCoordinator coordinator = new(
            planCoordinator, reconciler);

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => coordinator.RunCycleAsync(cts.Token));
    }

    [Fact]
    public async Task RunCycleAsync_BlockedReconciliation_IsPreserved()
    {
        RuntimeReconciliationResult blocked =
            RuntimeReconciliationResult.Blocked(
                ["Gateway unavailable"]);
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(blocked);
        RuntimeCycleCoordinator coordinator = new(
            planCoordinator, reconciler);

        RuntimeCycleResult result =
            await coordinator.RunCycleAsync();

        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            result.Reconciliation.Status);
        Assert.False(result.Reconciliation.Succeeded);
        Assert.False(result.Reconciliation.MutatedInfrastructure);
    }

    [Fact]
    public async Task RunCycleAsync_ChangesPlanned_RemainsNonMutating()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                new RuntimeChange
                {
                    Kind = RuntimeChangeKind.AddPrefixRoute,
                    Identity = "203.0.113.0/24 via 192.168.1.1",
                    DestinationPrefix = "203.0.113.0/24",
                    Gateway = "192.168.1.1",
                    InterfaceIndex = 10,
                    Metric = 256,
                    Description = "Add test route."
                }
            ]
        };
        RuntimeReconciliationResult planned =
            RuntimeReconciliationResult.Planned(changeSet);
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(planned);
        RuntimeCycleCoordinator coordinator = new(
            planCoordinator, reconciler);

        RuntimeCycleResult result =
            await coordinator.RunCycleAsync();

        Assert.Equal(
            RuntimeReconciliationStatus.ChangesPlanned,
            result.Reconciliation.Status);
        Assert.True(result.Reconciliation.Succeeded);
        Assert.False(result.Reconciliation.MutatedInfrastructure);
        Assert.False(result.Reconciliation.ChangeSet.IsEmpty);
    }

    [Fact]
    public async Task RunCycleAsync_SetsCompletedAt()
    {
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new();
        RuntimeCycleCoordinator coordinator = new(
            planCoordinator, reconciler);

        DateTimeOffset before = DateTimeOffset.UtcNow;
        RuntimeCycleResult result =
            await coordinator.RunCycleAsync();
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.NotEqual(default, result.CompletedAt);
        Assert.InRange(
            result.CompletedAt, before, after);
    }

    private static RuntimePlanSnapshot CreatePlanSnapshot()
    {
        return new RuntimePlanSnapshot
        {
            Configuration = new DesiredConfiguration
            {
                Enabled = true
            },
            Observed = new ObservedRuntime
            {
                VpnProfileValid = true,
                Routes = [],
                VpnEndpoints = [],
                DirectGateway = new ObservedDirectGateway
                {
                    Address = "192.168.1.1",
                    InterfaceIndex = 10,
                    InterfaceName = "Ethernet",
                    InterfaceMetric = 10
                }
            },
            Desired = new DesiredRuntime
            {
                Enabled = true,
                EndpointRoutes = [],
                PrefixRoutes = [],
                Blockers = []
            },
            PlannedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class FakePlanCoordinator : IRuntimePlanCoordinator
    {
        private readonly RuntimePlanSnapshot? _snapshot;
        private readonly bool _cancelOnBuild;

        public int BuildCallCount { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }

        public FakePlanCoordinator(
            RuntimePlanSnapshot? snapshot = null,
            bool cancelOnBuild = false)
        {
            _snapshot = snapshot;
            _cancelOnBuild = cancelOnBuild;
        }

        public Task<RuntimePlanSnapshot> BuildPlanAsync(
            CancellationToken cancellationToken = default)
        {
            BuildCallCount++;
            ReceivedToken = cancellationToken;

            if (_cancelOnBuild)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.FromResult(
                _snapshot ?? CreatePlanSnapshot());
        }
    }

    private sealed class FakeReconciler : IRuntimeReconciler
    {
        private readonly RuntimeReconciliationResult? _result;
        private readonly bool _cancelOnReconcile;

        public RuntimePlanSnapshot? ReceivedSnapshot { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }

        public FakeReconciler(
            RuntimeReconciliationResult? result = null,
            bool cancelOnReconcile = false)
        {
            _result = result;
            _cancelOnReconcile = cancelOnReconcile;
        }

        public Task<RuntimeReconciliationResult> ReconcileAsync(
            RuntimePlanSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ReceivedSnapshot = snapshot;
            ReceivedToken = cancellationToken;

            if (_cancelOnReconcile)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.FromResult(
                _result ??
                RuntimeReconciliationResult.NoChanges());
        }
    }
}
