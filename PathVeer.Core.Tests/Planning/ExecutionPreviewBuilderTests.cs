using PathVeer.Core.Planning;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Reconciliation;

namespace PathVeer.Core.Tests.Planning;

public sealed class ExecutionPreviewBuilderTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly FakeTimeProvider FixedTimeProvider =
        new(FixedTime);

    [Fact]
    public void Constructor_NullTimeProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ExecutionPreviewBuilder(null!));
    }

    [Fact]
    public void Build_NullDecision_Throws()
    {
        ExecutionPreviewBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentNullException>(
            () => builder.Build(null!));
    }

    [Fact]
    public void Build_AddPrefixRoute_MapsToCreateRoute()
    {
        RuntimeDecision decision =
            CreateDecisionWithSingleStep(
                RuntimeExecutionStepKind.AddPrefixRoute,
                "203.0.113.0/24");

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Single(preview.Steps);
        Assert.Equal(
            ExecutionPreviewCategory.Route,
            preview.Steps[0].Category);
        Assert.Equal(
            ExecutionPreviewOperation.Create,
            preview.Steps[0].Operation);
        Assert.Equal(
            "203.0.113.0/24",
            preview.Steps[0].Target);
    }

    [Fact]
    public void Build_RemovePrefixRoute_MapsToDeleteRoute()
    {
        RuntimeDecision decision =
            CreateDecisionWithSingleStep(
                RuntimeExecutionStepKind.RemovePrefixRoute,
                "203.0.113.0/24");

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Single(preview.Steps);
        Assert.Equal(
            ExecutionPreviewCategory.Route,
            preview.Steps[0].Category);
        Assert.Equal(
            ExecutionPreviewOperation.Delete,
            preview.Steps[0].Operation);
    }

    [Fact]
    public void Build_AddEndpointRoute_MapsToCreateVpnEndpoint()
    {
        RuntimeDecision decision =
            CreateDecisionWithSingleStep(
                RuntimeExecutionStepKind.AddEndpointRoute,
                "5.160.74.148/32");

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Single(preview.Steps);
        Assert.Equal(
            ExecutionPreviewCategory.VpnEndpoint,
            preview.Steps[0].Category);
        Assert.Equal(
            ExecutionPreviewOperation.Create,
            preview.Steps[0].Operation);
    }

    [Fact]
    public void Build_RemoveEndpointRoute_MapsToDeleteVpnEndpoint()
    {
        RuntimeDecision decision =
            CreateDecisionWithSingleStep(
                RuntimeExecutionStepKind.RemoveEndpointRoute,
                "5.160.74.148/32");

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Single(preview.Steps);
        Assert.Equal(
            ExecutionPreviewCategory.VpnEndpoint,
            preview.Steps[0].Category);
        Assert.Equal(
            ExecutionPreviewOperation.Delete,
            preview.Steps[0].Operation);
    }

    [Fact]
    public void Build_MixedPlan_PreservesExactOrder()
    {
        RuntimeDecision decision =
            CreateDecisionWithSteps(
            [
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddEndpointRoute,
                    "A"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .RemovePrefixRoute,
                    "B"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddPrefixRoute,
                    "C"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .RemoveEndpointRoute,
                    "D")
            ]);

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(4, preview.Steps.Count);
        Assert.Equal(
            ExecutionPreviewCategory.VpnEndpoint,
            preview.Steps[0].Category);
        Assert.Equal(
            ExecutionPreviewOperation.Create,
            preview.Steps[0].Operation);
        Assert.Equal(
            ExecutionPreviewCategory.Route,
            preview.Steps[1].Category);
        Assert.Equal(
            ExecutionPreviewOperation.Delete,
            preview.Steps[1].Operation);
        Assert.Equal(
            ExecutionPreviewCategory.Route,
            preview.Steps[2].Category);
        Assert.Equal(
            ExecutionPreviewOperation.Create,
            preview.Steps[2].Operation);
        Assert.Equal(
            ExecutionPreviewCategory.VpnEndpoint,
            preview.Steps[3].Category);
        Assert.Equal(
            ExecutionPreviewOperation.Delete,
            preview.Steps[3].Operation);
    }

    [Fact]
    public void Build_SummaryCreateCount_EqualsCreateOperations()
    {
        RuntimeDecision decision =
            CreateDecisionWithSteps(
            [
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddPrefixRoute,
                    "A"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddEndpointRoute,
                    "B"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddPrefixRoute,
                    "C")
            ]);

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(3, preview.Summary.CreateCount);
    }

    [Fact]
    public void Build_SummaryDeleteCount_EqualsDeleteOperations()
    {
        RuntimeDecision decision =
            CreateDecisionWithSteps(
            [
                CreateStep(
                    RuntimeExecutionStepKind
                        .RemovePrefixRoute,
                    "A"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .RemoveEndpointRoute,
                    "B")
            ]);

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(2, preview.Summary.DeleteCount);
    }

    [Fact]
    public void Build_SummaryVpnEndpointUpdates_EqualsEndpointSteps()
    {
        RuntimeDecision decision =
            CreateDecisionWithSteps(
            [
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddEndpointRoute,
                    "A"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .RemoveEndpointRoute,
                    "B"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddPrefixRoute,
                    "C")
            ]);

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(2, preview.Summary.VpnEndpointUpdates);
    }

    [Fact]
    public void Build_SummaryInventoryUpdates_EqualsPrefixRouteSteps()
    {
        RuntimeDecision decision =
            CreateDecisionWithSteps(
            [
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddPrefixRoute,
                    "A"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .RemovePrefixRoute,
                    "B"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddEndpointRoute,
                    "C")
            ]);

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(2, preview.Summary.InventoryUpdates);
    }

    [Fact]
    public void Build_SummaryVerifyCount_IsZero()
    {
        RuntimeDecision decision =
            CreateDecisionWithSingleStep(
                RuntimeExecutionStepKind.AddPrefixRoute,
                "10.0.0.0/8");

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(0, preview.Summary.VerifyCount);
    }

    [Fact]
    public void Build_SummaryCustomRouteUpdates_IsZero()
    {
        RuntimeDecision decision =
            CreateDecisionWithSingleStep(
                RuntimeExecutionStepKind.AddPrefixRoute,
                "10.0.0.0/8");

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(0, preview.Summary.CustomRouteUpdates);
    }

    [Fact]
    public void Build_EmptyPlan_ReturnsEmptyStepsAndAllZeroCounts()
    {
        RuntimeDecision decision = CreateEmptyDecision();

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Empty(preview.Steps);
        Assert.Equal(0, preview.Summary.CreateCount);
        Assert.Equal(0, preview.Summary.DeleteCount);
        Assert.Equal(0, preview.Summary.VerifyCount);
        Assert.Equal(0, preview.Summary.InventoryUpdates);
        Assert.Equal(0, preview.Summary.CustomRouteUpdates);
        Assert.Equal(0, preview.Summary.VpnEndpointUpdates);
        Assert.False(preview.HasChanges);
    }

    [Fact]
    public void Build_EmptyPlan_ReturnsEmptyCategories()
    {
        RuntimeDecision decision = CreateEmptyDecision();

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Empty(preview.Categories);
    }

    [Fact]
    public void Build_TargetFallbackToIdentity_WhenPrefixEmpty()
    {
        RuntimeDecision decision =
            CreateDecisionWithSteps(
            [
                new RuntimeExecutionStep
                {
                    Kind =
                        RuntimeExecutionStepKind
                            .AddPrefixRoute,
                    Identity = "fallback-identity",
                    DestinationPrefix = "",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "test"
                }
            ]);

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(
            "fallback-identity",
            preview.Steps[0].Target);
    }

    [Fact]
    public void Build_TargetFallbackToIdentity_WhenPrefixNull()
    {
        RuntimeDecision decision =
            CreateDecisionWithSteps(
            [
                new RuntimeExecutionStep
                {
                    Kind =
                        RuntimeExecutionStepKind
                            .AddPrefixRoute,
                    Identity = "fallback-id",
                    DestinationPrefix = "",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "test"
                }
            ]);

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(
            "fallback-id",
            preview.Steps[0].Target);
    }

    [Fact]
    public void Build_TargetUsesPrefix_WhenPresent()
    {
        RuntimeDecision decision =
            CreateDecisionWithSingleStep(
                RuntimeExecutionStepKind.AddPrefixRoute,
                "203.0.113.0/24");

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(
            "203.0.113.0/24",
            preview.Steps[0].Target);
    }

    [Fact]
    public void Build_Reasons_AreDeterministic()
    {
        RuntimeDecision decision =
            CreateDecisionWithSteps(
            [
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddPrefixRoute,
                    "A"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .RemovePrefixRoute,
                    "B"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddEndpointRoute,
                    "C"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .RemoveEndpointRoute,
                    "D")
            ]);

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(
            "Desired prefix route is missing.",
            preview.Steps[0].Reason);
        Assert.Equal(
            "Owned prefix route is no longer desired.",
            preview.Steps[1].Reason);
        Assert.Equal(
            "VPN endpoint protection route is missing.",
            preview.Steps[2].Reason);
        Assert.Equal(
            "Owned VPN endpoint route is no longer required.",
            preview.Steps[3].Reason);
    }

    [Fact]
    public void Build_CapturedAt_FromInjectedTimeProvider()
    {
        RuntimeDecision decision =
            CreateDecisionWithSingleStep(
                RuntimeExecutionStepKind.AddPrefixRoute,
                "10.0.0.0/8");

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(FixedTime, preview.CapturedAt);
    }

    [Fact]
    public void Build_TimeProviderCalledExactlyOnce()
    {
        RuntimeDecision decision =
            CreateDecisionWithSingleStep(
                RuntimeExecutionStepKind.AddPrefixRoute,
                "10.0.0.0/8");

        FakeTimeProvider timeProvider = new(FixedTime);
        ExecutionPreviewBuilder builder =
            new(timeProvider);

        builder.Build(decision);

        Assert.Equal(1, timeProvider.GetUtcNowCallCount);
    }

    [Fact]
    public void Build_AllFourStepKinds_AreExhaustivelyHandled()
    {
        RuntimeDecision decision =
            CreateDecisionWithSteps(
            [
                CreateStep(
                    RuntimeExecutionStepKind.AddPrefixRoute,
                    "A"),
                CreateStep(
                    RuntimeExecutionStepKind.RemovePrefixRoute,
                    "B"),
                CreateStep(
                    RuntimeExecutionStepKind.AddEndpointRoute,
                    "C"),
                CreateStep(
                    RuntimeExecutionStepKind.RemoveEndpointRoute,
                    "D")
            ]);

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(4, preview.Steps.Count);

        ExecutionPreviewCategory[] expectedCategories =
        [
            ExecutionPreviewCategory.Route,
            ExecutionPreviewCategory.Route,
            ExecutionPreviewCategory.VpnEndpoint,
            ExecutionPreviewCategory.VpnEndpoint
        ];

        ExecutionPreviewOperation[] expectedOperations =
        [
            ExecutionPreviewOperation.Create,
            ExecutionPreviewOperation.Delete,
            ExecutionPreviewOperation.Create,
            ExecutionPreviewOperation.Delete
        ];

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(
                expectedCategories[i],
                preview.Steps[i].Category);
            Assert.Equal(
                expectedOperations[i],
                preview.Steps[i].Operation);
        }
    }

    [Fact]
    public void Build_SourceDecision_NotMutated()
    {
        RuntimeDecision decision =
            CreateDecisionWithSingleStep(
                RuntimeExecutionStepKind.AddPrefixRoute,
                "10.0.0.0/8");

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(1, decision.ExecutionPlan.Count);
        Assert.Equal(
            RuntimeExecutionStepKind.AddPrefixRoute,
            decision.ExecutionPlan.Steps[0].Kind);
        Assert.False(preview.Steps[0].Equals(
            decision.ExecutionPlan.Steps[0]));
    }

    [Fact]
    public void Build_HasChanges_TrueWhenCreateOrDelete()
    {
        RuntimeDecision decision =
            CreateDecisionWithSingleStep(
                RuntimeExecutionStepKind.AddPrefixRoute,
                "10.0.0.0/8");

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.True(preview.HasChanges);
    }

    [Fact]
    public void Build_HasChanges_FalseForEmptyPlan()
    {
        RuntimeDecision decision = CreateEmptyDecision();

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.False(preview.HasChanges);
    }

    [Fact]
    public void Build_Categories_GroupStepsCorrectly()
    {
        RuntimeDecision decision =
            CreateDecisionWithSteps(
            [
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddPrefixRoute,
                    "A"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddEndpointRoute,
                    "B"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddPrefixRoute,
                    "C")
            ]);

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(2, preview.Categories.Count);
        Assert.Equal(
            2,
            preview.Categories[
                ExecutionPreviewCategory.Route].Count);
        Assert.Single(
            preview.Categories[
                ExecutionPreviewCategory.VpnEndpoint]);
    }

    [Fact]
    public void Build_MultipleCalls_ProduceEquivalentResults()
    {
        RuntimeDecision decision =
            CreateDecisionWithSteps(
            [
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddPrefixRoute,
                    "A"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .RemoveEndpointRoute,
                    "B")
            ]);

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview first = builder.Build(decision);
        ExecutionPreview second = builder.Build(decision);

        Assert.Equal(
            first.Steps.Count,
            second.Steps.Count);
        Assert.Equal(
            first.Summary.CreateCount,
            second.Summary.CreateCount);
        Assert.Equal(
            first.Summary.DeleteCount,
            second.Summary.DeleteCount);
        Assert.Equal(
            first.Steps[0].Target,
            second.Steps[0].Target);
        Assert.Equal(
            first.Steps[1].Target,
            second.Steps[1].Target);
    }

    [Fact]
    public void Build_SummaryEstimatedOperations_IsSumOfCounts()
    {
        RuntimeDecision decision =
            CreateDecisionWithSteps(
            [
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddPrefixRoute,
                    "A"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .AddPrefixRoute,
                    "B"),
                CreateStep(
                    RuntimeExecutionStepKind
                        .RemoveEndpointRoute,
                    "C")
            ]);

        ExecutionPreviewBuilder builder = CreateBuilder();

        ExecutionPreview preview = builder.Build(decision);

        Assert.Equal(
            preview.Summary.CreateCount +
            preview.Summary.DeleteCount +
            preview.Summary.VerifyCount +
            preview.Summary.InventoryUpdates +
            preview.Summary.CustomRouteUpdates +
            preview.Summary.VpnEndpointUpdates,
            preview.Summary.EstimatedOperations);
    }

    private static ExecutionPreviewBuilder CreateBuilder()
    {
        return new ExecutionPreviewBuilder(
            FixedTimeProvider);
    }

    private static RuntimeDecision CreateDecisionWithSingleStep(
        RuntimeExecutionStepKind kind,
        string destinationPrefix)
    {
        return CreateDecisionWithSteps(
        [
            CreateStep(kind, destinationPrefix)
        ]);
    }

    private static RuntimeDecision CreateDecisionWithSteps(
        RuntimeExecutionStep[] steps)
    {
        RuntimeChangeSet changeSet =
            new()
            {
                Changes = steps.Select(MapToChange).ToList()
            };

        RuntimeReconciliationResult reconciliation =
            RuntimeReconciliationResult.Planned(changeSet);

        RuntimeExecutionPlan executionPlan =
            new() { Steps = steps };

        return RuntimeDecision.Create(
            CreatePlanSnapshot(),
            reconciliation,
            executionPlan,
            FixedTime);
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
            CreatePlanSnapshot(),
            reconciliation,
            executionPlan,
            FixedTime);
    }

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

    private static RuntimeExecutionStep CreateStep(
        RuntimeExecutionStepKind kind,
        string destinationPrefix)
    {
        return new RuntimeExecutionStep
        {
            Kind = kind,
            Identity =
                $"{destinationPrefix} via 192.168.1.1",
            DestinationPrefix = destinationPrefix,
            Gateway = "192.168.1.1",
            InterfaceIndex = 10,
            Metric = 1,
            Description = $"Test {kind} for {destinationPrefix}."
        };
    }

    private static RuntimeChange MapToChange(
        RuntimeExecutionStep step)
    {
        RuntimeChangeKind kind = step.Kind switch
        {
            RuntimeExecutionStepKind.AddEndpointRoute =>
                RuntimeChangeKind.AddEndpointRoute,
            RuntimeExecutionStepKind.RemoveEndpointRoute =>
                RuntimeChangeKind.RemoveEndpointRoute,
            RuntimeExecutionStepKind.AddPrefixRoute =>
                RuntimeChangeKind.AddPrefixRoute,
            RuntimeExecutionStepKind.RemovePrefixRoute =>
                RuntimeChangeKind.RemovePrefixRoute,
            _ => throw new ArgumentOutOfRangeException(
                nameof(step.Kind), step.Kind, null)
        };

        return new RuntimeChange
        {
            Kind = kind,
            Identity = step.Identity,
            DestinationPrefix = step.DestinationPrefix,
            Gateway = step.Gateway,
            InterfaceIndex = step.InterfaceIndex,
            Metric = step.Metric,
            Description = step.Description
        };
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
