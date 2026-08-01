namespace IranDirect.Core.CustomRoutes;

public sealed record CustomRouteResolutionResult
{
    public IReadOnlyList<string> Prefixes { get; init; } = [];

    public IReadOnlyList<CustomRouteResolutionFailure> Failures
        { get; init; } = [];

    public bool AllSucceeded => Failures.Count == 0;
}

public sealed record CustomRouteResolutionFailure
{
    public CustomRouteEntryType Type { get; init; }

    public string Value { get; init; } = "";

    public string Reason { get; init; } = "";
}
