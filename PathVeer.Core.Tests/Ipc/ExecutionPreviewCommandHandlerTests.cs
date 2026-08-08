using PathVeer.Core.Ipc;
using PathVeer.Core.Planning;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Reconciliation;

namespace PathVeer.Core.Tests.Ipc;

public sealed class ExecutionPreviewCommandHandlerTests
{
    [Fact]
    public async Task GetAsync_ReturnsSuccessWithPreview()
    {
        ExecutionPreview expectedPreview = CreatePreview();

        FakePlanner planner = new(expectedPreview);

        ExecutionPreviewCommandHandler handler =
            new(planner);

        ServiceResponse response =
            await handler.GetAsync();

        Assert.True(response.Success);
        Assert.Same(expectedPreview, response.Preview);
        Assert.False(
            string.IsNullOrWhiteSpace(response.Message));
    }

    [Fact]
    public async Task GetAsync_CallsPlannerExactlyOnce()
    {
        FakePlanner planner = new(CreatePreview());

        ExecutionPreviewCommandHandler handler =
            new(planner);

        await handler.GetAsync();

        Assert.Equal(1, planner.BuildPreviewCallCount);
    }

    [Fact]
    public async Task GetAsync_EmptyPlan_ShowsNoChangesMessage()
    {
        ExecutionPreview emptyPreview = CreatePreview(
            hasChanges: false);

        FakePlanner planner = new(emptyPreview);

        ExecutionPreviewCommandHandler handler =
            new(planner);

        ServiceResponse response =
            await handler.GetAsync();

        Assert.Contains(
            "No changes would be applied",
            response.Message);
    }

    [Fact]
    public async Task GetAsync_WithChanges_ShowsComputedMessage()
    {
        ExecutionPreview preview =
            CreatePreview(hasChanges: true);

        FakePlanner planner = new(preview);

        ExecutionPreviewCommandHandler handler =
            new(planner);

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

        FakePlanner planner = new(CreatePreview());

        ExecutionPreviewCommandHandler handler =
            new(planner);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.GetAsync(cts.Token));
    }

    [Fact]
    public async Task GetAsync_PlannerException_Propagates()
    {
        FakePlanner planner = new(CreatePreview(), throws: true);

        ExecutionPreviewCommandHandler handler =
            new(planner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.GetAsync());
    }

    [Fact]
    public void Constructor_NullPlanner_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ExecutionPreviewCommandHandler(null!));
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

    private sealed class FakePlanner : IRuntimePreviewPlanner
    {
        private readonly ExecutionPreview _preview;
        private readonly bool _throws;

        public FakePlanner(
            ExecutionPreview preview,
            bool throws = false)
        {
            _preview = preview;
            _throws = throws;
        }

        public int BuildPreviewCallCount { get; private set; }

        public Task<RuntimeDecision> BuildDecisionAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_throws)
            {
                throw new InvalidOperationException(
                    "Planner failed.");
            }

            return Task.FromResult(
                CreateDecisionWithOneStep());
        }

        public Task<ExecutionPreview> BuildPreviewAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_throws)
            {
                throw new InvalidOperationException(
                    "Planner failed.");
            }

            BuildPreviewCallCount++;
            return Task.FromResult(_preview);
        }
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
}
