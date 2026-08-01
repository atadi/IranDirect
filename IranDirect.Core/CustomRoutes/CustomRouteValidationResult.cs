namespace IranDirect.Core.CustomRoutes;

public sealed record CustomRouteValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public string? NormalizedValue { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];
}
