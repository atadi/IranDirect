using IranDirect.Core.Configuration;

namespace IranDirect.Core.Prefixes;

/// <summary>
/// Front-end for the generic country-prefix source. Replaces the
/// Iran-specific provider; one instance serves every country via
/// <see cref="DirectCountryCode"/>.
/// </summary>
public sealed class CountryPrefixProvider
{
    private readonly ICountryPrefixSource _source;

    public CountryPrefixProvider(
        ICountryPrefixSource source)
    {
        _source = source;
    }

    public async Task<IReadOnlyList<string>> DownloadIpv4PrefixesAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(country);

        PrefixSourceFetchResult result =
            await _source.FetchAsync(
                country,
                cancellationToken);

        return result.Prefixes;
    }
}
