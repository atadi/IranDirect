using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace IranDirect.Core.Prefixes;

public sealed class OfficialIranPrefixSource :
    IPrefixSource
{
    public static PrefixSourceDescriptor Descriptor { get; } =
        new()
        {
            Id = "ripe-stat-country-resource-list-ipv4",
            DisplayName =
                "RIPEstat Iran IPv4 country resource list",
            Uri =
                "https://stat.ripe.net/data/country-resource-list/data.json?resource=IR",
            Format = "ripestat-country-resource-list-json",
            ParserVersion = "1"
        };

    PrefixSourceDescriptor IPrefixSource.Descriptor =>
        Descriptor;

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;

    public OfficialIranPrefixSource(
        HttpClient httpClient,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PrefixSourceFetchResult> FetchAsync(
        PrefixSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset startedAt = _timeProvider.GetUtcNow();

        using HttpResponseMessage response = await _httpClient.GetAsync(
            Descriptor.Uri,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            DateTimeOffset notModifiedCompletedAt =
                _timeProvider.GetUtcNow();

            return new PrefixSourceFetchResult
            {
                Source = Descriptor,
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

        if (result?.Data?.Resources?.Ipv4 is not { Count: > 0 } prefixes)
        {
            throw new InvalidOperationException(
                "RIPEstat returned no Iranian IPv4 prefixes.");
        }

        IReadOnlyList<string> normalized = prefixes
            .Where(IsValidIpv4Prefix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ParseAddress)
            .ThenBy(ParsePrefixLength)
            .ToArray();

        DateTimeOffset completedAt = _timeProvider.GetUtcNow();

        return new PrefixSourceFetchResult
        {
            Source = Descriptor,
            Prefixes = normalized,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Duration = completedAt - startedAt,
            ETag = ReadETag(response),
            LastModified = ReadLastModified(response),
            ContentHash =
                PrefixContentHasher.ComputeHash(normalized),
            ContentLength =
                response.Content.Headers.ContentLength
        };
    }

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
