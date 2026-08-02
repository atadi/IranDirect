namespace IranDirect.Core.Prefixes;

public interface IPrefixSourceMetadataService
{
    Task<PrefixSourceMetadata?> GetCurrentAsync(
        CancellationToken cancellationToken = default);

    Task RecordSuccessAsync(
        PrefixSourceFetchResult result,
        CancellationToken cancellationToken = default);

    Task RecordNotModifiedAsync(
        PrefixSourceFetchResult result,
        CancellationToken cancellationToken = default);

    Task RecordFailureAsync(
        PrefixSourceDescriptor source,
        string error,
        CancellationToken cancellationToken = default);
}
