using PathVeer.Core.Runtime.Execution;
using PathVeer.Testing.Performance.Workloads;

namespace PathVeer.Testing.Performance.Execution;

public sealed class RuntimeExecutionWorkloadGenerator
{
    public const int DefaultSeed = 20260803;

    public const string GeneratorVersion = "1.0";

    private readonly RouteWorkloadGenerator _routes = new();

    public RuntimeExecutionWorkload Generate(
        RuntimeExecutionWorkloadKind kind,
        RuntimeWorkloadSize size) =>
        Generate(kind, size, DefaultSeed);

    public RuntimeExecutionWorkload Generate(
        RuntimeExecutionWorkloadKind kind,
        RuntimeWorkloadSize size,
        int seed)
    {
        int count = (int)size;

        return kind switch
        {
            RuntimeExecutionWorkloadKind.PrefixRouteCreates =>
                GenerateUniform(
                    kind,
                    count,
                    seed,
                    RuntimeExecutionStepKind.AddPrefixRoute,
                    prefixIndexOffset: 0,
                    endpointIndexOffset: 0),
            RuntimeExecutionWorkloadKind.PrefixRouteDeletes =>
                GenerateUniform(
                    kind,
                    count,
                    seed,
                    RuntimeExecutionStepKind.RemovePrefixRoute,
                    prefixIndexOffset: 0,
                    endpointIndexOffset: 0),
            RuntimeExecutionWorkloadKind.EndpointRouteCreates =>
                GenerateUniform(
                    kind,
                    count,
                    seed,
                    RuntimeExecutionStepKind.AddEndpointRoute,
                    prefixIndexOffset: 0,
                    endpointIndexOffset: 0),
            RuntimeExecutionWorkloadKind.EndpointRouteDeletes =>
                GenerateUniform(
                    kind,
                    count,
                    seed,
                    RuntimeExecutionStepKind.RemoveEndpointRoute,
                    prefixIndexOffset: 0,
                    endpointIndexOffset: 0),
            RuntimeExecutionWorkloadKind.MixedExecutionGroups =>
                GenerateMixed(count, seed),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                null)
        };
    }

    public RuntimeExecutionStep CreatePrefixStep(
        RuntimeExecutionStepKind stepKind,
        int prefixIndex,
        int index,
        int seed)
    {
        string prefix = PrefixWorkloadGenerator.ToCidr(prefixIndex);

        return new RuntimeExecutionStep
        {
            Kind = stepKind,
            Identity = $"{prefix}|{_routes.DesiredGateway(index, seed)}|{_routes.DesiredInterface(index)}",
            DestinationPrefix = prefix,
            Gateway = _routes.DesiredGateway(index, seed),
            InterfaceIndex = _routes.DesiredInterface(index),
            Metric = 256,
            Description = stepKind == RuntimeExecutionStepKind.AddPrefixRoute
                ? "Stress add prefix route."
                : "Stress remove prefix route."
        };
    }

    public RuntimeExecutionStep CreateEndpointStep(
        RuntimeExecutionStepKind stepKind,
        int endpointIndex,
        int index,
        int seed)
    {
        string prefix = PrefixWorkloadGenerator.ToEndpointCidr(endpointIndex);

        return new RuntimeExecutionStep
        {
            Kind = stepKind,
            Identity = $"{prefix}|{_routes.EndpointGateway(index, seed)}|{_routes.EndpointInterface(index)}",
            DestinationPrefix = prefix,
            Gateway = _routes.EndpointGateway(index, seed),
            InterfaceIndex = _routes.EndpointInterface(index),
            Metric = 1,
            Description = stepKind == RuntimeExecutionStepKind.AddEndpointRoute
                ? "Stress add endpoint route."
                : "Stress remove endpoint route."
        };
    }

    private RuntimeExecutionWorkload GenerateUniform(
        RuntimeExecutionWorkloadKind kind,
        int count,
        int seed,
        RuntimeExecutionStepKind stepKind,
        int prefixIndexOffset,
        int endpointIndexOffset)
    {
        bool prefix = stepKind is RuntimeExecutionStepKind.AddPrefixRoute
            or RuntimeExecutionStepKind.RemovePrefixRoute;

        var steps = new RuntimeExecutionStep[count];
        for (int i = 0; i < count; i++)
        {
            steps[i] = prefix
                ? CreatePrefixStep(stepKind, prefixIndexOffset + i, i, seed)
                : CreateEndpointStep(stepKind, endpointIndexOffset + i, i, seed);
        }

        int addEndpoint = stepKind == RuntimeExecutionStepKind.AddEndpointRoute ? count : 0;
        int removeEndpoint = stepKind == RuntimeExecutionStepKind.RemoveEndpointRoute ? count : 0;
        int addPrefix = stepKind == RuntimeExecutionStepKind.AddPrefixRoute ? count : 0;
        int removePrefix = stepKind == RuntimeExecutionStepKind.RemovePrefixRoute ? count : 0;

        return Finish(kind, count, seed, steps, addEndpoint, removeEndpoint, addPrefix, removePrefix);
    }

    private RuntimeExecutionWorkload GenerateMixed(int count, int seed)
    {
        int addEndpoint = count * 25 / 100;
        int removePrefix = count * 20 / 100;
        int addPrefix = count * 30 / 100;
        int removeEndpoint = count - addEndpoint - removePrefix - addPrefix;

        int addEndpointIndex = 0;
        int removePrefixIndex = 0;
        int addPrefixIndex = removePrefix;
        int removeEndpointIndex = addEndpoint;

        var steps = new RuntimeExecutionStep[count];
        int cursor = 0;

        for (int i = 0; i < addEndpoint; i++, cursor++)
        {
            steps[cursor] = CreateEndpointStep(
                RuntimeExecutionStepKind.AddEndpointRoute,
                addEndpointIndex + i,
                i,
                seed);
        }

        for (int i = 0; i < removePrefix; i++, cursor++)
        {
            steps[cursor] = CreatePrefixStep(
                RuntimeExecutionStepKind.RemovePrefixRoute,
                removePrefixIndex + i,
                i,
                seed);
        }

        for (int i = 0; i < addPrefix; i++, cursor++)
        {
            steps[cursor] = CreatePrefixStep(
                RuntimeExecutionStepKind.AddPrefixRoute,
                addPrefixIndex + i,
                i,
                seed);
        }

        for (int i = 0; i < removeEndpoint; i++, cursor++)
        {
            steps[cursor] = CreateEndpointStep(
                RuntimeExecutionStepKind.RemoveEndpointRoute,
                removeEndpointIndex + i,
                i,
                seed);
        }

        return Finish(
            RuntimeExecutionWorkloadKind.MixedExecutionGroups,
            count,
            seed,
            steps,
            addEndpoint,
            removeEndpoint,
            addPrefix,
            removePrefix);
    }

    private static RuntimeExecutionWorkload Finish(
        RuntimeExecutionWorkloadKind kind,
        int count,
        int seed,
        RuntimeExecutionStep[] steps,
        int addEndpoint,
        int removeEndpoint,
        int addPrefix,
        int removePrefix) =>
        new()
        {
            Kind = kind,
            Size = (RuntimeWorkloadSize)count,
            Seed = seed,
            GeneratorVersion = GeneratorVersion,
            Steps = steps,
            AddEndpointRouteCount = addEndpoint,
            RemoveEndpointRouteCount = removeEndpoint,
            AddPrefixRouteCount = addPrefix,
            RemovePrefixRouteCount = removePrefix
        };
}
