namespace PathVeer.Core.Tests.Runtime;

using PathVeer.Core.Configuration;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Reconciliation;

public sealed class RuntimeCycleCoordinatorTests
{
    [Fact]
    public void Constructor_ThrowsOnNullDecisionBuilder()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RuntimeCycleCoordinator(null!));
    }

    [Fact]
    public async Task RunCycleAsync_CallsBuilderExactlyOnce()
    {
        FakeDecisionBuilder builder = new();
        RuntimeCycleCoordinator coordinator = new(builder);

        await coordinator.RunCycleAsync();

        Assert.Equal(1, builder.BuildCallCount);
    }

    [Fact]
    public async Task RunCycleAsync_ReturnsExactDecisionReference()
    {
        RuntimeDecision expected = CreateDecision();
        FakeDecisionBuilder builder = new(expected);
        RuntimeCycleCoordinator coordinator = new(builder);

        RuntimeDecision actual = await coordinator.RunCycleAsync();

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task RunCycleAsync_ForwardsCancellationToken()
    {
        using CancellationTokenSource cts = new();
        FakeDecisionBuilder builder = new();
        RuntimeCycleCoordinator coordinator = new(builder);

        await coordinator.RunCycleAsync(cts.Token);

        Assert.Equal(cts.Token, builder.ReceivedToken);
    }

    [Fact]
    public async Task RunCycleAsync_CancellationPropagates()
    {
        using CancellationTokenSource cts = new();
        FakeDecisionBuilder builder = new(cancelOnBuild: true);
        RuntimeCycleCoordinator coordinator = new(builder);

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => coordinator.RunCycleAsync(cts.Token));
    }

    [Fact]
    public async Task RunCycleAsync_BuilderExceptionPropagates()
    {
        FakeDecisionBuilder builder = new(
            exception: new InvalidOperationException("fail"));
        RuntimeCycleCoordinator coordinator = new(builder);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.RunCycleAsync());

        Assert.Equal("fail", ex.Message);
    }

    [Fact]
    public async Task RunCycleAsync_PerformsNoPlanConstruction()
    {
        FakeDecisionBuilder builder = new();
        RuntimeCycleCoordinator coordinator = new(builder);

        await coordinator.RunCycleAsync();

        Assert.False(builder.PlanBuiltDirectly);
    }

    [Fact]
    public async Task RunCycleAsync_PerformsNoReconciliation()
    {
        FakeDecisionBuilder builder = new();
        RuntimeCycleCoordinator coordinator = new(builder);

        await coordinator.RunCycleAsync();

        Assert.False(builder.ReconciliationPerformed);
    }

    [Fact]
    public async Task RunCycleAsync_PerformsNoExecutionPlanning()
    {
        FakeDecisionBuilder builder = new();
        RuntimeCycleCoordinator coordinator = new(builder);

        await coordinator.RunCycleAsync();

        Assert.False(builder.ExecutionPlanBuilt);
    }

    [Fact]
    public async Task RunCycleAsync_DoesNotReadClock()
    {
        FakeDecisionBuilder builder = new();
        RuntimeCycleCoordinator coordinator = new(builder);

        await coordinator.RunCycleAsync();

        Assert.False(builder.ClockWasRead);
    }

    private static RuntimeDecision CreateDecision()
    {
        return RuntimeDecision.Create(
            new RuntimePlanSnapshot
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
            },
            RuntimeReconciliationResult.NoChanges(),
            new RuntimeExecutionPlan { Steps = [] },
            DateTimeOffset.UtcNow);
    }

    private sealed class FakeDecisionBuilder : IRuntimeDecisionBuilder
    {
        private readonly RuntimeDecision? _decision;
        private readonly bool _cancelOnBuild;
        private readonly Exception? _exception;

        public int BuildCallCount { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }
        public bool PlanBuiltDirectly { get; private set; }
        public bool ReconciliationPerformed { get; private set; }
        public bool ExecutionPlanBuilt { get; private set; }
        public bool ClockWasRead { get; private set; }

        public FakeDecisionBuilder(
            RuntimeDecision? decision = null,
            bool cancelOnBuild = false,
            Exception? exception = null)
        {
            _decision = decision;
            _cancelOnBuild = cancelOnBuild;
            _exception = exception;
        }

        public Task<RuntimeDecision> BuildAsync(
            CancellationToken cancellationToken = default)
        {
            BuildCallCount++;
            ReceivedToken = cancellationToken;

            if (_cancelOnBuild)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(
                _decision ?? CreateDecision());
        }
    }
}
