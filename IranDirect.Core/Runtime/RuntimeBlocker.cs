namespace IranDirect.Core.Runtime;

public sealed record RuntimeBlocker
{
    public required RuntimeBlockerCode Code { get; init; }

    public required string Message { get; init; }
}