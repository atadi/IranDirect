using IranDirect.Core.Configuration;

namespace IranDirect.Core.Prefixes;

public sealed record PrefixSourceFetchResult
{
    public PrefixSourceDescriptor Source { get; init; } = new();
    public IReadOnlyList<string> Prefixes { get; init; } = [];
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public string? ContentHash { get; init; }
    public long? ContentLength { get; init; }
    public bool NotModified { get; init; }

    /// <summary>
    /// The country this dataset was fetched for. This is the hard country
    /// binding: a dataset loaded for one country must never be consumed for
    /// another country. Set by the country-prefix source on every fetch.
    /// </summary>
    public DirectCountryCode? CountryCode { get; init; }
}
