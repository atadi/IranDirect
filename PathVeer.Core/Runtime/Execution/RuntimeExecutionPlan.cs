namespace PathVeer.Core.Runtime.Execution;

public sealed record RuntimeExecutionPlan
{
    public required IReadOnlyList<RuntimeExecutionStep> Steps
        { get; init; } = [];

    public bool IsEmpty => Steps.Count == 0;

    public int Count => Steps.Count;
}
