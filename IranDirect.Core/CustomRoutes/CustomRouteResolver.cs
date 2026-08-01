using System.Net;
using System.Net.Sockets;

namespace IranDirect.Core.CustomRoutes;

public sealed class CustomRouteResolver : ICustomRouteResolver
{
    private readonly ICustomRouteRepository _repository;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>> _dnsLookup;

    public CustomRouteResolver(
        ICustomRouteRepository repository,
        Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>>? dnsLookup = null)
    {
        _repository = repository;
        _dnsLookup = dnsLookup ?? DefaultDnsLookupAsync;
    }

    public async Task<CustomRouteResolutionResult> ResolveAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CustomRouteEntry> entries =
            await _repository.GetAllAsync(cancellationToken);

        HashSet<string> prefixes =
            new(StringComparer.OrdinalIgnoreCase);

        List<CustomRouteResolutionFailure> failures = [];

        foreach (CustomRouteEntry entry in entries)
        {
            if (!entry.Enabled)
            {
                continue;
            }

            switch (entry.Type)
            {
                case CustomRouteEntryType.IpAddress:
                    ResolveIpAddress(entry, prefixes, failures);
                    break;

                case CustomRouteEntryType.Cidr:
                    ResolveCidr(entry, prefixes, failures);
                    break;

                case CustomRouteEntryType.Domain:
                    await ResolveDomainAsync(
                        entry,
                        prefixes,
                        failures,
                        cancellationToken);
                    break;

                default:
                    failures.Add(
                        CreateFailure(
                            entry,
                            $"Unsupported entry type: {entry.Type}."));
                    break;
            }
        }

        return new CustomRouteResolutionResult
        {
            Prefixes = prefixes
                .OrderBy(ParseAddress)
                .ThenBy(ParsePrefixLength)
                .ToArray(),
            Failures = failures
        };
    }

    private static void ResolveIpAddress(
        CustomRouteEntry entry,
        HashSet<string> prefixes,
        List<CustomRouteResolutionFailure> failures)
    {
        if (!IPAddress.TryParse(entry.Value, out IPAddress? address)
            || address.AddressFamily != AddressFamily.InterNetwork)
        {
            failures.Add(
                CreateFailure(
                    entry,
                    "Invalid IPv4 address."));
            return;
        }

        prefixes.Add($"{address}/32");
    }

    private static void ResolveCidr(
        CustomRouteEntry entry,
        HashSet<string> prefixes,
        List<CustomRouteResolutionFailure> failures)
    {
        string[] parts = entry.Value.Split('/');

        bool valid =
            parts.Length == 2
            && IPAddress.TryParse(parts[0], out IPAddress? address)
            && address.AddressFamily ==
                AddressFamily.InterNetwork
            && byte.TryParse(parts[1], out byte prefixLength)
            && prefixLength is >= 1 and <= 32;

        if (!valid)
        {
            failures.Add(
                CreateFailure(
                    entry,
                    "Invalid IPv4 CIDR."));
            return;
        }

        prefixes.Add(entry.Value);
    }

    private async Task ResolveDomainAsync(
        CustomRouteEntry entry,
        HashSet<string> prefixes,
        List<CustomRouteResolutionFailure> failures,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IPAddress> addresses;

        try
        {
            addresses =
                await _dnsLookup(
                    entry.Value,
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            failures.Add(
                CreateFailure(
                    entry,
                    $"DNS resolution failed: {exception.Message}"));
            return;
        }

        IPAddress[] ipv4 = addresses
            .Where(address =>
                address.AddressFamily ==
                AddressFamily.InterNetwork)
            .ToArray();

        if (ipv4.Length == 0)
        {
            failures.Add(
                CreateFailure(
                    entry,
                    "No IPv4 A records resolved."));
            return;
        }

        foreach (IPAddress address in ipv4)
        {
            prefixes.Add($"{address}/32");
        }
    }

    private static CustomRouteResolutionFailure CreateFailure(
        CustomRouteEntry entry,
        string reason) =>
        new()
        {
            Type = entry.Type,
            Value = entry.Value,
            Reason = reason
        };

    private static async Task<IReadOnlyList<IPAddress>> DefaultDnsLookupAsync(
        string host,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses =
            await Dns.GetHostAddressesAsync(
                host,
                cancellationToken);

        return addresses;
    }

    private static uint ParseAddress(string prefix)
    {
        byte[] bytes =
            IPAddress.Parse(prefix.Split('/')[0])
                .GetAddressBytes();

        return ((uint)bytes[0] << 24)
             | ((uint)bytes[1] << 16)
             | ((uint)bytes[2] << 8)
             | bytes[3];
    }

    private static int ParsePrefixLength(string prefix) =>
        int.Parse(prefix.Split('/')[1]);
}
