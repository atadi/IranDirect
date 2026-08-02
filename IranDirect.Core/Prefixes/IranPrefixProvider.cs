namespace IranDirect.Core.Prefixes;

public sealed class IranPrefixProvider
{
    private readonly OfficialIranPrefixSource _source;

    public IranPrefixProvider(
        HttpClient httpClient,
        TimeProvider? timeProvider = null)
    {
        _source = new OfficialIranPrefixSource(
            httpClient,
            timeProvider);
    }

    public async Task<IReadOnlyList<string>> DownloadIpv4PrefixesAsync(
        CancellationToken cancellationToken = default)
    {
        PrefixSourceFetchResult result =
            await _source.FetchAsync(
                new PrefixSourceRequest(),
                cancellationToken);

        return result.Prefixes;
    }
}
