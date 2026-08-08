namespace PathVeer.Core.CustomRoutes;

public enum CustomRouteDnsCacheState
{
    Fresh,
    Stale,
    Expired,
    Failed,
    Missing,
    Disabled
}

public sealed record CustomRouteDnsCacheStatus
{
    public Guid CustomRouteEntryId { get; init; }

    public string Domain { get; init; } = "";

    public bool Enabled { get; init; }

    public CustomRouteDnsCacheState State { get; init; }

    public IReadOnlyList<string> IPv4Addresses { get; init; } = [];

    public DateTimeOffset? LastAttemptedAt { get; init; }

    public DateTimeOffset? LastSucceededAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? StaleUntil { get; init; }

    public string? LastError { get; init; }
}
