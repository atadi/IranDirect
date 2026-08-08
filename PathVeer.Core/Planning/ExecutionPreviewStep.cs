namespace PathVeer.Core.Planning;

public sealed record ExecutionPreviewStep
{
    public required ExecutionPreviewCategory Category
        { get; init; }

    public required ExecutionPreviewOperation Operation
        { get; init; }

    public required string Target { get; init; }

    public required string Reason { get; init; }
}
