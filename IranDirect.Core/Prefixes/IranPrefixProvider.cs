using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace IranDirect.Core.Prefixes;

public sealed class IranPrefixProvider
{
    private const string RipeStatUrl =
        "https://stat.ripe.net/data/country-resource-list/data.json?resource=IR";

    private readonly HttpClient _httpClient;

    public IranPrefixProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<string>> DownloadIpv4PrefixesAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            RipeStatUrl,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        CountryResourceResponse? result =
            await response.Content.ReadFromJsonAsync<CountryResourceResponse>(
                cancellationToken: cancellationToken);

        if (result?.Data?.Resources?.Ipv4 is not { Count: > 0 } prefixes)
        {
            throw new InvalidOperationException(
                "RIPEstat returned no Iranian IPv4 prefixes.");
        }

        return prefixes
            .Where(IsValidIpv4Prefix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ParseAddress)
            .ThenBy(ParsePrefixLength)
            .ToArray();
    }

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