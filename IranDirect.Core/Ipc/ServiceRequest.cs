namespace IranDirect.Core.Ipc;

public sealed record ServiceRequest
{
    public required string Command { get; init; }

    public string? Value { get; init; }
}