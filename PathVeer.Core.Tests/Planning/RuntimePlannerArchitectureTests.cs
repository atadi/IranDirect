using System.Reflection;
using PathVeer.Core.Ipc;
using PathVeer.Core.Planning;
using PathVeer.Core.Runtime;

namespace PathVeer.Core.Tests.Planning;

public sealed class RuntimePreviewPlannerArchitectureTests
{
    [Fact]
    public void RuntimePreviewPlanner_HasExpectedConstructorDependencies()
    {
        ConstructorInfo ctor =
            typeof(RuntimePreviewPlanner)
                .GetConstructors()
                .Single();

        Type[] paramTypes =
            ctor.GetParameters()
                .Select(p => p.ParameterType)
                .ToArray();

        Assert.Equal(2, paramTypes.Length);
        Assert.Contains(
            typeof(IRuntimeDecisionBuilder), paramTypes);
        Assert.Contains(
            typeof(IExecutionPreviewBuilder), paramTypes);
    }

    [Fact]
    public void ExecutionPreviewCommandHandler_NoLongerReferencesDecisionBuilder()
    {
        ConstructorInfo ctor =
            typeof(ExecutionPreviewCommandHandler)
                .GetConstructors()
                .Single();

        Type[] paramTypes =
            ctor.GetParameters()
                .Select(p => p.ParameterType)
                .ToArray();

        Assert.Single(paramTypes);
        Assert.Contains(typeof(IRuntimePreviewPlanner), paramTypes);
        Assert.DoesNotContain(
            typeof(IRuntimeDecisionBuilder), paramTypes);
        Assert.DoesNotContain(
            typeof(IExecutionPreviewBuilder), paramTypes);
    }
}
