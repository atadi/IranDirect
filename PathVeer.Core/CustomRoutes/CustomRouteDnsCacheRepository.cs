namespace PathVeer.Core.CustomRoutes;

public sealed class CustomRouteDnsCacheRepository :
    ICustomRouteDnsCacheRepository
{
    private const int MaxErrorLength = 512;

    private readonly CustomRouteDnsCacheStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly CustomRouteEntryValidator _validator = new();
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public CustomRouteDnsCacheRepository(
        CustomRouteDnsCacheStore store,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<CustomRouteDnsCacheEntry>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        CustomRouteDnsCacheCollection collection =
            await _store.LoadAsync(cancellationToken);

        return collection.Entries;
    }

    public async Task<CustomRouteDnsCacheEntry?> GetByEntryIdAsync(
        Guid customRouteEntryId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidEntryId(customRouteEntryId);

        CustomRouteDnsCacheCollection collection =
            await _store.LoadAsync(cancellationToken);

        return collection.Entries.FirstOrDefault(
            entry => entry.CustomRouteEntryId == customRouteEntryId);
    }

    public async Task UpsertSuccessAsync(
        Guid customRouteEntryId,
        string? domain,
        IEnumerable<string>? ipv4Addresses,
        TimeSpan cacheDuration,
        TimeSpan maxStaleDuration,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidEntryId(customRouteEntryId);

        string normalizedDomain = NormalizeDomain(domain);

        IReadOnlyList<string> addresses =
            NormalizeAddresses(ipv4Addresses);

        if (cacheDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cacheDuration),
                "Cache duration must be greater than zero.");
        }

        if (maxStaleDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxStaleDuration),
                "Max stale duration must be greater than zero.");
        }

        if (maxStaleDuration < cacheDuration)
        {
            throw new ArgumentException(
                "Max stale duration must not be earlier than cache duration.",
                nameof(maxStaleDuration));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        CustomRouteDnsCacheEntry entry = new()
        {
            CustomRouteEntryId = customRouteEntryId,
            Domain = normalizedDomain,
            IPv4Addresses = addresses,
            LastAttemptedAt = now,
            LastSucceededAt = now,
            ExpiresAt = now + cacheDuration,
            StaleUntil = now + maxStaleDuration,
            LastError = null
        };

        await MutateAsync(collection =>
        {
            return new CustomRouteDnsCacheCollection
            {
                SchemaVersion = collection.SchemaVersion,
                Entries = collection.Entries
                    .Where(e => e.CustomRouteEntryId != customRouteEntryId)
                    .Append(entry)
                    .ToArray()
            };
        }, cancellationToken);
    }

    public async Task UpsertFailureAsync(
        Guid customRouteEntryId,
        string? domain,
        string? error,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidEntryId(customRouteEntryId);

        string normalizedDomain = NormalizeDomain(domain);

        string normalizedError = NormalizeError(error);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        await MutateAsync(collection =>
        {
            CustomRouteDnsCacheEntry? existing =
                collection.Entries.FirstOrDefault(
                    e => e.CustomRouteEntryId == customRouteEntryId);

            CustomRouteDnsCacheEntry entry = existing is null
                ? new CustomRouteDnsCacheEntry
                {
                    CustomRouteEntryId = customRouteEntryId,
                    Domain = normalizedDomain,
                    IPv4Addresses = [],
                    LastAttemptedAt = now,
                    LastError = normalizedError
                }
                : existing with
                {
                    Domain = normalizedDomain,
                    LastAttemptedAt = now,
                    LastError = normalizedError
                };

            return new CustomRouteDnsCacheCollection
            {
                SchemaVersion = collection.SchemaVersion,
                Entries = collection.Entries
                    .Where(e => e.CustomRouteEntryId != customRouteEntryId)
                    .Append(entry)
                    .ToArray()
            };
        }, cancellationToken);
    }

    public async Task<bool> RemoveAsync(
        Guid customRouteEntryId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidEntryId(customRouteEntryId);

        bool removed = false;

        await MutateAsync(collection =>
        {
            IReadOnlyList<CustomRouteDnsCacheEntry> remaining =
                collection.Entries
                    .Where(e => e.CustomRouteEntryId != customRouteEntryId)
                    .ToArray();

            removed = remaining.Count != collection.Entries.Count;

            return new CustomRouteDnsCacheCollection
            {
                SchemaVersion = collection.SchemaVersion,
                Entries = remaining
            };
        }, cancellationToken);

        return removed;
    }

    public async Task<int> RemoveMissingEntriesAsync(
        IReadOnlyCollection<Guid> existingEntryIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(existingEntryIds);

        HashSet<Guid> valid = existingEntryIds.ToHashSet();

        int removed = 0;

        await MutateAsync(collection =>
        {
            IReadOnlyList<CustomRouteDnsCacheEntry> remaining =
                collection.Entries
                    .Where(e => valid.Contains(e.CustomRouteEntryId))
                    .ToArray();

            removed = collection.Entries.Count - remaining.Count;

            return new CustomRouteDnsCacheCollection
            {
                SchemaVersion = collection.SchemaVersion,
                Entries = remaining
            };
        }, cancellationToken);

        return removed;
    }

    private async Task MutateAsync(
        Func<CustomRouteDnsCacheCollection, CustomRouteDnsCacheCollection> transform,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transform);

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            CustomRouteDnsCacheCollection current =
                await _store.LoadAsync(cancellationToken);

            CustomRouteDnsCacheCollection updated =
                transform(current);

            await _store.SaveAsync(updated, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private string NormalizeDomain(string? domain)
    {
        CustomRouteValidationResult result =
            _validator.ValidateAndNormalize(
                CustomRouteEntryType.Domain,
                domain);

        if (result.Errors.Count > 0)
        {
            throw new ArgumentException(
                $"Domain is invalid: {result.Errors[0]}",
                nameof(domain));
        }

        return result.NormalizedValue ?? "";
    }

    private static IReadOnlyList<string> NormalizeAddresses(
        IEnumerable<string>? ipv4Addresses)
    {
        if (ipv4Addresses is null)
        {
            return [];
        }

        List<string> addresses = new();

        foreach (string? address in ipv4Addresses)
        {
            if (address is null)
            {
                throw new ArgumentException(
                    "IPv4 addresses must not contain null entries.",
                    nameof(ipv4Addresses));
            }

            if (!TryParseIpv4(address, out string canonical))
            {
                throw new ArgumentException(
                    $"'{address}' is not a valid IPv4 address.",
                    nameof(ipv4Addresses));
            }

            if (!addresses.Contains(canonical, StringComparer.Ordinal))
            {
                addresses.Add(canonical);
            }
        }

        addresses.Sort(StringComparer.Ordinal);

        return addresses;
    }

    private static string NormalizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException(
                "Error message must not be empty.",
                nameof(error));
        }

        string collapsed = string.Join(
            ' ',
            error
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));

        if (collapsed.Length == 0)
        {
            throw new ArgumentException(
                "Error message must not be empty.",
                nameof(error));
        }

        return collapsed.Length <= MaxErrorLength
            ? collapsed
            : collapsed[..MaxErrorLength];
    }

    private static void ThrowIfInvalidEntryId(Guid customRouteEntryId)
    {
        if (customRouteEntryId == Guid.Empty)
        {
            throw new ArgumentException(
                "Custom route entry ID must not be empty.",
                nameof(customRouteEntryId));
        }
    }

    private static bool TryParseIpv4(
        string value,
        out string canonical)
    {
        canonical = "";

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string s = value.Trim();

        string[] parts = s.Split('.');

        if (parts.Length != 4)
        {
            return false;
        }

        byte[] octets = new byte[4];

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];

            if (part.Length == 0
                || part.Length > 3
                || (part.Length > 1 && part[0] == '0'))
            {
                return false;
            }

            foreach (char c in part)
            {
                if (c is not (>= '0' and <= '9'))
                {
                    return false;
                }
            }

            if (!byte.TryParse(part, out octets[i]))
            {
                return false;
            }
        }

        canonical =
            $"{octets[0]}.{octets[1]}.{octets[2]}.{octets[3]}";
        return true;
    }
}
