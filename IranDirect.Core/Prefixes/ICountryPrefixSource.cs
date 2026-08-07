using IranDirect.Core.Configuration;

namespace IranDirect.Core.Prefixes;

/// <summary>
/// A prefix source that can fetch the IPv4 prefix dataset for an arbitrary
/// ISO 3166-1 alpha-2 country. The requested country is the single source of
/// truth for which dataset is fetched; the implementation must never hard-code
/// a country.
/// </summary>
public interface ICountryPrefixSource
{
    PrefixSourceDescriptor GetDescriptor(
        DirectCountryCode country);

    Task<PrefixSourceFetchResult> FetchAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default);
}
