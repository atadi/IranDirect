namespace IranDirect.Core.CustomRoutes;

public sealed class CustomRouteDnsCacheService
{
    private readonly CustomRouteService _service;
    private readonly ICustomRouteDnsCacheRepository _cacheRepository;
    private readonly TimeProvider _timeProvider;

    public CustomRouteDnsCacheService(
        CustomRouteService service,
        ICustomRouteDnsCacheRepository cacheRepository,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(cacheRepository);

        _service = service;
        _cacheRepository = cacheRepository;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<CustomRouteDnsCacheStatus>>
        GetStatusAsync(
            CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CustomRouteEntry> entries =
            await _service.GetAllAsync(cancellationToken);

        IReadOnlyList<CustomRouteDnsCacheEntry> records =
            await _cacheRepository.GetAllAsync(
                cancellationToken);

        Dictionary<Guid, CustomRouteDnsCacheEntry> byEntryId =
            records.ToDictionary(
                record => record.CustomRouteEntryId);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        return entries
            .Where(entry =>
                entry.Type == CustomRouteEntryType.Domain)
            .Select(entry =>
            {
                byEntryId.TryGetValue(
                    entry.Id,
                    out CustomRouteDnsCacheEntry? record);

                return CreateStatus(entry, record, now);
            })
            .OrderBy(
                status => status.Domain,
                StringComparer.Ordinal)
            .ThenBy(status => status.CustomRouteEntryId)
            .ToArray();
    }

    public async Task<bool> InvalidateAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId == Guid.Empty)
        {
            throw new ArgumentException(
                "Custom route ID must not be empty.",
                nameof(entryId));
        }

        IReadOnlyList<CustomRouteEntry> entries =
            await _service.GetAllAsync(cancellationToken);

        CustomRouteEntry? entry =
            entries.FirstOrDefault(e => e.Id == entryId);

        if (entry is null ||
            entry.Type != CustomRouteEntryType.Domain)
        {
            throw new KeyNotFoundException(
                $"Custom route '{entryId}' was not found.");
        }

        return await _cacheRepository.RemoveAsync(
            entryId,
            cancellationToken);
    }

    public async Task<int> InvalidateAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _cacheRepository.RemoveMissingEntriesAsync(
            [],
            cancellationToken);
    }

    private static CustomRouteDnsCacheStatus CreateStatus(
        CustomRouteEntry entry,
        CustomRouteDnsCacheEntry? record,
        DateTimeOffset now)
    {
        bool hasAddresses =
            record?.IPv4Addresses.Count > 0;

        CustomRouteDnsCacheState state;

        if (!entry.Enabled)
        {
            state = CustomRouteDnsCacheState.Disabled;
        }
        else if (record is null)
        {
            state = CustomRouteDnsCacheState.Missing;
        }
        else if (hasAddresses)
        {
            if (record.ExpiresAt is not null &&
                record.ExpiresAt.Value > now)
            {
                state = CustomRouteDnsCacheState.Fresh;
            }
            else if (record.StaleUntil is not null &&
                     record.StaleUntil.Value > now)
            {
                state = CustomRouteDnsCacheState.Stale;
            }
            else
            {
                state = CustomRouteDnsCacheState.Expired;
            }
        }
        else if (record.LastError is not null)
        {
            state = CustomRouteDnsCacheState.Failed;
        }
        else
        {
            state = CustomRouteDnsCacheState.Missing;
        }

        return new CustomRouteDnsCacheStatus
        {
            CustomRouteEntryId = entry.Id,
            Domain = entry.Value,
            Enabled = entry.Enabled,
            State = state,
            IPv4Addresses = record?.IPv4Addresses ?? [],
            LastAttemptedAt = record?.LastAttemptedAt,
            LastSucceededAt = record?.LastSucceededAt,
            ExpiresAt = record?.ExpiresAt,
            StaleUntil = record?.StaleUntil,
            LastError = record?.LastError
        };
    }
}
