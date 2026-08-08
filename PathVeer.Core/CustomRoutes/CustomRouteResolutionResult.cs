namespace PathVeer.Core.CustomRoutes;

public sealed record CustomRouteResolutionResult
{
    public IReadOnlyList<string> Prefixes { get; init; } = [];

    public IReadOnlyList<CustomRouteResolutionFailure> Failures
        { get; init; } = [];

    public IReadOnlyList<CustomRouteResolutionDiagnostic> Diagnostics
        { get; init; } = [];

    public bool AllSucceeded => Failures.Count == 0;
}

public sealed record CustomRouteResolutionFailure
{
    public CustomRouteEntryType Type { get; init; }

    public string Value { get; init; } = "";

    public string Reason { get; init; } = "";
}

public enum CustomRouteResolutionStatus
{
    FreshCacheHit = 0,
    Refreshed = 1,
    StaleFallback = 2,
    Failure = 3
}

public sealed record CustomRouteResolutionDiagnostic
{
    public CustomRouteEntryType Type { get; init; }

    public string Value { get; init; } = "";

    public CustomRouteResolutionStatus Status { get; init; }

    public string? Reason { get; init; }
}
