namespace IranDirect.Core.Prefixes;

public interface IPrefixSourceUpdateHistoryRepository
{
    Task<PrefixSourceUpdateHistoryDocument> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        PrefixSourceUpdateHistoryDocument document,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PrefixSourceUpdateHistoryEntry>>
        GetRecentAsync(
            int limit,
            CancellationToken cancellationToken = default);

    Task AppendAsync(
        PrefixSourceUpdateHistoryEntry entry,
        CancellationToken cancellationToken = default);

    Task ClearAsync(
        CancellationToken cancellationToken = default);
}
