namespace PathVeer.Core.State;

public sealed record PathVeerState
{
    public bool Enabled { get; init; }

    public string? Gateway { get; init; }

    public uint InterfaceIndex { get; init; }

    public string? InterfaceName { get; init; }

    public int PrefixCount { get; init; }

    public DateTimeOffset? EnabledAt { get; init; }

    public DateTimeOffset? PrefixesUpdatedAt { get; init; }

    public string? LastError { get; init; }
}