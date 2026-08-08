namespace PathVeer.Core.Prefixes;

public sealed record PrefixUpdateMonitorSnapshot
{
    public PrefixUpdateCheckResult? CurrentResult { get; init; }
    public DateTimeOffset? LastCheckedAt { get; init; }
    public DateTimeOffset? LastSuccessfulCheckAt { get; init; }
    public int ConsecutiveFailures { get; init; }
    public bool Running { get; init; }
    public bool Checking { get; init; }
}
