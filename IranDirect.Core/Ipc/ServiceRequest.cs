namespace IranDirect.Core.Ipc;

public sealed record ServiceRequest
{
    public int ProtocolVersion { get; init; } =
        IpcProtocol.CurrentVersion;

    public required IranDirectCommand Command { get; init; }

    public string? Value { get; init; }

    public string? Description { get; init; }
}