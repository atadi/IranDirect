using PathVeer.Core.Planning;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Reconciliation;

namespace PathVeer.Core.Tests.Planning;

/// <summary>
/// Exact-equivalence tests comparing the optimized <see cref="ExecutionPreviewBuilder"/>
/// against <see cref="ReferenceExecutionPreviewBuilder"/> (pre-change algorithm).
/// Uses structural equality, not parsed-JSON equality, for every field and the
/// grouped <see cref="ExecutionPreview.Categories"/> map.
/// </summary>
public sealed class ExecutionPreviewBuilderEquivalenceTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeProvider FixedTimeProvider =
        new FakeTimeProvider(FixedTime);

    private static ExecutionPreviewBuilder CreateOptimized() =>
        new(FixedTimeProvider);

    private static ReferenceExecutionPreviewBuilder CreateReference() =>
        new(FixedTimeProvider);

    private static void AssertEquivalent(
        string label,
        ExecutionPreview optimized,
        ExecutionPreview reference)
    {
        // NOTE: full record equality (Assert.Equal(reference, optimized)) is
        // intentionally NOT used here because ExecutionPreview.Steps is an
        // IReadOnlyList (compared by reference, not structurally) and the
        // cached Categories backing field differs by instance; the checks
        // below assert exact structural equivalence instead.
        Assert.Equal(reference.CapturedAt, optimized.CapturedAt);
        Assert.Equal(reference.HasChanges, optimized.HasChanges);
        Assert.Equal(
            reference.Summary.EstimatedOperations,
            optimized.Summary.EstimatedOperations);

        // Per-step exact order + field equality.
        Assert.Equal(reference.Steps.Count, optimized.Steps.Count);
        for (int i = 0; i < reference.Steps.Count; i++)
        {
            ExecutionPreviewStep r = reference.Steps[i];
            ExecutionPreviewStep o = optimized.Steps[i];
            Assert.Equal(r.Category, o.Category);
            Assert.Equal(r.Operation, o.Operation);
            Assert.Equal(r.Target, o.Target);
            Assert.Equal(r.Reason, o.Reason);
        }

        // Categories: identical keys, identical per-category step order/values.
        Assert.Equal(reference.Categories.Count, optimized.Categories.Count);
        foreach (var kvp in reference.Categories)
        {
            Assert.True(
                optimized.Categories.TryGetValue(
                    kvp.Key, out var oList),
                $"{label}: missing category {kvp.Key}");
            Assert.Equal(kvp.Value.Count, oList!.Count);
            for (int i = 0; i < kvp.Value.Count; i++)
            {
                Assert.Equal(kvp.Value[i].Target, oList[i].Target);
                Assert.Equal(kvp.Value[i].Reason, oList[i].Reason);
                Assert.Equal(kvp.Value[i].Category, oList[i].Category);
                Assert.Equal(kvp.Value[i].Operation, oList[i].Operation);
            }
        }
    }

    [Fact]
    public void EmptyPlan_Equivalent()
    {
        RuntimeDecision decision = CreateEmptyDecision();

        ExecutionPreview optimized = CreateOptimized().Build(decision);
        ExecutionPreview reference = CreateReference().Build(decision);

        AssertEquivalent("empty", optimized, reference);
        Assert.Empty(optimized.Steps);
        Assert.Empty(optimized.Categories);
        Assert.False(optimized.HasChanges);
    }

    [Fact]
    public void EveryStepKind_Equivalent()
    {
        foreach (RuntimeExecutionStepKind kind in
                 Enum.GetValues<RuntimeExecutionStepKind>())
        {
            RuntimeDecision decision = CreateDecisionWithSteps(
                [CreateStep(kind, "203.0.113.0/24")]);

            ExecutionPreview optimized = CreateOptimized().Build(decision);
            ExecutionPreview reference = CreateReference().Build(decision);

            AssertEquivalent(kind.ToString(), optimized, reference);
        }
    }

    [Fact]
    public void MixedPlan_PreservesOrder_Equivalent()
    {
        RuntimeDecision decision = CreateDecisionWithSteps(
        [
            CreateStep(
                RuntimeExecutionStepKind.AddEndpointRoute, "A"),
            CreateStep(
                RuntimeExecutionStepKind.RemovePrefixRoute, "B"),
            CreateStep(
                RuntimeExecutionStepKind.AddPrefixRoute, "C"),
            CreateStep(
                RuntimeExecutionStepKind.RemoveEndpointRoute, "D")
        ]);

        ExecutionPreview optimized = CreateOptimized().Build(decision);
        ExecutionPreview reference = CreateReference().Build(decision);

        AssertEquivalent("mixed", optimized, reference);
    }

    [Fact]
    public void BlankDestinationPrefix_FallsBackToIdentity_Equivalent()
    {
        RuntimeDecision decision = CreateDecisionWithSteps(
        [
            new RuntimeExecutionStep
            {
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                Identity = "fallback-identity",
                DestinationPrefix = "",
                Gateway = "10.0.0.1",
                InterfaceIndex = 1,
                Metric = 1,
                Description = "blank"
            }
        ]);

        ExecutionPreview optimized = CreateOptimized().Build(decision);
        ExecutionPreview reference = CreateReference().Build(decision);

        AssertEquivalent("blank", optimized, reference);
        Assert.Equal("fallback-identity", optimized.Steps[0].Target);
    }

    [Fact]
    public void WhitespaceDestinationPrefix_FallsBackToIdentity_Equivalent()
    {
        RuntimeDecision decision = CreateDecisionWithSteps(
        [
            new RuntimeExecutionStep
            {
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                Identity = "ws-identity",
                DestinationPrefix = "   ",
                Gateway = "10.0.0.1",
                InterfaceIndex = 1,
                Metric = 1,
                Description = "ws"
            }
        ]);

        ExecutionPreview optimized = CreateOptimized().Build(decision);
        ExecutionPreview reference = CreateReference().Build(decision);

        AssertEquivalent("ws", optimized, reference);
        Assert.Equal("ws-identity", optimized.Steps[0].Target);
    }

    [Fact]
    public void RepeatedIdentities_Equivalent()
    {
        RuntimeDecision decision = CreateDecisionWithSteps(
        [
            CreateStep(
                RuntimeExecutionStepKind.AddPrefixRoute, "same"),
            CreateStep(
                RuntimeExecutionStepKind.AddPrefixRoute, "same"),
            CreateStep(
                RuntimeExecutionStepKind.RemovePrefixRoute, "same")
        ]);

        ExecutionPreview optimized = CreateOptimized().Build(decision);
        ExecutionPreview reference = CreateReference().Build(decision);

        AssertEquivalent("repeated", optimized, reference);
    }

    [Fact]
    public void AllCreate_Equivalent()
    {
        RuntimeDecision decision = CreateDecisionWithSteps(
            Enumerable.Range(0, 50)
                .Select(i => CreateStep(
                    RuntimeExecutionStepKind.AddPrefixRoute,
                    $"10.0.{i}.0/24"))
                .ToArray());

        ExecutionPreview optimized = CreateOptimized().Build(decision);
        ExecutionPreview reference = CreateReference().Build(decision);

        AssertEquivalent("all-create", optimized, reference);
        Assert.Equal(50, optimized.Summary.CreateCount);
        Assert.Equal(0, optimized.Summary.DeleteCount);
    }

    [Fact]
    public void AllDelete_Equivalent()
    {
        RuntimeDecision decision = CreateDecisionWithSteps(
            Enumerable.Range(0, 50)
                .Select(i => CreateStep(
                    RuntimeExecutionStepKind.RemoveEndpointRoute,
                    $"5.6.{i}.0/32"))
                .ToArray());

        ExecutionPreview optimized = CreateOptimized().Build(decision);
        ExecutionPreview reference = CreateReference().Build(decision);

        AssertEquivalent("all-delete", optimized, reference);
        Assert.Equal(50, optimized.Summary.DeleteCount);
        Assert.Equal(50, optimized.Summary.VpnEndpointUpdates);
    }

    [Fact]
    public void AlternatingCreateDelete_Equivalent()
    {
        RuntimeDecision decision = CreateDecisionWithSteps(
            Enumerable.Range(0, 50)
                .Select(i => CreateStep(
                    i % 2 == 0
                        ? RuntimeExecutionStepKind.AddPrefixRoute
                        : RuntimeExecutionStepKind.RemovePrefixRoute,
                    $"10.0.{i}.0/24"))
                .ToArray());

        ExecutionPreview optimized = CreateOptimized().Build(decision);
        ExecutionPreview reference = CreateReference().Build(decision);

        AssertEquivalent("alt", optimized, reference);
        Assert.Equal(25, optimized.Summary.CreateCount);
        Assert.Equal(25, optimized.Summary.DeleteCount);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(5_000)]
    [InlineData(10_000)]
    [InlineData(25_000)]
    [InlineData(50_000)]
    public void LargeDeterministicPlan_Equivalent(int count)
    {
        RuntimeDecision decision = CreateLargeDecision(count);

        ExecutionPreview optimized = CreateOptimized().Build(decision);
        ExecutionPreview reference = CreateReference().Build(decision);

        AssertEquivalent($"large-{count}", optimized, reference);
        Assert.Equal(count, optimized.Steps.Count);
    }

    [Fact]
    public void RepeatedBuilds_AreEquivalent()
    {
        RuntimeDecision decision = CreateLargeDecision(1_000);

        ExecutionPreview firstOpt = CreateOptimized().Build(decision);
        ExecutionPreview firstRef = CreateReference().Build(decision);
        AssertEquivalent("r1", firstOpt, firstRef);

        for (int i = 0; i < 5; i++)
        {
            AssertEquivalent(
                $"r{i}",
                CreateOptimized().Build(decision),
                CreateReference().Build(decision));
        }
    }

    [Fact]
    public void SourcePlan_IsNotMutated()
    {
        RuntimeDecision decision = CreateLargeDecision(1_000);
        int before = decision.ExecutionPlan.Steps.Count;

        _ = CreateOptimized().Build(decision);

        Assert.Equal(before, decision.ExecutionPlan.Steps.Count);
    }

    // ---- helpers ----

    private static RuntimeDecision CreateEmptyDecision()
    {
        RuntimeChangeSet changeSet = new();

        RuntimeReconciliationResult reconciliation =
            RuntimeReconciliationResult.NoChanges(
                "No changes required.");

        RuntimeExecutionPlan executionPlan = new() { Steps = [] };

        return RuntimeDecision.Create(
            CreatePlanSnapshot(),
            reconciliation,
            executionPlan,
            FixedTime);
    }

    private static RuntimeDecision CreateDecisionWithSteps(
        RuntimeExecutionStep[] steps)
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes = steps.Select(MapToChange).ToList()
        };

        RuntimeReconciliationResult reconciliation =
            RuntimeReconciliationResult.Planned(changeSet);

        RuntimeExecutionPlan executionPlan = new() { Steps = steps };

        return RuntimeDecision.Create(
            CreatePlanSnapshot(),
            reconciliation,
            executionPlan,
            FixedTime);
    }

    private static RuntimeDecision CreateLargeDecision(int count)
    {
        RuntimeExecutionStepKind[] kinds =
        [
            RuntimeExecutionStepKind.AddPrefixRoute,
            RuntimeExecutionStepKind.RemovePrefixRoute,
            RuntimeExecutionStepKind.AddEndpointRoute,
            RuntimeExecutionStepKind.RemoveEndpointRoute
        ];

        var steps = new RuntimeExecutionStep[count];
        for (int i = 0; i < count; i++)
        {
            steps[i] = CreateStep(kinds[i % kinds.Length], $"10.{i / 256}.{i % 256}.0/24");
        }

        return CreateDecisionWithSteps(steps);
    }

    private static RuntimePlanSnapshot CreatePlanSnapshot() =>
        new()
        {
            Configuration = new(),
            Observed = new(),
            Desired = new(),
            PlannedAt = FixedTime
        };

    private static RuntimeExecutionStep CreateStep(
        RuntimeExecutionStepKind kind,
        string destinationPrefix) =>
        new()
        {
            Kind = kind,
            Identity = $"{destinationPrefix} via 192.168.1.1",
            DestinationPrefix = destinationPrefix,
            Gateway = "192.168.1.1",
            InterfaceIndex = 10,
            Metric = 1,
            Description = $"Test {kind} for {destinationPrefix}."
        };

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

        public FakeTimeProvider(DateTimeOffset fixedTime)
        {
            _fixed = fixedTime;
        }

        public override DateTimeOffset GetUtcNow() => _fixed;
    }
}
