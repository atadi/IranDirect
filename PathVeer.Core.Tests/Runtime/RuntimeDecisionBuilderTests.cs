using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Reconciliation;

namespace PathVeer.Core.Tests.Runtime;

public sealed class RuntimeDecisionBuilderTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_NullPlanCoordinator_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RuntimeDecisionBuilder(
                null!,
                CreateFakeReconciler(),
                new RuntimeExecutionPlanner(),
                CreateFixedTimeProvider()));
    }

    [Fact]
    public void Constructor_NullReconciler_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RuntimeDecisionBuilder(
                CreateFakePlanCoordinator(),
                null!,
                new RuntimeExecutionPlanner(),
                CreateFixedTimeProvider()));
    }

    [Fact]
    public void Constructor_NullPlanner_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RuntimeDecisionBuilder(
                CreateFakePlanCoordinator(),
                CreateFakeReconciler(),
                null!,
                CreateFixedTimeProvider()));
    }

    [Fact]
    public void Constructor_NullTimeProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RuntimeDecisionBuilder(
                CreateFakePlanCoordinator(),
                CreateFakeReconciler(),
                new RuntimeExecutionPlanner(),
                null!));
    }

    [Fact]
    public async Task BuildAsync_BuildsPlanExactlyOnce()
    {
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new();
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        await builder.BuildAsync();

        Assert.Equal(1, planCoordinator.BuildCallCount);
    }

    [Fact]
    public async Task BuildAsync_PassesExactPlanToReconciler()
    {
        RuntimePlanSnapshot snapshot = CreatePlanSnapshot();
        FakePlanCoordinator planCoordinator = new(snapshot);
        FakeReconciler reconciler = new();
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        await builder.BuildAsync();

        Assert.Same(snapshot, reconciler.ReceivedSnapshot);
    }

    [Fact]
    public async Task BuildAsync_ReconcilerInvokedExactlyOnce()
    {
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new();
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        await builder.BuildAsync();

        Assert.Equal(1, reconciler.ReconcileCallCount);
    }

    [Fact]
    public async Task BuildAsync_PassesChangeSetToPlanner()
    {
        RuntimeChangeSet changeSet = CreateSingleAddEndpointChangeSet();
        RuntimeReconciliationResult planned =
            RuntimeReconciliationResult.Planned(changeSet);
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(planned);
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        RuntimeDecision decision = await builder.BuildAsync();

        Assert.Equal(changeSet.Count, decision.ExecutionPlan.Count);
        Assert.Equal(
            changeSet.Changes[0].Identity,
            decision.ExecutionPlan.Steps[0].Identity);
    }

    [Fact]
    public async Task BuildAsync_PreservesExactPlanReference()
    {
        RuntimePlanSnapshot snapshot = CreatePlanSnapshot();
        FakePlanCoordinator planCoordinator = new(snapshot);
        FakeReconciler reconciler = new();
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        RuntimeDecision decision = await builder.BuildAsync();

        Assert.Same(snapshot, decision.Plan);
    }

    [Fact]
    public async Task BuildAsync_PreservesExactReconciliationReference()
    {
        RuntimeReconciliationResult expected =
            RuntimeReconciliationResult.NoChanges("all good");
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(expected);
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        RuntimeDecision decision = await builder.BuildAsync();

        Assert.Same(expected, decision.Reconciliation);
    }

    [Fact]
    public async Task BuildAsync_PreservesExactExecutionPlanReference()
    {
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new();
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        RuntimeDecision decision = await builder.BuildAsync();

        Assert.NotNull(decision.ExecutionPlan);
    }

    [Fact]
    public async Task BuildAsync_NoChangesRequired_ProducesEmptyPlan()
    {
        RuntimeDecisionBuilder builder = CreateBuilder();

        RuntimeDecision decision = await builder.BuildAsync();

        Assert.Equal(
            RuntimeReconciliationStatus.NoChangesRequired,
            decision.Reconciliation.Status);
        Assert.True(decision.ExecutionPlan.IsEmpty);
    }

    [Fact]
    public async Task BuildAsync_Blocked_ProducesEmptyPlan()
    {
        RuntimeReconciliationResult blocked =
            RuntimeReconciliationResult.Blocked(["Blocked"]);
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(blocked);
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        RuntimeDecision decision = await builder.BuildAsync();

        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            decision.Reconciliation.Status);
        Assert.True(decision.ExecutionPlan.IsEmpty);
    }

    [Fact]
    public async Task BuildAsync_Failed_ProducesEmptyPlan()
    {
        RuntimeReconciliationResult failed =
            RuntimeReconciliationResult.Failed(["Failed"]);
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(failed);
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        RuntimeDecision decision = await builder.BuildAsync();

        Assert.Equal(
            RuntimeReconciliationStatus.Failed,
            decision.Reconciliation.Status);
        Assert.True(decision.ExecutionPlan.IsEmpty);
    }

    [Fact]
    public async Task BuildAsync_ChangesPlanned_ProducesNonEmptyPlan()
    {
        RuntimeChangeSet changeSet = CreateSingleAddEndpointChangeSet();
        RuntimeReconciliationResult planned =
            RuntimeReconciliationResult.Planned(changeSet);
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(planned);
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        RuntimeDecision decision = await builder.BuildAsync();

        Assert.Equal(
            RuntimeReconciliationStatus.ChangesPlanned,
            decision.Reconciliation.Status);
        Assert.False(decision.ExecutionPlan.IsEmpty);
        Assert.Equal(1, decision.ExecutionPlan.Count);
    }

    [Fact]
    public async Task BuildAsync_ChangesApplied_Throws()
    {
        RuntimeChangeSet changeSet = CreateSingleAddEndpointChangeSet();
        RuntimeReconciliationResult applied =
            RuntimeReconciliationResult.Applied(changeSet);
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(applied);
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => builder.BuildAsync());
    }

    [Fact]
    public async Task BuildAsync_ForwardsTokenToPlanCoordinator()
    {
        using CancellationTokenSource cts = new();
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new();
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        await builder.BuildAsync(cts.Token);

        Assert.Equal(cts.Token, planCoordinator.ReceivedToken);
    }

    [Fact]
    public async Task BuildAsync_ForwardsTokenToReconciler()
    {
        using CancellationTokenSource cts = new();
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new();
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        await builder.BuildAsync(cts.Token);

        Assert.Equal(cts.Token, reconciler.ReceivedToken);
    }

    [Fact]
    public async Task BuildAsync_CancellationDuringPlanning_Propagates()
    {
        using CancellationTokenSource cts = new();
        FakePlanCoordinator planCoordinator = new(
            cancelOnBuild: true);
        FakeReconciler reconciler = new();
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => builder.BuildAsync(cts.Token));
    }

    [Fact]
    public async Task BuildAsync_CancellationDuringReconciliation_Propagates()
    {
        using CancellationTokenSource cts = new();
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(
            cancelOnReconcile: true);
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => builder.BuildAsync(cts.Token));
    }

    [Fact]
    public async Task BuildAsync_PlannerException_Propagates()
    {
        RuntimeChangeSet invalidChangeSet = new()
        {
            Changes =
            [
                new RuntimeChange
                {
                    Kind = (RuntimeChangeKind)999,
                    Identity = "test",
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "test"
                }
            ]
        };

        RuntimeReconciliationResult planned =
            RuntimeReconciliationResult.Planned(invalidChangeSet);
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(planned);
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => builder.BuildAsync());
    }

    [Fact]
    public async Task BuildAsync_DecidedAtFromTimeProvider()
    {
        FakeTimeProvider timeProvider = new(FixedTime);
        RuntimeDecisionBuilder builder = new(
            CreateFakePlanCoordinator(),
            CreateFakeReconciler(),
            new RuntimeExecutionPlanner(),
            timeProvider);

        RuntimeDecision decision = await builder.BuildAsync();

        Assert.Equal(FixedTime, decision.DecidedAt);
    }

    [Fact]
    public async Task BuildAsync_TimeProviderCalledOnce()
    {
        FakeTimeProvider timeProvider = new(FixedTime);
        RuntimeDecisionBuilder builder = new(
            CreateFakePlanCoordinator(),
            CreateFakeReconciler(),
            new RuntimeExecutionPlanner(),
            timeProvider);

        await builder.BuildAsync();

        Assert.Equal(1, timeProvider.GetUtcNowCallCount);
    }

    [Fact]
    public async Task BuildAsync_EquivalentInputs_ProduceEquivalentDecisions()
    {
        RuntimeChangeSet changeSet = CreateSingleAddEndpointChangeSet();
        RuntimeReconciliationResult planned =
            RuntimeReconciliationResult.Planned(changeSet);
        FakePlanCoordinator planCoordinator = new();
        FakeReconciler reconciler = new(planned);
        RuntimeDecisionBuilder builder = CreateBuilder(
            planCoordinator, reconciler);

        RuntimeDecision a = await builder.BuildAsync();
        RuntimeDecision b = await builder.BuildAsync();

        Assert.Equal(a.Plan, b.Plan);
        Assert.Equal(a.Reconciliation.Status, b.Reconciliation.Status);
        Assert.Equal(a.ExecutionPlan.Count, b.ExecutionPlan.Count);
    }

    private static RuntimeDecisionBuilder CreateBuilder(
        FakePlanCoordinator? planCoordinator = null,
        FakeReconciler? reconciler = null)
    {
        return new RuntimeDecisionBuilder(
            planCoordinator ?? CreateFakePlanCoordinator(),
            reconciler ?? CreateFakeReconciler(),
            new RuntimeExecutionPlanner(),
            CreateFixedTimeProvider());
    }

    private static FakePlanCoordinator CreateFakePlanCoordinator() => new();

    private static FakeReconciler CreateFakeReconciler() => new();

    private static FakeTimeProvider CreateFixedTimeProvider() => new(FixedTime);

    private static RuntimePlanSnapshot CreatePlanSnapshot()
    {
        return new RuntimePlanSnapshot
        {
            Configuration = new(),
            Observed = new(),
            Desired = new(),
            PlannedAt = FixedTime
        };
    }

    private static RuntimeChangeSet CreateSingleAddEndpointChangeSet()
    {
        return new RuntimeChangeSet
        {
            Changes =
            [
                new RuntimeChange
                {
                    Kind = RuntimeChangeKind.AddEndpointRoute,
                    Identity = "5.160.74.148/32 via 192.168.1.1",
                    DestinationPrefix = "5.160.74.148/32",
                    Gateway = "192.168.1.1",
                    InterfaceIndex = 10,
                    Metric = 1,
                    Description =
                        "Protect VPN endpoint 5.160.74.148/32 " +
                        "through 192.168.1.1."
                }
            ]
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

        public int ReconcileCallCount { get; private set; }
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
            ReconcileCallCount++;
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

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _fixed;

        public int GetUtcNowCallCount { get; private set; }

        public FakeTimeProvider(DateTimeOffset fixedTime)
        {
            _fixed = fixedTime;
        }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCallCount++;
            return _fixed;
        }
    }
}
