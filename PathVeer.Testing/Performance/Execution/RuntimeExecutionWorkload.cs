using PathVeer.Core.Runtime.Execution;
using PathVeer.Testing.Performance.Workloads;

namespace PathVeer.Testing.Performance.Execution;

public sealed record RuntimeExecutionWorkload
{
    public required RuntimeExecutionWorkloadKind Kind { get; init; }

    public required RuntimeWorkloadSize Size { get; init; }

    public required int Seed { get; init; }

    public required string GeneratorVersion { get; init; }

    public required IReadOnlyList<RuntimeExecutionStep> Steps { get; init; }

    public required int AddEndpointRouteCount { get; init; }

    public required int RemoveEndpointRouteCount { get; init; }

    public required int AddPrefixRouteCount { get; init; }

    public required int RemovePrefixRouteCount { get; init; }

    public int Count => Steps.Count;
}
