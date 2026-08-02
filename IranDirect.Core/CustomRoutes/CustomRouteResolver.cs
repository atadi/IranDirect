using System.Net;
using System.Net.Sockets;

namespace IranDirect.Core.CustomRoutes;

public sealed class CustomRouteResolver : ICustomRouteResolver
{
    private const int MaxConcurrentDomainResolutions = 4;

    private const string CacheCleanupMarker = "<cache-cleanup>";

    private readonly ICustomRouteRepository _repository;
    private readonly ICustomRouteDnsCacheRepository _dnsCacheRepository;
    private readonly CustomRouteDnsCacheOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>> _dnsLookup;

    public CustomRouteResolver(
        ICustomRouteRepository repository,
        ICustomRouteDnsCacheRepository dnsCacheRepository,
        CustomRouteDnsCacheOptions options,
        TimeProvider timeProvider,
        Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>>? dnsLookup = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(dnsCacheRepository);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        CustomRouteDnsCacheOptions.Validate(options);

        _repository = repository;
        _dnsCacheRepository = dnsCacheRepository;
        _options = options;
        _timeProvider = timeProvider;
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
        List<CustomRouteResolutionDiagnostic> diagnostics = [];

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
                    break;

                default:
                    failures.Add(
                        CreateFailure(
                            entry,
                            $"Unsupported entry type: {entry.Type}."));
                    break;
            }
        }

        await ResolveDomainsAsync(
            entries,
            prefixes,
            failures,
            diagnostics,
            cancellationToken);

        await CleanupCacheAsync(
            entries,
            diagnostics,
            cancellationToken);

        return new CustomRouteResolutionResult
        {
            Prefixes = prefixes
                .OrderBy(ParseAddress)
                .ThenBy(ParsePrefixLength)
                .ToArray(),
            Failures = failures,
            Diagnostics = diagnostics
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

    private async Task ResolveDomainsAsync(
        IReadOnlyList<CustomRouteEntry> entries,
        HashSet<string> prefixes,
        List<CustomRouteResolutionFailure> failures,
        List<CustomRouteResolutionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var domainEntries = entries
            .Select((entry, index) => (entry, index))
            .Where(x => x.entry.Enabled
                && x.entry.Type == CustomRouteEntryType.Domain)
            .ToArray();

        if (domainEntries.Length == 0)
        {
            return;
        }

        DomainResolution?[] results =
            new DomainResolution?[entries.Count];

        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = MaxConcurrentDomainResolutions,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            domainEntries,
            parallelOptions,
            async (item, token) =>
            {
                results[item.index] =
                    await ResolveDomainCoreAsync(
                        item.entry,
                        token);
            });

        for (int i = 0; i < results.Length; i++)
        {
            DomainResolution? result = results[i];

            if (result is null)
            {
                continue;
            }

            foreach (string prefix in result.Prefixes)
            {
                prefixes.Add(prefix);
            }

            failures.AddRange(result.Failures);
            diagnostics.AddRange(result.Diagnostics);
        }
    }

    private async Task<DomainResolution> ResolveDomainCoreAsync(
        CustomRouteEntry entry,
        CancellationToken cancellationToken)
    {
        List<string> prefixes = [];
        List<CustomRouteResolutionFailure> failures = [];
        List<CustomRouteResolutionDiagnostic> diagnostics = [];

        CustomRouteDnsCacheEntry? cache =
            await LoadCacheEntryAsync(
                entry,
                diagnostics,
                cancellationToken);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (IsFresh(cache, now))
        {
            diagnostics.Add(
                CreateDiagnostic(
                    entry,
                    CustomRouteResolutionStatus.FreshCacheHit));

            return new DomainResolution(
                ToPrefixes(cache!.IPv4Addresses),
                diagnostics,
                failures);
        }

        using CancellationTokenSource timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutCts.CancelAfter(_options.DnsTimeout);

        IReadOnlyList<IPAddress> dnsAddresses;

        try
        {
            dnsAddresses =
                await _dnsLookup(
                    entry.Value,
                    timeoutCts.Token);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            string reason = timeoutCts.IsCancellationRequested
                ? "DNS resolution timed out."
                : $"DNS resolution failed: {exception.Message}";

            await RecordDnsFailureAsync(
                entry,
                cache,
                now,
                reason,
                prefixes,
                diagnostics,
                failures,
                cancellationToken);

            return new DomainResolution(
                prefixes,
                diagnostics,
                failures);
        }

        IReadOnlyList<string> ipv4 =
            CanonicalizeIpv4(dnsAddresses);

        if (ipv4.Count == 0)
        {
            await RecordDnsFailureAsync(
                entry,
                cache,
                now,
                "No IPv4 A records resolved.",
                prefixes,
                diagnostics,
                failures,
                cancellationToken);

            return new DomainResolution(
                prefixes,
                diagnostics,
                failures);
        }

        bool persisted = await TryUpsertSuccessAsync(
            entry,
            ipv4,
            diagnostics,
            cancellationToken);

        diagnostics.Add(
            CreateDiagnostic(
                entry,
                CustomRouteResolutionStatus.Refreshed));

        if (!persisted)
        {
            diagnostics.Add(
                CreateDiagnostic(
                    entry,
                    CustomRouteResolutionStatus.Failure,
                    "DNS cache update failed."));
        }

        foreach (string address in ipv4)
        {
            prefixes.Add($"{address}/32");
        }

        return new DomainResolution(
            prefixes,
            diagnostics,
            failures);
    }

    private async Task RecordDnsFailureAsync(
        CustomRouteEntry entry,
        CustomRouteDnsCacheEntry? cache,
        DateTimeOffset now,
        string reason,
        List<string> prefixes,
        List<CustomRouteResolutionDiagnostic> diagnostics,
        List<CustomRouteResolutionFailure> failures,
        CancellationToken cancellationToken)
    {
        await TryUpsertFailureAsync(
            entry,
            reason,
            diagnostics,
            cancellationToken);

        if (cache is not null
            && cache.IPv4Addresses.Count > 0
            && cache.StaleUntil is { } staleUntil
            && staleUntil > now)
        {
            foreach (string address in cache.IPv4Addresses)
            {
                prefixes.Add($"{address}/32");
            }

            diagnostics.Add(
                CreateDiagnostic(
                    entry,
                    CustomRouteResolutionStatus.StaleFallback,
                    $"{reason} Using stale cached addresses."));
        }
        else
        {
            failures.Add(
                CreateFailure(entry, reason));

            diagnostics.Add(
                CreateDiagnostic(
                    entry,
                    CustomRouteResolutionStatus.Failure,
                    reason));
        }
    }

    private async Task<CustomRouteDnsCacheEntry?> LoadCacheEntryAsync(
        CustomRouteEntry entry,
        List<CustomRouteResolutionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _dnsCacheRepository.GetByEntryIdAsync(
                entry.Id,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.Add(
                CreateDiagnostic(
                    entry,
                    CustomRouteResolutionStatus.Failure,
                    $"DNS cache read failed: {exception.Message}"));

            return null;
        }
    }

    private async Task<bool> TryUpsertSuccessAsync(
        CustomRouteEntry entry,
        IReadOnlyList<string> addresses,
        List<CustomRouteResolutionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dnsCacheRepository.UpsertSuccessAsync(
                entry.Id,
                entry.Value,
                addresses,
                _options.DnsCacheDuration,
                _options.DnsMaxStaleDuration,
                cancellationToken);

            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.Add(
                CreateDiagnostic(
                    entry,
                    CustomRouteResolutionStatus.Failure,
                    $"DNS cache update failed: {exception.Message}"));

            return false;
        }
    }

    private async Task<bool> TryUpsertFailureAsync(
        CustomRouteEntry entry,
        string reason,
        List<CustomRouteResolutionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dnsCacheRepository.UpsertFailureAsync(
                entry.Id,
                entry.Value,
                reason,
                cancellationToken);

            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.Add(
                CreateDiagnostic(
                    entry,
                    CustomRouteResolutionStatus.Failure,
                    $"DNS cache update failed: {exception.Message}"));

            return false;
        }
    }

    private async Task CleanupCacheAsync(
        IReadOnlyList<CustomRouteEntry> entries,
        List<CustomRouteResolutionDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        Guid[] domainEntryIds = entries
            .Where(e => e.Type == CustomRouteEntryType.Domain)
            .Select(e => e.Id)
            .ToArray();

        try
        {
            await _dnsCacheRepository.RemoveMissingEntriesAsync(
                domainEntryIds,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.Add(
                new CustomRouteResolutionDiagnostic
                {
                    Type = CustomRouteEntryType.Domain,
                    Value = CacheCleanupMarker,
                    Status = CustomRouteResolutionStatus.Failure,
                    Reason =
                        $"DNS cache cleanup failed: {exception.Message}"
                });
        }
    }

    private static bool IsFresh(
        CustomRouteDnsCacheEntry? cache,
        DateTimeOffset now) =>
        cache is not null
        && cache.IPv4Addresses.Count > 0
        && cache.ExpiresAt is { } expiresAt
        && expiresAt > now;

    private static IReadOnlyList<string> ToPrefixes(
        IEnumerable<string> addresses) =>
        addresses
            .Select(address => $"{address}/32")
            .ToArray();

    private static IReadOnlyList<string> CanonicalizeIpv4(
        IEnumerable<IPAddress> addresses) =>
        addresses
            .Where(address =>
                address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(address => address, StringComparer.Ordinal)
            .ToArray();

    private static CustomRouteResolutionFailure CreateFailure(
        CustomRouteEntry entry,
        string reason) =>
        new()
        {
            Type = entry.Type,
            Value = entry.Value,
            Reason = reason
        };

    private static CustomRouteResolutionDiagnostic CreateDiagnostic(
        CustomRouteEntry entry,
        CustomRouteResolutionStatus status,
        string? reason = null) =>
        new()
        {
            Type = entry.Type,
            Value = entry.Value,
            Status = status,
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

    private sealed record DomainResolution(
        IReadOnlyList<string> Prefixes,
        IReadOnlyList<CustomRouteResolutionDiagnostic> Diagnostics,
        IReadOnlyList<CustomRouteResolutionFailure> Failures);
}
