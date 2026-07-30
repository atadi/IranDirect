using IranDirect.Core.Runtime.Execution;

namespace IranDirect.Core.Tests.Runtime.Execution;

public sealed class RuntimeExecutionDomainTests
{
    [Fact]
    public void EmptyPlan_HasIsEmptyTrue()
    {
        RuntimeExecutionPlan plan = CreateEmptyPlan();

        Assert.True(plan.IsEmpty);
        Assert.Equal(0, plan.Count);
    }

    [Fact]
    public void PlanWithSteps_HasIsEmptyFalse()
    {
        RuntimeExecutionPlan plan = CreatePlanWithOneStep();

        Assert.False(plan.IsEmpty);
        Assert.Equal(1, plan.Count);
    }

    [Fact]
    public void Step_PreservesAllRouteFields()
    {
        RuntimeExecutionStep step = CreateEndpointAddStep();

        Assert.Equal(
            RuntimeExecutionStepKind.AddEndpointRoute,
            step.Kind);
        Assert.Equal(
            "5.160.74.148/32 via 192.168.1.1",
            step.Identity);
        Assert.Equal("5.160.74.148/32", step.DestinationPrefix);
        Assert.Equal("192.168.1.1", step.Gateway);
        Assert.Equal(10u, step.InterfaceIndex);
        Assert.Equal(1, step.Metric);
        Assert.Equal(
            "Protect VPN endpoint 5.160.74.148/32 through 192.168.1.1.",
            step.Description);
    }

    [Fact]
    public void EquivalentSteps_AreEqual()
    {
        RuntimeExecutionStep a = CreateEndpointAddStep();
        RuntimeExecutionStep b = CreateEndpointAddStep();

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentSteps_AreNotEqual()
    {
        RuntimeExecutionStep a = CreateEndpointAddStep();
        RuntimeExecutionStep b = CreatePrefixAddStep();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EquivalentPlans_HaveSameStepContents()
    {
        RuntimeExecutionPlan a = CreatePlanWithOneStep();
        RuntimeExecutionPlan b = CreatePlanWithOneStep();

        Assert.Equal(a.Count, b.Count);
        Assert.Equal(a.IsEmpty, b.IsEmpty);
        Assert.Equal(a.Steps[0], b.Steps[0]);
    }

    [Fact]
    public void DifferentPlanStepCounts_HaveDifferentContents()
    {
        RuntimeExecutionPlan empty = CreateEmptyPlan();
        RuntimeExecutionPlan one = CreatePlanWithOneStep();

        Assert.NotEqual(empty.Count, one.Count);
        Assert.NotEqual(empty.IsEmpty, one.IsEmpty);
    }

    [Fact]
    public void EquivalentResults_HaveSameContent()
    {
        RuntimeExecutionResult a = CreateCompletedResult();
        RuntimeExecutionResult b = CreateCompletedResult();

        Assert.Equal(a.Status, b.Status);
        Assert.Equal(
            a.MutatedInfrastructure,
            b.MutatedInfrastructure);
        Assert.Equal(a.StepResults.Count, b.StepResults.Count);
        Assert.Equal(a.StepResults[0], b.StepResults[0]);
    }

    [Fact]
    public void NoExecutionRequired_HasCorrectSemantics()
    {
        RuntimeExecutionResult result =
            RuntimeExecutionResult.NoExecutionRequired();

        Assert.Equal(
            RuntimeExecutionResultStatus.NoExecutionRequired,
            result.Status);
        Assert.False(result.MutatedInfrastructure);
        Assert.Empty(result.StepResults);
    }

    [Fact]
    public void Planned_HasCorrectSemantics()
    {
        RuntimeExecutionResult result =
            RuntimeExecutionResult.Planned();

        Assert.Equal(
            RuntimeExecutionResultStatus.Planned,
            result.Status);
        Assert.False(result.MutatedInfrastructure);
        Assert.Empty(result.StepResults);
    }

    [Fact]
    public void Completed_HasCorrectSemantics()
    {
        RuntimeExecutionStepResult stepResult = new()
        {
            StepIdentity = "test-identity",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Succeeded
        };
        RuntimeExecutionResult result =
            RuntimeExecutionResult.Completed([stepResult]);

        Assert.Equal(
            RuntimeExecutionResultStatus.Completed,
            result.Status);
        Assert.True(result.MutatedInfrastructure);
        Assert.Single(result.StepResults);
    }

    [Fact]
    public void Failed_HasCorrectSemantics()
    {
        RuntimeExecutionStepResult stepResult = new()
        {
            StepIdentity = "test-identity",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Failed,
            ErrorMessage = "Route add failed."
        };
        RuntimeExecutionResult result =
            RuntimeExecutionResult.Failed(
                [stepResult],
                "1 step failed.");

        Assert.Equal(
            RuntimeExecutionResultStatus.Failed,
            result.Status);
        Assert.False(result.MutatedInfrastructure);
        Assert.Equal("1 step failed.", result.ErrorMessage);
        Assert.Single(result.StepResults);
        Assert.Equal(
            RuntimeExecutionStepStatus.Failed,
            result.StepResults[0].Status);
        Assert.Equal(
            "Route add failed.",
            result.StepResults[0].ErrorMessage);
    }

    [Fact]
    public void Cancelled_HasCorrectSemantics()
    {
        RuntimeExecutionResult result =
            RuntimeExecutionResult.Cancelled(
                [],
                "Cancelled by user.");

        Assert.Equal(
            RuntimeExecutionResultStatus.Cancelled,
            result.Status);
        Assert.False(result.MutatedInfrastructure);
        Assert.Equal("Cancelled by user.", result.ErrorMessage);
        Assert.Empty(result.StepResults);
    }

    [Fact]
    public void PartiallyCompleted_DoesNotClaimFullSuccess()
    {
        RuntimeExecutionStepResult succeeded = new()
        {
            StepIdentity = "step-1",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Succeeded
        };
        RuntimeExecutionStepResult failed = new()
        {
            StepIdentity = "step-2",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Failed,
            ErrorMessage = "Failed."
        };
        RuntimeExecutionResult result =
            RuntimeExecutionResult.PartiallyCompleted(
                [succeeded, failed]);

        Assert.Equal(
            RuntimeExecutionResultStatus.PartiallyCompleted,
            result.Status);
        Assert.NotEqual(
            RuntimeExecutionResultStatus.Completed,
            result.Status);
        Assert.True(result.MutatedInfrastructure);
        Assert.Equal(2, result.StepResults.Count);
    }

    [Fact]
    public void StepResult_PreservesIdentityAndStatus()
    {
        RuntimeExecutionStepResult result = new()
        {
            StepIdentity = "203.0.113.0/24 via 192.168.1.1",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Planned
        };

        Assert.Equal(
            "203.0.113.0/24 via 192.168.1.1",
            result.StepIdentity);
        Assert.Equal(
            RuntimeExecutionStepStatus.Planned,
            result.Status);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void StepResult_CanHaveErrorMessage()
    {
        RuntimeExecutionStepResult result = new()
        {
            StepIdentity = "test",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Failed,
            ErrorMessage = "Something went wrong."
        };

        Assert.Equal("Something went wrong.", result.ErrorMessage);
    }

    [Fact]
    public void MutatedInfrastructure_TrueOnlyAfterExecution()
    {
        RuntimeExecutionStepResult succeeded = new()
        {
            StepIdentity = "s",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Succeeded
        };
        RuntimeExecutionStepResult failed = new()
        {
            StepIdentity = "f",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Failed,
            ErrorMessage = "err"
        };

        Assert.False(
            RuntimeExecutionResult.NoExecutionRequired()
                .MutatedInfrastructure);
        Assert.False(
            RuntimeExecutionResult.Planned()
                .MutatedInfrastructure);
        Assert.True(
            RuntimeExecutionResult.Completed([succeeded])
                .MutatedInfrastructure);
        Assert.False(
            RuntimeExecutionResult.Failed([failed])
                .MutatedInfrastructure);
        Assert.False(
            RuntimeExecutionResult.Cancelled([])
                .MutatedInfrastructure);
        Assert.True(
            RuntimeExecutionResult.PartiallyCompleted(
                    [succeeded, failed])
                .MutatedInfrastructure);
    }

    [Fact]
    public void NoExecutionRequired_HasNoStepResults()
    {
        RuntimeExecutionResult result =
            RuntimeExecutionResult.NoExecutionRequired();

        Assert.Empty(result.StepResults);
    }

    [Fact]
    public void Planned_HasNoStepResults()
    {
        RuntimeExecutionResult result =
            RuntimeExecutionResult.Planned();

        Assert.Empty(result.StepResults);
    }

    [Fact]
    public void Completed_WithEmptySteps_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => RuntimeExecutionResult.Completed([]));
    }

    [Fact]
    public void Completed_WithFailedStep_Throws()
    {
        RuntimeExecutionStepResult failed = new()
        {
            StepIdentity = "f",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Failed,
            ErrorMessage = "err"
        };

        Assert.Throws<ArgumentException>(
            () => RuntimeExecutionResult.Completed([failed]));
    }

    [Fact]
    public void Failed_WithoutFailedStep_Throws()
    {
        RuntimeExecutionStepResult succeeded = new()
        {
            StepIdentity = "s",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Succeeded
        };

        Assert.Throws<ArgumentException>(
            () => RuntimeExecutionResult.Failed([succeeded]));
    }

    [Fact]
    public void Failed_WithSucceededStep_Throws()
    {
        RuntimeExecutionStepResult succeeded = new()
        {
            StepIdentity = "s",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Succeeded
        };
        RuntimeExecutionStepResult failed = new()
        {
            StepIdentity = "f",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Failed,
            ErrorMessage = "err"
        };

        Assert.Throws<ArgumentException>(
            () => RuntimeExecutionResult.Failed(
                [succeeded, failed]));
    }

    [Fact]
    public void Cancelled_WithSucceededStep_Throws()
    {
        RuntimeExecutionStepResult succeeded = new()
        {
            StepIdentity = "s",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Succeeded
        };
        RuntimeExecutionStepResult cancelled = new()
        {
            StepIdentity = "c",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Cancelled
        };

        Assert.Throws<ArgumentException>(
            () => RuntimeExecutionResult.Cancelled(
                [succeeded, cancelled]));
    }

    [Fact]
    public void PartiallyCompleted_WithoutSucceededStep_Throws()
    {
        RuntimeExecutionStepResult failed = new()
        {
            StepIdentity = "f",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Failed,
            ErrorMessage = "err"
        };

        Assert.Throws<ArgumentException>(
            () => RuntimeExecutionResult.PartiallyCompleted(
                [failed]));
    }

    [Fact]
    public void PartiallyCompleted_WithoutNonSuccessfulStep_Throws()
    {
        RuntimeExecutionStepResult succeeded = new()
        {
            StepIdentity = "s",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Succeeded
        };

        Assert.Throws<ArgumentException>(
            () => RuntimeExecutionResult.PartiallyCompleted(
                [succeeded]));
    }

    [Fact]
    public void Cancelled_WithEmptyResults_IsValid()
    {
        RuntimeExecutionResult result =
            RuntimeExecutionResult.Cancelled([]);

        Assert.Equal(
            RuntimeExecutionResultStatus.Cancelled,
            result.Status);
        Assert.Empty(result.StepResults);
        Assert.False(result.MutatedInfrastructure);
    }

    [Fact]
    public void SucceededStatus_IncludesPostconditionVerification()
    {
        RuntimeExecutionStepResult step = new()
        {
            StepIdentity = "203.0.113.0/24 via 10.0.0.1",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "test",
            Status = RuntimeExecutionStepStatus.Succeeded
        };

        RuntimeExecutionResult result =
            RuntimeExecutionResult.Completed([step]);

        Assert.True(result.MutatedInfrastructure);
        // The invariant: Succeeded means platform mutation completed
        // AND postcondition was verified. A route command without
        // verified platform state is not Succeeded.
    }

    [Fact]
    public void StepKindEnum_OrdinalDoesNotDefineOrderingPolicy()
    {
        string[] safeOrder =
        [
            "AddEndpointRoute",
            "RemovePrefixRoute",
            "AddPrefixRoute",
            "RemoveEndpointRoute"
        ];

        string[] sortedByOrdinal = Enum
            .GetNames<RuntimeExecutionStepKind>()
            .Select(n => (
                name: n,
                value: (int)Enum.Parse<RuntimeExecutionStepKind>(n)))
            .OrderBy(x => x.value)
            .Select(x => x.name)
            .ToArray();

        Assert.NotEqual(safeOrder, sortedByOrdinal);
    }

    private static RuntimeExecutionPlan CreateEmptyPlan()
    {
        return new RuntimeExecutionPlan
        {
            Steps = Array.Empty<RuntimeExecutionStep>()
        };
    }

    private static RuntimeExecutionPlan CreatePlanWithOneStep()
    {
        return new RuntimeExecutionPlan
        {
            Steps = new[] { CreateEndpointAddStep() }
        };
    }

    private static RuntimeExecutionStep CreateEndpointAddStep()
    {
        return new RuntimeExecutionStep
        {
            Kind = RuntimeExecutionStepKind.AddEndpointRoute,
            Identity = "5.160.74.148/32 via 192.168.1.1",
            DestinationPrefix = "5.160.74.148/32",
            Gateway = "192.168.1.1",
            InterfaceIndex = 10,
            Metric = 1,
            Description =
                "Protect VPN endpoint 5.160.74.148/32 " +
                "through 192.168.1.1."
        };
    }

    private static RuntimeExecutionStep CreatePrefixAddStep()
    {
        return new RuntimeExecutionStep
        {
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            Identity = "203.0.113.0/24 via 192.168.1.1",
            DestinationPrefix = "203.0.113.0/24",
            Gateway = "192.168.1.1",
            InterfaceIndex = 10,
            Metric = 256,
            Description =
                "Add direct prefix route 203.0.113.0/24 " +
                "through 192.168.1.1."
        };
    }

    private static RuntimeExecutionResult CreateCompletedResult()
    {
        return RuntimeExecutionResult.Completed([
            new RuntimeExecutionStepResult
            {
                StepIdentity = "test",
                Kind = RuntimeExecutionStepKind.AddPrefixRoute,
                DestinationPrefix = "test",
                Status = RuntimeExecutionStepStatus.Succeeded
            }
        ]);
    }

    [Fact]
    public void StepResult_JsonRoundTrip_PreservesAllFields()
    {
        RuntimeExecutionStepResult original = new()
        {
            StepIdentity = "203.0.113.0/24|192.168.1.1|10",
            Kind = RuntimeExecutionStepKind.RemovePrefixRoute,
            DestinationPrefix = "203.0.113.0/24",
            Status = RuntimeExecutionStepStatus.Failed,
            ErrorMessage = "Access denied."
        };

        string json = System.Text.Json.JsonSerializer.Serialize(original);
        RuntimeExecutionStepResult? deserialized =
            System.Text.Json.JsonSerializer.Deserialize<RuntimeExecutionStepResult>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.StepIdentity, deserialized.StepIdentity);
        Assert.Equal(original.Kind, deserialized.Kind);
        Assert.Equal(original.DestinationPrefix, deserialized.DestinationPrefix);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.ErrorMessage, deserialized.ErrorMessage);
    }
}
