using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using IranDirect.Core.Configuration;
using IranDirect.Core.Testing.FaultInjection;

namespace IranDirect.Core.Prefixes;

/// <summary>
/// Generic RIPEstat country-resource-list prefix source. One implementation
/// parameterized by <see cref="DirectCountryCode"/>; the requested country is
/// the only thing that varies (the resource parameter in the URL). No country
/// is hard-coded.
/// </summary>
public sealed class OfficialCountryPrefixSource :
    ICountryPrefixSource
{
    public const string SourceId =
        "ripe-stat-country-resource-list-ipv4";
    public const string SourceFormat =
        "ripestat-country-resource-list-json";
    public const string ParserVersion = "1";

    private const string BaseUri =
        "https://stat.ripe.net/data/country-resource-list/data.json";

    private const int MaxPrefixCount = 2_000_000;
    private const long MaxContentLength = 512L * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly IFaultInjectionPolicy _faultPolicy;

    public OfficialCountryPrefixSource(
        HttpClient httpClient,
        TimeProvider? timeProvider = null,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
    }

    public PrefixSourceDescriptor GetDescriptor(
        DirectCountryCode country)
    {
        ArgumentNullException.ThrowIfNull(country);

        return new PrefixSourceDescriptor
        {
            Id = SourceId,
            DisplayName =
                $"RIPEstat {country.Code} IPv4 country resource list",
            Uri = BuildUri(country),
            Format = SourceFormat,
            ParserVersion = ParserVersion
        };
    }

    public async Task<PrefixSourceFetchResult> FetchAsync(
        DirectCountryCode country,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(country);

        PrefixSourceDescriptor descriptor = GetDescriptor(country);

        if (FaultInjectionResolver.ShouldFail(
                _faultPolicy,
                FaultInjectionPoint.HttpRequest))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.HttpRequest);
        }

        DateTimeOffset startedAt = _timeProvider.GetUtcNow();

        using HttpResponseMessage response = await _httpClient.GetAsync(
            descriptor.Uri,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            DateTimeOffset notModifiedCompletedAt =
                _timeProvider.GetUtcNow();

            return new PrefixSourceFetchResult
            {
                Source = descriptor,
                CountryCode = country,
                Prefixes = [],
                StartedAt = startedAt,
                CompletedAt = notModifiedCompletedAt,
                Duration = notModifiedCompletedAt - startedAt,
                ETag = ReadETag(response),
                LastModified = ReadLastModified(response),
                NotModified = true
            };
        }

        response.EnsureSuccessStatusCode();

        CountryResourceResponse? result =
            await response.Content.ReadFromJsonAsync<CountryResourceResponse>(
                cancellationToken: cancellationToken);

        long? contentLength =
            response.Content.Headers.ContentLength;
        if (contentLength is > MaxContentLength)
        {
            throw new InvalidOperationException(
                $"RIPEstat response for {country.Code} exceeded the " +
                $"maximum allowed size of {MaxContentLength} bytes.");
        }

        if (result?.Data?.Resources?.Ipv4 is not { Count: > 0 } prefixes)
        {
            throw new InvalidOperationException(
                $"RIPEstat returned no {country.Code} IPv4 prefixes.");
        }

        if (prefixes.Count > MaxPrefixCount)
        {
            throw new InvalidOperationException(
                $"RIPEstat returned {prefixes.Count} prefixes for " +
                $"{country.Code}, exceeding the safety limit of " +
                $"{MaxPrefixCount}.");
        }

        IReadOnlyList<string> normalized = prefixes
            .Where(IsValidIpv4Prefix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ParseAddress)
            .ThenBy(ParsePrefixLength)
            .ToArray();

        if (normalized.Count == 0)
        {
            throw new InvalidOperationException(
                $"RIPEstat returned no valid {country.Code} IPv4 prefixes.");
        }

        DateTimeOffset completedAt = _timeProvider.GetUtcNow();

        return new PrefixSourceFetchResult
        {
            Source = descriptor,
            CountryCode = country,
            Prefixes = normalized,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Duration = completedAt - startedAt,
            ETag = ReadETag(response),
            LastModified = ReadLastModified(response),
            ContentHash =
                PrefixContentHasher.ComputeHash(normalized),
            ContentLength = contentLength
        };
    }

    private static string BuildUri(DirectCountryCode country) =>
        $"{BaseUri}?resource={country.Code}";

    private static string? ReadETag(
        HttpResponseMessage response) =>
        response.Headers.ETag?.Tag;

    private static DateTimeOffset? ReadLastModified(
        HttpResponseMessage response) =>
        response.Content.Headers.LastModified;

    private static bool IsValidIpv4Prefix(string value)
    {
        string[] parts = value.Split('/');

        return parts.Length == 2
            && IPAddress.TryParse(parts[0], out IPAddress? address)
            && address.AddressFamily ==
                System.Net.Sockets.AddressFamily.InterNetwork
            && byte.TryParse(parts[1], out byte prefixLength)
            && prefixLength <= 32;
    }

    private static uint ParseAddress(string prefix)
    {
        byte[] bytes = IPAddress.Parse(prefix.Split('/')[0]).GetAddressBytes();

        return ((uint)bytes[0] << 24)
             | ((uint)bytes[1] << 16)
             | ((uint)bytes[2] << 8)
             | bytes[3];
    }

    private static int ParsePrefixLength(string prefix) =>
        int.Parse(prefix.Split('/')[1]);

    private sealed class CountryResourceResponse
    {
        [JsonPropertyName("data")]
        public CountryResourceData? Data { get; set; }
    }

    private sealed class CountryResourceData
    {
        [JsonPropertyName("resources")]
        public CountryResources? Resources { get; set; }
    }

    private sealed class CountryResources
    {
        [JsonPropertyName("ipv4")]
        public List<string>? Ipv4 { get; set; }
    }
}
