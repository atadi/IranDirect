namespace PathVeer.Core.Prefixes;

public sealed record PrefixSourceUpdateHistoryDocument
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<PrefixSourceUpdateHistoryEntry> Entries { get; init; } = [];
}
