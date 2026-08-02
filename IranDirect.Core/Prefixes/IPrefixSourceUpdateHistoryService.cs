namespace IranDirect.Core.Prefixes;

public interface IPrefixSourceUpdateHistoryService
{
    Task<IReadOnlyList<PrefixSourceUpdateHistoryEntry>>
        GetRecentAsync(
            int? limit = null,
            CancellationToken cancellationToken = default);

    Task RecordSuccessAsync(
        PrefixSourceFetchResult result,
        PrefixSourceChangeSummary? changeSummary = null,
        IReadOnlyList<string>? previousPrefixes = null,
        CancellationToken cancellationToken = default);

    Task RecordNotModifiedAsync(
        PrefixSourceFetchResult result,
        int currentPrefixCount = 0,
        string? currentContentHash = null,
        CancellationToken cancellationToken = default);

    Task RecordFailureAsync(
        PrefixSourceDescriptor source,
        string error,
        CancellationToken cancellationToken = default);

    Task ClearAsync(
        CancellationToken cancellationToken = default);
}
