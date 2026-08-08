using PathVeer.Core.Planning;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Reconciliation;

namespace PathVeer.Core.Tests.Planning;

public sealed class RuntimePreviewPlannerTests
{
    [Fact]
    public async Task BuildDecisionAsync_CallsDecisionBuilderExactlyOnce()
    {
        FakeDecisionBuilder decisionBuilder = new(
            CreateDecisionWithOneStep());
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        RuntimePreviewPlanner planner =
            new(decisionBuilder, previewBuilder);

        await planner.BuildDecisionAsync();

        Assert.Equal(1, decisionBuilder.BuildCallCount);
    }

    [Fact]
    public async Task BuildDecisionAsync_ReturnsSameInstance()
    {
        RuntimeDecision expectedDecision =
            CreateDecisionWithOneStep();

        FakeDecisionBuilder decisionBuilder =
            new(expectedDecision);
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        RuntimePreviewPlanner planner =
            new(decisionBuilder, previewBuilder);

        RuntimeDecision result =
            await planner.BuildDecisionAsync();

        Assert.Same(expectedDecision, result);
    }

    [Fact]
    public async Task BuildDecisionAsync_PropagatesCancellation()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        FakeDecisionBuilder decisionBuilder =
            new(CreateDecisionWithOneStep());
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        RuntimePreviewPlanner planner =
            new(decisionBuilder, previewBuilder);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => planner.BuildDecisionAsync(cts.Token));
    }

    [Fact]
    public async Task BuildDecisionAsync_PropagatesException()
    {
        FakeDecisionBuilder decisionBuilder =
            new(throws: true);
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        RuntimePreviewPlanner planner =
            new(decisionBuilder, previewBuilder);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => planner.BuildDecisionAsync());
    }

    [Fact]
    public async Task BuildPreviewAsync_CallsDecisionBuilderExactlyOnce()
    {
        FakeDecisionBuilder decisionBuilder =
            new(CreateDecisionWithOneStep());
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        RuntimePreviewPlanner planner =
            new(decisionBuilder, previewBuilder);

        await planner.BuildPreviewAsync();

        Assert.Equal(1, decisionBuilder.BuildCallCount);
    }

    [Fact]
    public async Task BuildPreviewAsync_CallsPreviewBuilderExactlyOnce()
    {
        FakeDecisionBuilder decisionBuilder =
            new(CreateDecisionWithOneStep());
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        RuntimePreviewPlanner planner =
            new(decisionBuilder, previewBuilder);

        await planner.BuildPreviewAsync();

        Assert.Equal(1, previewBuilder.BuildCallCount);
    }

    [Fact]
    public async Task BuildPreviewAsync_PassesExactDecisionToPreviewBuilder()
    {
        RuntimeDecision expectedDecision =
            CreateDecisionWithOneStep();

        FakeDecisionBuilder decisionBuilder =
            new(expectedDecision);
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        RuntimePreviewPlanner planner =
            new(decisionBuilder, previewBuilder);

        await planner.BuildPreviewAsync();

        Assert.Same(
            expectedDecision,
            previewBuilder.ReceivedDecision);
    }

    [Fact]
    public async Task BuildPreviewAsync_ReturnsSamePreviewInstance()
    {
        ExecutionPreview expectedPreview = CreatePreview();

        FakeDecisionBuilder decisionBuilder =
            new(CreateDecisionWithOneStep());
        FakePreviewBuilder previewBuilder =
            new(expectedPreview);

        RuntimePreviewPlanner planner =
            new(decisionBuilder, previewBuilder);

        ExecutionPreview result =
            await planner.BuildPreviewAsync();

        Assert.Same(expectedPreview, result);
    }

    [Fact]
    public async Task BuildPreviewAsync_PropagatesPreviewBuilderException()
    {
        FakeDecisionBuilder decisionBuilder =
            new(CreateDecisionWithOneStep());
        FakePreviewBuilder previewBuilder =
            new(CreatePreview(), throws: true);

        RuntimePreviewPlanner planner =
            new(decisionBuilder, previewBuilder);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => planner.BuildPreviewAsync());
    }

    [Fact]
    public async Task BuildPreviewAsync_PropagatesCancellation()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        FakeDecisionBuilder decisionBuilder =
            new(CreateDecisionWithOneStep());
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        RuntimePreviewPlanner planner =
            new(decisionBuilder, previewBuilder);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => planner.BuildPreviewAsync(cts.Token));
    }

    [Fact]
    public async Task RepeatedCalls_DoNotCacheResults()
    {
        RuntimeDecision decision1 = CreateDecisionWithOneStep();
        RuntimeDecision decision2 = CreateDecisionWithOneStep();

        FakeDecisionBuilder decisionBuilder =
            new(decision1, decision2);
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        RuntimePreviewPlanner planner =
            new(decisionBuilder, previewBuilder);

        RuntimeDecision result1 =
            await planner.BuildDecisionAsync();
        RuntimeDecision result2 =
            await planner.BuildDecisionAsync();

        Assert.Equal(2, decisionBuilder.BuildCallCount);
        Assert.Same(decision1, result1);
        Assert.Same(decision2, result2);
    }

    [Fact]
    public void Constructor_NullDecisionBuilder_Throws()
    {
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        Assert.Throws<ArgumentNullException>(
            () => new RuntimePreviewPlanner(
                null!, previewBuilder));
    }

    [Fact]
    public void Constructor_NullPreviewBuilder_Throws()
    {
        FakeDecisionBuilder decisionBuilder =
            new(CreateDecisionWithOneStep());

        Assert.Throws<ArgumentNullException>(
            () => new RuntimePreviewPlanner(
                decisionBuilder, null!));
    }

    private static RuntimeDecision CreateDecisionWithOneStep()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                new RuntimeChange
                {
                    Kind =
                        RuntimeChangeKind.AddPrefixRoute,
                    Identity = "10.0.0.0/24 via 10.0.0.1",
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "Add route 10.0.0.0/24"
                }
            ]
        };

        RuntimeReconciliationResult reconciliation =
            RuntimeReconciliationResult.Planned(changeSet);

        RuntimeExecutionPlan executionPlan =
            new()
            {
                Steps =
                [
                    new RuntimeExecutionStep
                    {
                        Kind =
                            RuntimeExecutionStepKind
                                .AddPrefixRoute,
                        Identity =
                            "10.0.0.0/24 via 10.0.0.1",
                        DestinationPrefix = "10.0.0.0/24",
                        Gateway = "10.0.0.1",
                        InterfaceIndex = 1,
                        Metric = 1,
                        Description =
                            "Add route 10.0.0.0/24"
                    }
                ]
            };

        return RuntimeDecision.Create(
            new RuntimePlanSnapshot
            {
                Configuration = new(),
                Observed = new(),
                Desired = new(),
                PlannedAt = DateTimeOffset.UtcNow
            },
            reconciliation,
            executionPlan,
            DateTimeOffset.UtcNow);
    }

    private static ExecutionPreview CreatePreview()
    {
        return new ExecutionPreview
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = 1,
                DeleteCount = 0,
                VerifyCount = 0,
                InventoryUpdates = 1,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = 0
            },
            Steps =
            [
                new ExecutionPreviewStep
                {
                    Category =
                        ExecutionPreviewCategory.Route,
                    Operation =
                        ExecutionPreviewOperation.Create,
                    Target = "10.0.0.0/24",
                    Reason = "Missing route"
                }
            ]
        };
    }

    private sealed class FakeDecisionBuilder :
        IRuntimeDecisionBuilder
    {
        private readonly IReadOnlyList<RuntimeDecision>
            _decisions;

        private readonly bool _throws;

        private int _callIndex;

        public FakeDecisionBuilder(
            params RuntimeDecision[] decisions)
        {
            _decisions = decisions;
        }

        public FakeDecisionBuilder(
            bool throws = false)
        {
            _throws = throws;
            _decisions = [];
        }

        public int BuildCallCount { get; private set; }

        public Task<RuntimeDecision> BuildAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_throws)
            {
                throw new InvalidOperationException(
                    "Decision build failed.");
            }

            BuildCallCount++;
            return Task.FromResult(
                _decisions[
                    Math.Min(
                        _callIndex++,
                        _decisions.Count - 1)]);
        }
    }

    private sealed class FakePreviewBuilder :
        IExecutionPreviewBuilder
    {
        private readonly ExecutionPreview _preview;
        private readonly bool _throws;

        public FakePreviewBuilder(
            ExecutionPreview preview,
            bool throws = false)
        {
            _preview = preview;
            _throws = throws;
        }

        public int BuildCallCount { get; private set; }

        public RuntimeDecision? ReceivedDecision
            { get; private set; }

        public ExecutionPreview Build(
            RuntimeDecision decision)
        {
            if (_throws)
            {
                throw new InvalidOperationException(
                    "Preview build failed.");
            }

            BuildCallCount++;
            ReceivedDecision = decision;
            return _preview;
        }
    }
}
