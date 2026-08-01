namespace IranDirect.Core.CustomRoutes;

public sealed record CustomRouteEntry
{
    public Guid Id { get; init; }

    public CustomRouteEntryType Type { get; init; }

    public string Value { get; init; } = "";

    public bool Enabled { get; init; }

    public string? Description { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ModifiedAt { get; init; }
}
