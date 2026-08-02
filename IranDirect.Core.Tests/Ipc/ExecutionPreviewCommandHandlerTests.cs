using IranDirect.Core.Ipc;
using IranDirect.Core.Planning;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Reconciliation;

namespace IranDirect.Core.Tests.Ipc;

public sealed class ExecutionPreviewCommandHandlerTests
{
    [Fact]
    public async Task GetAsync_ReturnsSuccessWithPreview()
    {
        RuntimeDecision decision =
            CreateDecisionWithOneStep();

        ExecutionPreview expectedPreview = CreatePreview();

        FakeDecisionBuilder decisionBuilder =
            new(decision);
        FakePreviewBuilder previewBuilder =
            new(expectedPreview);

        ExecutionPreviewCommandHandler handler =
            new(decisionBuilder, previewBuilder);

        ServiceResponse response =
            await handler.GetAsync();

        Assert.True(response.Success);
        Assert.Same(expectedPreview, response.Preview);
        Assert.False(
            string.IsNullOrWhiteSpace(response.Message));
    }

    [Fact]
    public async Task GetAsync_BuildsDecisionExactlyOnce()
    {
        FakeDecisionBuilder decisionBuilder =
            new(CreateDecisionWithOneStep());
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        ExecutionPreviewCommandHandler handler =
            new(decisionBuilder, previewBuilder);

        await handler.GetAsync();

        Assert.Equal(1, decisionBuilder.BuildCallCount);
    }

    [Fact]
    public async Task GetAsync_BuildsPreviewExactlyOnce()
    {
        FakeDecisionBuilder decisionBuilder =
            new(CreateDecisionWithOneStep());
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        ExecutionPreviewCommandHandler handler =
            new(decisionBuilder, previewBuilder);

        await handler.GetAsync();

        Assert.Equal(1, previewBuilder.BuildCallCount);
    }

    [Fact]
    public async Task GetAsync_PassesDecisionToPreviewBuilder()
    {
        RuntimeDecision decision =
            CreateDecisionWithOneStep();

        FakeDecisionBuilder decisionBuilder =
            new(decision);
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        ExecutionPreviewCommandHandler handler =
            new(decisionBuilder, previewBuilder);

        await handler.GetAsync();

        Assert.Same(decision, previewBuilder.ReceivedDecision);
    }

    [Fact]
    public async Task GetAsync_EmptyPlan_ShowsNoChangesMessage()
    {
        RuntimeDecision decision =
            CreateEmptyDecision();

        ExecutionPreview emptyPreview = CreatePreview(
            hasChanges: false);

        FakeDecisionBuilder decisionBuilder =
            new(decision);
        FakePreviewBuilder previewBuilder =
            new(emptyPreview);

        ExecutionPreviewCommandHandler handler =
            new(decisionBuilder, previewBuilder);

        ServiceResponse response =
            await handler.GetAsync();

        Assert.Contains(
            "No changes would be applied",
            response.Message);
    }

    [Fact]
    public async Task GetAsync_WithChanges_ShowsComputedMessage()
    {
        RuntimeDecision decision =
            CreateDecisionWithOneStep();

        ExecutionPreview preview =
            CreatePreview(hasChanges: true);

        FakeDecisionBuilder decisionBuilder =
            new(decision);
        FakePreviewBuilder previewBuilder =
            new(preview);

        ExecutionPreviewCommandHandler handler =
            new(decisionBuilder, previewBuilder);

        ServiceResponse response =
            await handler.GetAsync();

        Assert.Contains(
            "Execution preview computed",
            response.Message);
    }

    [Fact]
    public async Task GetAsync_WithCancellation_Propagates()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        FakeDecisionBuilder decisionBuilder = new(
            CreateDecisionWithOneStep());
        FakePreviewBuilder previewBuilder =
            new(CreatePreview());

        ExecutionPreviewCommandHandler handler =
            new(decisionBuilder, previewBuilder);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.GetAsync(cts.Token));
    }

    [Fact]
    public void Constructor_NullDecisionBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ExecutionPreviewCommandHandler(
                null!,
                new FakePreviewBuilder(CreatePreview())));
    }

    [Fact]
    public void Constructor_NullPreviewBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ExecutionPreviewCommandHandler(
                new FakeDecisionBuilder(
                    CreateDecisionWithOneStep()),
                null!));
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

    private static RuntimeDecision CreateEmptyDecision()
    {
        RuntimeChangeSet changeSet = new();

        RuntimeReconciliationResult reconciliation =
            RuntimeReconciliationResult.NoChanges(
                "No changes required.");

        RuntimeExecutionPlan executionPlan =
            new() { Steps = [] };

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

    private static ExecutionPreview CreatePreview(
        bool hasChanges = false)
    {
        return new ExecutionPreview
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount =
                    hasChanges ? 1 : 0,
                DeleteCount = 0,
                VerifyCount = 0,
                InventoryUpdates =
                    hasChanges ? 1 : 0,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = 0
            },
            Steps =
                hasChanges
                    ?
                    [
                        new ExecutionPreviewStep
                        {
                            Category =
                                ExecutionPreviewCategory
                                    .Route,
                            Operation =
                                ExecutionPreviewOperation
                                    .Create,
                            Target = "10.0.0.0/24",
                            Reason = "Missing route"
                        }
                    ]
                    : []
        };
    }

    private sealed class FakeDecisionBuilder :
        IRuntimeDecisionBuilder
    {
        private readonly RuntimeDecision _decision;

        public FakeDecisionBuilder(RuntimeDecision decision)
        {
            _decision = decision;
        }

        public int BuildCallCount { get; private set; }

        public Task<RuntimeDecision> BuildAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BuildCallCount++;
            return Task.FromResult(_decision);
        }
    }

    private sealed class FakePreviewBuilder :
        IExecutionPreviewBuilder
    {
        private readonly ExecutionPreview _preview;

        public FakePreviewBuilder(ExecutionPreview preview)
        {
            _preview = preview;
        }

        public int BuildCallCount { get; private set; }

        public RuntimeDecision? ReceivedDecision
            { get; private set; }

        public ExecutionPreview Build(
            RuntimeDecision decision)
        {
            BuildCallCount++;
            ReceivedDecision = decision;
            return _preview;
        }
    }
}
