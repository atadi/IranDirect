using IranDirect.Core.Configuration;

namespace IranDirect.Core.Prefixes;

public interface IPrefixSourceUpdateHistoryService
{
    Task<IReadOnlyList<PrefixSourceUpdateHistoryEntry>>
        GetRecentAsync(
            DirectCountryCode country,
            int? limit = null,
            CancellationToken cancellationToken = default);

    Task RecordSuccessAsync(
        DirectCountryCode country,
        PrefixSourceFetchResult result,
        PrefixSourceChangeSummary? changeSummary = null,
        IReadOnlyList<string>? previousPrefixes = null,
        CancellationToken cancellationToken = default);

    Task RecordNotModifiedAsync(
        DirectCountryCode country,
        PrefixSourceFetchResult result,
        int currentPrefixCount = 0,
        string? currentContentHash = null,
        CancellationToken cancellationToken = default);

    Task RecordFailureAsync(
        DirectCountryCode country,
        PrefixSourceDescriptor source,
        string error,
        CancellationToken cancellationToken = default);

    Task ClearAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default);
}
