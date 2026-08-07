using IranDirect.Core.Configuration;

namespace IranDirect.Core.Prefixes;

public interface IPrefixSourceMetadataService
{
    Task<PrefixSourceMetadata?> GetCurrentAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default);

    Task<PrefixSourceChangeSummary?> GetLatestChangeSummaryAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default);

    Task RecordSuccessAsync(
        DirectCountryCode country,
        PrefixSourceFetchResult result,
        IReadOnlyList<string>? previousPrefixes = null,
        CancellationToken cancellationToken = default);

    Task RecordNotModifiedAsync(
        DirectCountryCode country,
        PrefixSourceFetchResult result,
        CancellationToken cancellationToken = default);

    Task RecordFailureAsync(
        DirectCountryCode country,
        PrefixSourceDescriptor source,
        string error,
        CancellationToken cancellationToken = default);
}
