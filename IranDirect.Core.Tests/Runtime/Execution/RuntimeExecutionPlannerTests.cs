using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Reconciliation;

namespace IranDirect.Core.Tests.Runtime.Execution;

public sealed class RuntimeExecutionPlannerTests
{
    private readonly RuntimeExecutionPlanner _planner = new();

    [Fact]
    public void Plan_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => _planner.Plan(null!));
    }

    [Fact]
    public void Plan_EmptyChangeSet_ReturnsEmptyPlan()
    {
        RuntimeChangeSet changeSet = new();

        RuntimeExecutionPlan plan = _planner.Plan(changeSet);

        Assert.True(plan.IsEmpty);
        Assert.Equal(0, plan.Count);
    }

    [Fact]
    public void Plan_OneAddEndpointChange_MapsToOneAddEndpointStep()
    {
        RuntimeChangeSet changeSet = CreateChangeSet(
            RuntimeChangeKind.AddEndpointRoute,
            "5.160.74.148/32 via 192.168.1.1");

        RuntimeExecutionPlan plan = _planner.Plan(changeSet);

        Assert.False(plan.IsEmpty);
        Assert.Equal(1, plan.Count);
        Assert.Equal(
            RuntimeExecutionStepKind.AddEndpointRoute,
            plan.Steps[0].Kind);
    }

    [Fact]
    public void Plan_OneRemoveEndpointChange_MapsToOneRemoveEndpointStep()
    {
        RuntimeChangeSet changeSet = CreateChangeSet(
            RuntimeChangeKind.RemoveEndpointRoute,
            "5.160.74.148/32 via 192.168.1.1");

        RuntimeExecutionPlan plan = _planner.Plan(changeSet);

        Assert.Equal(
            RuntimeExecutionStepKind.RemoveEndpointRoute,
            plan.Steps[0].Kind);
    }

    [Fact]
    public void Plan_OneAddPrefixChange_MapsToOneAddPrefixStep()
    {
        RuntimeChangeSet changeSet = CreateChangeSet(
            RuntimeChangeKind.AddPrefixRoute,
            "203.0.113.0/24 via 192.168.1.1");

        RuntimeExecutionPlan plan = _planner.Plan(changeSet);

        Assert.Equal(
            RuntimeExecutionStepKind.AddPrefixRoute,
            plan.Steps[0].Kind);
    }

    [Fact]
    public void Plan_OneRemovePrefixChange_MapsToOneRemovePrefixStep()
    {
        RuntimeChangeSet changeSet = CreateChangeSet(
            RuntimeChangeKind.RemovePrefixRoute,
            "203.0.113.0/24 via 192.168.1.1");

        RuntimeExecutionPlan plan = _planner.Plan(changeSet);

        Assert.Equal(
            RuntimeExecutionStepKind.RemovePrefixRoute,
            plan.Steps[0].Kind);
    }

    [Fact]
    public void Plan_AllRouteFields_ArePreservedExactly()
    {
        RuntimeChange change = new()
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
        };
        RuntimeChangeSet changeSet = new() { Changes = [change] };

        RuntimeExecutionPlan plan = _planner.Plan(changeSet);

        RuntimeExecutionStep step = plan.Steps[0];
        Assert.Equal(change.Identity, step.Identity);
        Assert.Equal(change.DestinationPrefix, step.DestinationPrefix);
        Assert.Equal(change.Gateway, step.Gateway);
        Assert.Equal(change.InterfaceIndex, step.InterfaceIndex);
        Assert.Equal(change.Metric, step.Metric);
        Assert.Equal(change.Description, step.Description);
    }

    [Fact]
    public void Plan_Identity_IsPreservedExactlyNotReconstructed()
    {
        string identity = "Custom identity format 192.0.2.0/24 via 10.0.0.1";
        RuntimeChangeSet changeSet = CreateChangeSet(
            RuntimeChangeKind.AddPrefixRoute,
            identity);

        RuntimeExecutionPlan plan = _planner.Plan(changeSet);

        Assert.Equal(identity, plan.Steps[0].Identity);
    }

    [Fact]
    public void Plan_Description_IsPreservedExactly()
    {
        string description = "Custom description, not regenerated.";
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                new RuntimeChange
                {
                    Kind = RuntimeChangeKind.AddPrefixRoute,
                    Identity = "id",
                    DestinationPrefix = "10.0.0.0/24",
                    Gateway = "10.0.0.1",
                    InterfaceIndex = 1,
                    Metric = 1,
                    Description = description
                }
            ]
        };

        RuntimeExecutionPlan plan = _planner.Plan(changeSet);

        Assert.Equal(description, plan.Steps[0].Description);
    }

    [Fact]
    public void Plan_MixedChanges_UsesSafeOrder()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                CreateChange(RuntimeChangeKind.RemoveEndpointRoute, "D"),
                CreateChange(RuntimeChangeKind.RemovePrefixRoute, "C"),
                CreateChange(RuntimeChangeKind.AddPrefixRoute, "B"),
                CreateChange(RuntimeChangeKind.AddEndpointRoute, "A")
            ]
        };

        RuntimeExecutionPlan plan = _planner.Plan(changeSet);

        Assert.Equal(4, plan.Count);
        Assert.Equal(
            RuntimeExecutionStepKind.AddEndpointRoute,
            plan.Steps[0].Kind);
        Assert.Equal(
            RuntimeExecutionStepKind.RemovePrefixRoute,
            plan.Steps[1].Kind);
        Assert.Equal(
            RuntimeExecutionStepKind.AddPrefixRoute,
            plan.Steps[2].Kind);
        Assert.Equal(
            RuntimeExecutionStepKind.RemoveEndpointRoute,
            plan.Steps[3].Kind);
    }

    [Fact]
    public void Plan_SafeOrder_DiffersFromEnumOrdinalOrder()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                CreateChange(RuntimeChangeKind.RemoveEndpointRoute, "id"),
                CreateChange(RuntimeChangeKind.RemovePrefixRoute, "id"),
                CreateChange(RuntimeChangeKind.AddPrefixRoute, "id"),
                CreateChange(RuntimeChangeKind.AddEndpointRoute, "id")
            ]
        };

        RuntimeExecutionPlan plan = _planner.Plan(changeSet);

        RuntimeExecutionStepKind[] safeOrder =
        [
            RuntimeExecutionStepKind.AddEndpointRoute,
            RuntimeExecutionStepKind.RemovePrefixRoute,
            RuntimeExecutionStepKind.AddPrefixRoute,
            RuntimeExecutionStepKind.RemoveEndpointRoute
        ];

        RuntimeExecutionStepKind[] ordinalOrder =
        [
            RuntimeExecutionStepKind.AddEndpointRoute,
            RuntimeExecutionStepKind.RemoveEndpointRoute,
            RuntimeExecutionStepKind.AddPrefixRoute,
            RuntimeExecutionStepKind.RemovePrefixRoute
        ];

        RuntimeExecutionStepKind[] actualOrder =
            plan.Steps.Select(s => s.Kind).ToArray();

        Assert.Equal(safeOrder, actualOrder);
        Assert.NotEqual(ordinalOrder, actualOrder);
    }

    [Fact]
    public void Plan_WithinSameKind_SortedByIdentityOrdinalIgnoreCase()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                CreateChange(RuntimeChangeKind.AddPrefixRoute, "203.0.113.0/24 via b"),
                CreateChange(RuntimeChangeKind.AddPrefixRoute, "203.0.113.0/24 via a"),
                CreateChange(RuntimeChangeKind.AddPrefixRoute, "203.0.113.0/24 via C")
            ]
        };

        RuntimeExecutionPlan plan = _planner.Plan(changeSet);

        Assert.Equal(3, plan.Count);
        Assert.Equal("203.0.113.0/24 via a", plan.Steps[0].Identity);
        Assert.Equal("203.0.113.0/24 via b", plan.Steps[1].Identity);
        Assert.Equal("203.0.113.0/24 via C", plan.Steps[2].Identity);
    }

    [Fact]
    public void Plan_DifferentInputOrder_ProducesEquivalentPlan()
    {
        RuntimeChange a = CreateChange(RuntimeChangeKind.AddEndpointRoute, "B");
        RuntimeChange b = CreateChange(RuntimeChangeKind.AddEndpointRoute, "A");

        RuntimeExecutionPlan planA =
            _planner.Plan(new RuntimeChangeSet { Changes = [a, b] });
        RuntimeExecutionPlan planB =
            _planner.Plan(new RuntimeChangeSet { Changes = [b, a] });

        Assert.Equal(planA.Count, planB.Count);
        Assert.Equal(planA.Steps[0].Identity, planB.Steps[0].Identity);
        Assert.Equal(planA.Steps[1].Identity, planB.Steps[1].Identity);
    }

    [Fact]
    public void Plan_RepeatedCall_ProducesEquivalentPlan()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                CreateChange(RuntimeChangeKind.RemovePrefixRoute, "X"),
                CreateChange(RuntimeChangeKind.AddEndpointRoute, "Y")
            ]
        };

        RuntimeExecutionPlan first = _planner.Plan(changeSet);
        RuntimeExecutionPlan second = _planner.Plan(changeSet);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first.Steps[0].Kind, second.Steps[0].Kind);
        Assert.Equal(first.Steps[1].Kind, second.Steps[1].Kind);
        Assert.Equal(first.Steps[0].Identity, second.Steps[0].Identity);
        Assert.Equal(first.Steps[1].Identity, second.Steps[1].Identity);
    }

    [Fact]
    public void Plan_DuplicateChanges_ArePreserved()
    {
        RuntimeChange dup1 = CreateChange(RuntimeChangeKind.AddPrefixRoute, "dup-id");
        RuntimeChange dup2 = CreateChange(RuntimeChangeKind.AddPrefixRoute, "dup-id");

        RuntimeExecutionPlan plan =
            _planner.Plan(new RuntimeChangeSet { Changes = [dup1, dup2] });

        Assert.Equal(2, plan.Count);
        Assert.Equal("dup-id", plan.Steps[0].Identity);
        Assert.Equal("dup-id", plan.Steps[1].Identity);
    }

    [Fact]
    public void Plan_UnsupportedChangeKind_Throws()
    {
        RuntimeChangeSet changeSet = new()
        {
            Changes =
            [
                new RuntimeChange
                {
                    Kind = (RuntimeChangeKind)999,
                    Identity = "unknown",
                    DestinationPrefix = "0.0.0.0/0",
                    Gateway = "0.0.0.1",
                    InterfaceIndex = 0,
                    Metric = 0,
                    Description = "unknown kind"
                }
            ]
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _planner.Plan(changeSet));
    }

    [Fact]
    public void Plan_StepHasNoStatus_OnlyRouteFields()
    {
        var stepType = typeof(RuntimeExecutionStep);
        var statusProperty = stepType.GetProperty("Status");

        Assert.Null(statusProperty);
    }

    [Fact]
    public void Plan_ReturnsPlanNotResult()
    {
        var method = typeof(RuntimeExecutionPlanner)
            .GetMethod("Plan", [typeof(RuntimeChangeSet)]);

        Assert.NotNull(method);
        Assert.Equal(typeof(RuntimeExecutionPlan), method.ReturnType);
    }

    private static RuntimeChangeSet CreateChangeSet(
        RuntimeChangeKind kind,
        string identity)
    {
        return new RuntimeChangeSet
        {
            Changes =
            [
                CreateChange(kind, identity)
            ]
        };
    }

    private static RuntimeChange CreateChange(
        RuntimeChangeKind kind,
        string identity)
    {
        return new RuntimeChange
        {
            Kind = kind,
            Identity = identity,
            DestinationPrefix = identity.Split(' ')[0],
            Gateway = "192.168.1.1",
            InterfaceIndex = 10,
            Metric = 1,
            Description = $"Test {kind} for {identity}."
        };
    }
}
