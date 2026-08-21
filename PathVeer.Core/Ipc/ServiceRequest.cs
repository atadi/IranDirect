namespace PathVeer.Core.Ipc;

public sealed record ServiceRequest
{
    public int ProtocolVersion { get; init; } =
        IpcProtocol.CurrentVersion;

    public required PathVeerCommand Command { get; init; }

    public string? Value { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// Explicit operator intent flag. Used by Cloud re-enrollment to replace
    /// an existing registration (conservative, never implicit).
    /// </summary>
    public bool Force { get; init; }
}