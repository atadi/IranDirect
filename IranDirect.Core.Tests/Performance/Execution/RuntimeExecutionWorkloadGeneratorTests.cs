using IranDirect.Core.Runtime.Execution;
using IranDirect.Testing.Performance.Execution;
using IranDirect.Testing.Performance.Workloads;

namespace IranDirect.Core.Tests.Performance.Execution;

public sealed class RuntimeExecutionWorkloadGeneratorTests
{
    private readonly RuntimeExecutionWorkloadGenerator _generator = new();

    [Theory]
    [InlineData(RuntimeExecutionWorkloadKind.PrefixRouteCreates, RuntimeExecutionStepKind.AddPrefixRoute)]
    [InlineData(RuntimeExecutionWorkloadKind.PrefixRouteDeletes, RuntimeExecutionStepKind.RemovePrefixRoute)]
    [InlineData(RuntimeExecutionWorkloadKind.EndpointRouteCreates, RuntimeExecutionStepKind.AddEndpointRoute)]
    [InlineData(RuntimeExecutionWorkloadKind.EndpointRouteDeletes, RuntimeExecutionStepKind.RemoveEndpointRoute)]
    public void Generate_UniformKind_ProducesExpectedCountAndKind(
        RuntimeExecutionWorkloadKind kind,
        RuntimeExecutionStepKind stepKind)
    {
        RuntimeExecutionWorkload workload = _generator.Generate(kind, RuntimeWorkloadSize.Scale2K);

        Assert.Equal(2_000, workload.Count);
        Assert.Equal(RuntimeWorkloadSize.Scale2K, workload.Size);
        Assert.Equal(RuntimeExecutionWorkloadGenerator.DefaultSeed, workload.Seed);
        Assert.Equal(RuntimeExecutionWorkloadGenerator.GeneratorVersion, workload.GeneratorVersion);
        Assert.All(workload.Steps, step => Assert.Equal(stepKind, step.Kind));
    }

    [Fact]
    public void Generate_MixedExecutionGroups_HasAllFourKindsInPlannerOrder()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.MixedExecutionGroups,
            RuntimeWorkloadSize.Scale10K);

        Assert.Equal(10_000, workload.Count);
        Assert.Equal(2_500, workload.AddEndpointRouteCount);
        Assert.Equal(2_000, workload.RemovePrefixRouteCount);
        Assert.Equal(3_000, workload.AddPrefixRouteCount);
        Assert.Equal(2_500, workload.RemoveEndpointRouteCount);

        int addEndpointEnd = workload.AddEndpointRouteCount;
        int removePrefixEnd = addEndpointEnd + workload.RemovePrefixRouteCount;
        int addPrefixEnd = removePrefixEnd + workload.AddPrefixRouteCount;

        Assert.All(
            workload.Steps.Take(addEndpointEnd),
            step => Assert.Equal(RuntimeExecutionStepKind.AddEndpointRoute, step.Kind));
        Assert.All(
            workload.Steps.Skip(addEndpointEnd).Take(workload.RemovePrefixRouteCount),
            step => Assert.Equal(RuntimeExecutionStepKind.RemovePrefixRoute, step.Kind));
        Assert.All(
            workload.Steps.Skip(removePrefixEnd).Take(workload.AddPrefixRouteCount),
            step => Assert.Equal(RuntimeExecutionStepKind.AddPrefixRoute, step.Kind));
        Assert.All(
            workload.Steps.Skip(addPrefixEnd),
            step => Assert.Equal(RuntimeExecutionStepKind.RemoveEndpointRoute, step.Kind));
    }

    [Theory]
    [InlineData(RuntimeExecutionWorkloadKind.PrefixRouteCreates)]
    [InlineData(RuntimeExecutionWorkloadKind.PrefixRouteDeletes)]
    [InlineData(RuntimeExecutionWorkloadKind.EndpointRouteCreates)]
    [InlineData(RuntimeExecutionWorkloadKind.EndpointRouteDeletes)]
    [InlineData(RuntimeExecutionWorkloadKind.MixedExecutionGroups)]
    public void Generate_DestinationPrefixesAreUnique(
        RuntimeExecutionWorkloadKind kind)
    {
        RuntimeExecutionWorkload workload = _generator.Generate(kind, RuntimeWorkloadSize.Scale5K);

        HashSet<string> prefixes = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);

        foreach (RuntimeExecutionStep step in workload.Steps)
        {
            Assert.True(
                prefixes.Add(step.DestinationPrefix),
                $"Duplicate destination prefix '{step.DestinationPrefix}'.");
            Assert.True(
                identities.Add(step.Identity),
                $"Duplicate identity '{step.Identity}'.");
        }
    }

    [Fact]
    public void Generate_SameSeed_IsDeterministic()
    {
        RuntimeExecutionWorkload first = _generator.Generate(
            RuntimeExecutionWorkloadKind.MixedExecutionGroups,
            RuntimeWorkloadSize.Scale1K,
            seed: 12345);
        RuntimeExecutionWorkload second = _generator.Generate(
            RuntimeExecutionWorkloadKind.MixedExecutionGroups,
            RuntimeWorkloadSize.Scale1K,
            seed: 12345);

        Assert.Equal(first.Steps, second.Steps);
    }

    [Fact]
    public void Generate_DifferentSeed_ProducesDifferentGatewayPersonality()
    {
        RuntimeExecutionWorkload first = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale1K,
            seed: 12345);
        RuntimeExecutionWorkload second = _generator.Generate(
            RuntimeExecutionWorkloadKind.PrefixRouteCreates,
            RuntimeWorkloadSize.Scale1K,
            seed: 54321);

        bool anyGatewayDiffers = first.Steps.Zip(second.Steps)
            .Any(pair => !string.Equals(
                pair.First.Gateway,
                pair.Second.Gateway,
                StringComparison.Ordinal));

        Assert.True(anyGatewayDiffers, "Different seeds must vary the generated gateways.");
        Assert.False(
            first.Steps.SequenceEqual(second.Steps),
            "Different seeds must produce different steps.");
    }

    [Fact]
    public void Generate_PrefixStepsUsePrefixCidrAndEndpointStepsUseHostCidr()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.MixedExecutionGroups,
            RuntimeWorkloadSize.Scale2K);

        foreach (RuntimeExecutionStep step in workload.Steps)
        {
            bool isEndpoint = step.Kind is RuntimeExecutionStepKind.AddEndpointRoute
                or RuntimeExecutionStepKind.RemoveEndpointRoute;

            if (isEndpoint)
            {
                Assert.EndsWith("/32", step.DestinationPrefix);
            }
            else
            {
                Assert.EndsWith("/24", step.DestinationPrefix);
            }
        }
    }

    [Fact]
    public void Generate_AllRequiredFieldsArePopulated()
    {
        RuntimeExecutionWorkload workload = _generator.Generate(
            RuntimeExecutionWorkloadKind.MixedExecutionGroups,
            RuntimeWorkloadSize.Scale1K);

        Assert.All(workload.Steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Identity));
            Assert.False(string.IsNullOrWhiteSpace(step.DestinationPrefix));
            Assert.False(string.IsNullOrWhiteSpace(step.Gateway));
            Assert.True(step.InterfaceIndex > 0);
            Assert.True(step.Metric > 0);
            Assert.False(string.IsNullOrWhiteSpace(step.Description));
        });
    }
}
