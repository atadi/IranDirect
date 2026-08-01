namespace IranDirect.Core.CustomRoutes;

public sealed record CustomRouteDnsCacheEntry
{
    public Guid CustomRouteEntryId { get; init; }

    public string Domain { get; init; } = "";

    public IReadOnlyList<string> IPv4Addresses { get; init; } = [];

    public DateTimeOffset? LastAttemptedAt { get; init; }

    public DateTimeOffset? LastSucceededAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? StaleUntil { get; init; }

    public string? LastError { get; init; }
}
