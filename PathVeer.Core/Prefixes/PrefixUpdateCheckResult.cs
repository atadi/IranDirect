namespace PathVeer.Core.Prefixes;

public sealed record PrefixUpdateCheckResult
{
    public PrefixUpdateCheckStatus Status { get; init; }
    public PrefixSourceMetadata? CurrentMetadata { get; init; }
    public PrefixUpdateCheckRemoteMetadata? RemoteMetadata { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
    public string? Reason { get; init; }
}
