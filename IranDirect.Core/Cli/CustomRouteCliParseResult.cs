namespace IranDirect.Core.Cli;

public sealed record CustomRouteCliParseResult
{
    public CustomRouteCliCommand? Command { get; init; }

    public string? Value { get; init; }

    public string? Description { get; init; }

    public string? Error { get; init; }

    public bool IsValid => Command is not null;
}
