namespace IranDirect.Core.Prefixes;

public interface IPrefixSource
{
    PrefixSourceDescriptor Descriptor { get; }

    Task<PrefixSourceFetchResult> FetchAsync(
        PrefixSourceRequest request,
        CancellationToken cancellationToken = default);
}
