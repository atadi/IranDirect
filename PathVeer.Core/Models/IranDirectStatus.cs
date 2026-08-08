namespace PathVeer.Core.Models;

public sealed record IranDirectStatus
{
    public bool Enabled { get; init; }

    public string? GatewayAddress { get; init; }

    public uint? InterfaceIndex { get; init; }

    public string? InterfaceName { get; init; }

    public int ExpectedRouteCount { get; init; }

    public int InstalledRouteCount { get; init; }

    public DateTimeOffset? PrefixListUpdatedAt { get; init; }

    public string? LastError { get; init; }
}