using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Reconciliation;

namespace IranDirect.Core.Tests.Runtime;

public sealed class RuntimeDecisionTests
{
    private readonly RuntimePlanSnapshot _validPlan = CreateValidPlan();
    private readonly DateTimeOffset _validDecidedAt =
        new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_NullPlan_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => RuntimeDecision.Create(
                null!,
                CreateNoChangesReconciliation(),
                CreateEmptyPlan(),
                _validDecidedAt));
    }

    [Fact]
    public void Create_NullReconciliation_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => RuntimeDecision.Create(
                _validPlan,
                null!,
                CreateEmptyPlan(),
                _validDecidedAt));
    }

    [Fact]
    public void Create_NullExecutionPlan_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => RuntimeDecision.Create(
                _validPlan,
                CreateNoChangesReconciliation(),
                null!,
                _validDecidedAt));
    }

    [Fact]
    public void Create_DefaultDecidedAt_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => RuntimeDecision.Create(
                _validPlan,
                CreateNoChangesReconciliation(),
                CreateEmptyPlan(),
                default));
    }

    [Fact]
    public void Create_NoChangesRequired_AcceptsEmptyPlan()
    {
        RuntimeDecision decision = RuntimeDecision.Create(
            _validPlan,
            CreateNoChangesReconciliation(),
            CreateEmptyPlan(),
            _validDecidedAt);

        Assert.Equal(
            RuntimeReconciliationStatus.NoChangesRequired,
            decision.Reconciliation.Status);
        Assert.True(decision.ExecutionPlan.IsEmpty);
    }

    [Fact]
    public void Create_NoChangesRequired_RejectsNonEmptyPlan()
    {
        Assert.Throws<ArgumentException>(
            () => RuntimeDecision.Create(
                _validPlan,
                CreateNoChangesReconciliation(),
                CreatePlanWithOneStep(),
                _validDecidedAt));
    }

    [Fact]
    public void Create_ChangesPlanned_AcceptsCompatibleNonEmptyPlan()
    {
        RuntimeChangeSet changeSet = CreateSingleAddEndpointChangeSet();
        RuntimeExecutionPlan plan = CreatePlanFromChanges(changeSet);

        RuntimeDecision decision = RuntimeDecision.Create(
            _validPlan,
            CreatePlannedReconciliation(changeSet),
            plan,
            _validDecidedAt);

        Assert.Equal(
            RuntimeReconciliationStatus.ChangesPlanned,
            decision.Reconciliation.Status);
        Assert.False(decision.ExecutionPlan.IsEmpty);
    }

    [Fact]
    public void Create_ChangesPlanned_RejectsEmptyPlan()
    {
        RuntimeChangeSet changeSet = CreateSingleAddEndpointChangeSet();

        Assert.Throws<ArgumentException>(
            () => RuntimeDecision.Create(
                _validPlan,
                CreatePlannedReconciliation(changeSet),
                CreateEmptyPlan(),
                _validDecidedAt));
    }

    [Fact]
    public void Create_ChangesPlanned_RejectsEmptyChangeSet()
    {
        RuntimeExecutionPlan plan = CreatePlanWithOneStep();

        RuntimeReconciliationResult invalid =
            new RuntimeReconciliationResult
            {
                Status = RuntimeReconciliationStatus.ChangesPlanned
            };

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => RuntimeDecision.Create(
                _validPlan,
                invalid,
                plan,
                _validDecidedAt));

        Assert.Contains("ChangesPlanned", ex.Message);
    }

    [Fact]
    public void Create_Blocked_AcceptsEmptyPlan()
    {
        RuntimeDecision decision = RuntimeDecision.Create(
            _validPlan,
            RuntimeReconciliationResult.Blocked(["Blocked"]),
            CreateEmptyPlan(),
            _validDecidedAt);

        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            decision.Reconciliation.Status);
        Assert.True(decision.ExecutionPlan.IsEmpty);
    }

    [Fact]
    public void Create_Blocked_RejectsNonEmptyPlan()
    {
        Assert.Throws<ArgumentException>(
            () => RuntimeDecision.Create(
                _validPlan,
                RuntimeReconciliationResult.Blocked(["Blocked"]),
                CreatePlanWithOneStep(),
                _validDecidedAt));
    }

    [Fact]
    public void Create_Failed_AcceptsEmptyPlan()
    {
        RuntimeDecision decision = RuntimeDecision.Create(
            _validPlan,
            RuntimeReconciliationResult.Failed(["Failed"]),
            CreateEmptyPlan(),
            _validDecidedAt);

        Assert.Equal(
            RuntimeReconciliationStatus.Failed,
            decision.Reconciliation.Status);
        Assert.True(decision.ExecutionPlan.IsEmpty);
    }

    [Fact]
    public void Create_Failed_RejectsNonEmptyPlan()
    {
        Assert.Throws<ArgumentException>(
            () => RuntimeDecision.Create(
                _validPlan,
                RuntimeReconciliationResult.Failed(["Failed"]),
                CreatePlanWithOneStep(),
                _validDecidedAt));
    }

    [Fact]
    public void Create_ChangesApplied_Throws()
    {
        RuntimeChangeSet changeSet = CreateSingleAddEndpointChangeSet();
        RuntimeExecutionPlan plan = CreatePlanFromChanges(changeSet);

        Assert.Throws<ArgumentException>(
            () => RuntimeDecision.Create(
                _validPlan,
                RuntimeReconciliationResult.Applied(changeSet),
                plan,
                _validDecidedAt));
    }

    [Fact]
    public void Create_PreservesExactPlanReference()
    {
        RuntimeDecision decision = RuntimeDecision.Create(
            _validPlan,
            CreateNoChangesReconciliation(),
            CreateEmptyPlan(),
            _validDecidedAt);

        Assert.Same(_validPlan, decision.Plan);
    }

    [Fact]
    public void Create_PreservesExactReconciliationReference()
    {
        RuntimeReconciliationResult reconciliation =
            CreateNoChangesReconciliation();

        RuntimeDecision decision = RuntimeDecision.Create(
            _validPlan,
            reconciliation,
            CreateEmptyPlan(),
            _validDecidedAt);

        Assert.Same(reconciliation, decision.Reconciliation);
    }

    [Fact]
    public void Create_PreservesExactExecutionPlanReference()
    {
        RuntimeExecutionPlan plan = CreateEmptyPlan();

        RuntimeDecision decision = RuntimeDecision.Create(
            _validPlan,
            CreateNoChangesReconciliation(),
            plan,
            _validDecidedAt);

        Assert.Same(plan, decision.ExecutionPlan);
    }

    [Fact]
    public void Create_PreservesDecidedAtExactly()
    {
        RuntimeDecision decision = RuntimeDecision.Create(
            _validPlan,
            CreateNoChangesReconciliation(),
            CreateEmptyPlan(),
            _validDecidedAt);

        Assert.Equal(_validDecidedAt, decision.DecidedAt);
    }

    [Fact]
    public void Create_IncompatibleIdentities_Throws()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                new RuntimeChange
                {
                    Kind = RuntimeChangeKind.AddEndpointRoute,
                    Identity = "identity-A",
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "A"
                }
            ]
        };

        RuntimeExecutionPlan plan = new()
        {
            Steps =
            [
                new RuntimeExecutionStep
                {
                    Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                    Identity = "identity-B",
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "B"
                }
            ]
        };

        Assert.Throws<ArgumentException>(
            () => RuntimeDecision.Create(
                _validPlan,
                CreatePlannedReconciliation(changeSet),
                plan,
                _validDecidedAt));
    }

    [Fact]
    public void Create_IncompatibleKinds_Throws()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                new RuntimeChange
                {
                    Kind = RuntimeChangeKind.AddEndpointRoute,
                    Identity = "test-id",
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "test"
                }
            ]
        };

        RuntimeExecutionPlan plan = new()
        {
            Steps =
            [
                new RuntimeExecutionStep
                {
                    Kind = RuntimeExecutionStepKind.RemoveEndpointRoute,
                    Identity = "test-id",
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "test"
                }
            ]
        };

        Assert.Throws<ArgumentException>(
            () => RuntimeDecision.Create(
                _validPlan,
                CreatePlannedReconciliation(changeSet),
                plan,
                _validDecidedAt));
    }

    [Fact]
    public void Create_DuplicateChanges_TraceableCorrectly()
    {
        RuntimeChange change1 = new()
        {
            Kind = RuntimeChangeKind.AddPrefixRoute,
            Identity = "dup-id",
            DestinationPrefix = "10.0.0.0/24",
            Gateway = "10.0.0.1",
            InterfaceIndex = 1,
            Metric = 1,
            Description = "first"
        };

        RuntimeChange change2 = new()
        {
            Kind = RuntimeChangeKind.AddPrefixRoute,
            Identity = "dup-id",
            DestinationPrefix = "10.0.0.0/24",
            Gateway = "10.0.0.1",
            InterfaceIndex = 1,
            Metric = 1,
            Description = "second"
        };

        RuntimeChangeSet changeSet = new()
        {
            Changes = [change1, change2]
        };

        RuntimeExecutionPlan plan = new()
        {
            Steps =
            [
                new RuntimeExecutionStep
                {
                    Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                    Identity = "dup-id",
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "first"
                },
                new RuntimeExecutionStep
                {
                    Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                    Identity = "dup-id",
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "second"
                }
            ]
        };

        RuntimeDecision decision = RuntimeDecision.Create(
            _validPlan,
            CreatePlannedReconciliation(changeSet),
            plan,
            _validDecidedAt);

        Assert.Equal(2, decision.Reconciliation.ChangeSet.Count);
        Assert.Equal(2, decision.ExecutionPlan.Count);
    }

    [Fact]
    public void Create_ValidDecision_DoesNotRecalculateOrder()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                new RuntimeChange
                {
                    Kind = RuntimeChangeKind.RemoveEndpointRoute,
                    Identity = "D",
                    DestinationPrefix = "10.0.3.0/24",
                    Gateway = "10.0.0.3",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "D"
                },
                new RuntimeChange
                {
                    Kind = RuntimeChangeKind.AddEndpointRoute,
                    Identity = "A",
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "A"
                }
            ]
        };

        RuntimeExecutionPlan plan = new()
        {
            Steps =
            [
                new RuntimeExecutionStep
                {
                    Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                    Identity = "A",
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "A"
                },
                new RuntimeExecutionStep
                {
                    Kind = RuntimeExecutionStepKind.RemoveEndpointRoute,
                    Identity = "D",
                    DestinationPrefix = "10.0.3.0/24",
                    Gateway = "10.0.0.3",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "D"
                }
            ]
        };

        RuntimeDecision decision = RuntimeDecision.Create(
            _validPlan,
            CreatePlannedReconciliation(changeSet),
            plan,
            _validDecidedAt);

        Assert.Equal(2, decision.ExecutionPlan.Count);
        Assert.Equal(
            RuntimeExecutionStepKind.AddEndpointRoute,
            decision.ExecutionPlan.Steps[0].Kind);
        Assert.Equal(
            RuntimeExecutionStepKind.RemoveEndpointRoute,
            decision.ExecutionPlan.Steps[1].Kind);
    }

    [Fact]
    public void Create_HasNoMutationProperty()
    {
        var decisionType = typeof(RuntimeDecision);
        var mutationProp = decisionType.GetProperty("MutatedInfrastructure");

        Assert.Null(mutationProp);
    }

    [Fact]
    public void Create_EquivalentInputs_ProduceEquivalentDecisions()
    {
        RuntimeChangeSet changeSet = CreateSingleAddEndpointChangeSet();
        RuntimeExecutionPlan plan = CreatePlanFromChanges(changeSet);

        RuntimeDecision a = RuntimeDecision.Create(
            _validPlan,
            CreatePlannedReconciliation(changeSet),
            plan,
            _validDecidedAt);

        RuntimeDecision b = RuntimeDecision.Create(
            _validPlan,
            CreatePlannedReconciliation(changeSet),
            plan,
            _validDecidedAt);

        Assert.Equal(a.Plan, b.Plan);
        Assert.Equal(a.Reconciliation.Status, b.Reconciliation.Status);
        Assert.Equal(a.ExecutionPlan.Count, b.ExecutionPlan.Count);
        Assert.Equal(a.ExecutionPlan.Steps[0], b.ExecutionPlan.Steps[0]);
    }

    private static RuntimePlanSnapshot CreateValidPlan()
    {
        return new RuntimePlanSnapshot
        {
            Configuration = new(),
            Observed = new(),
            Desired = new(),
            PlannedAt = DateTimeOffset.UtcNow
        };
    }

    private static RuntimeReconciliationResult CreateNoChangesReconciliation()
    {
        return RuntimeReconciliationResult.NoChanges();
    }

    private static RuntimeReconciliationResult CreatePlannedReconciliation(
        RuntimeChangeSet changeSet)
    {
        return RuntimeReconciliationResult.Planned(changeSet);
    }

    private static RuntimeExecutionPlan CreateEmptyPlan()
    {
        return new RuntimeExecutionPlan { Steps = [] };
    }

    private static RuntimeExecutionPlan CreatePlanWithOneStep()
    {
        return new RuntimeExecutionPlan
        {
            Steps =
            [
                new RuntimeExecutionStep
                {
                    Kind = RuntimeExecutionStepKind.AddEndpointRoute,
                    Identity = "test-id",
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = "test"
                }
            ]
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

    private static RuntimeExecutionPlan CreatePlanFromChanges(
        RuntimeChangeSet changeSet)
    {
        RuntimeExecutionStep[] steps = changeSet.Changes
            .Select(change => new RuntimeExecutionStep
            {
                Kind = change.Kind switch
                {
                    RuntimeChangeKind.AddEndpointRoute =>
                        RuntimeExecutionStepKind.AddEndpointRoute,
                    RuntimeChangeKind.RemoveEndpointRoute =>
                        RuntimeExecutionStepKind.RemoveEndpointRoute,
                    RuntimeChangeKind.AddPrefixRoute =>
                        RuntimeExecutionStepKind.AddPrefixRoute,
                    RuntimeChangeKind.RemovePrefixRoute =>
                        RuntimeExecutionStepKind.RemovePrefixRoute,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(change.Kind), change.Kind, null)
                },
                Identity = change.Identity,
                DestinationPrefix = change.DestinationPrefix,
                Gateway = change.Gateway,
                InterfaceIndex = change.InterfaceIndex,
                Metric = change.Metric,
                Description = change.Description
            })
            .ToArray();

        return new RuntimeExecutionPlan { Steps = steps };
    }
}
