using IranDirect.Core.CustomRoutes;

namespace IranDirect.Core.Ipc;

public sealed class CustomRouteCommandHandler
{
    private readonly CustomRouteService _service;
    private readonly ICustomRouteResolver _resolver;
    private readonly CustomRouteDnsCacheService _dnsCache;

    public CustomRouteCommandHandler(
        CustomRouteService service,
        ICustomRouteResolver resolver,
        CustomRouteDnsCacheService dnsCache)
    {
        _service = service;
        _resolver = resolver;
        _dnsCache = dnsCache;
    }

    public async Task<ServiceResponse> ListAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CustomRouteEntry> entries =
            await _service.GetAllAsync(cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message =
                entries.Count == 0
                    ? "No custom routes configured."
                    : $"Retrieved {entries.Count} custom route(s).",
            CustomRoutes = entries
        };
    }

    public async Task<ServiceResponse> AddAsync(
        CustomRouteEntryType type,
        string? value,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CustomRouteEntry added =
                await _service.AddAsync(
                    type,
                    value,
                    description,
                    enabled: true,
                    cancellationToken);

            IReadOnlyList<CustomRouteEntry> entries =
                await _service.GetAllAsync(cancellationToken);

            return new ServiceResponse
            {
                Success = true,
                Message =
                    $"Added {type} custom route " +
                    $"'{added.Value}'.",
                CustomRoutes = entries
            };
        }
        catch (ArgumentException exception)
        {
            return Failure(
                "INVALID_CUSTOM_ROUTE",
                exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(
                "DUPLICATE_CUSTOM_ROUTE",
                exception.Message);
        }
    }

    public async Task<ServiceResponse> SetEnabledAsync(
        string? idText,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseId(idText, out Guid id))
        {
            return Failure(
                "INVALID_CUSTOM_ROUTE_ID",
                "A valid custom route ID (GUID) is required.");
        }

        try
        {
            await _service.SetEnabledAsync(
                id,
                enabled,
                cancellationToken);

            IReadOnlyList<CustomRouteEntry> entries =
                await _service.GetAllAsync(cancellationToken);

            return new ServiceResponse
            {
                Success = true,
                Message =
                    enabled
                        ? "Custom route enabled."
                        : "Custom route disabled.",
                CustomRoutes = entries
            };
        }
        catch (KeyNotFoundException exception)
        {
            return Failure(
                "CUSTOM_ROUTE_NOT_FOUND",
                exception.Message);
        }
    }

    public async Task<ServiceResponse> RemoveAsync(
        string? idText,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseId(idText, out Guid id))
        {
            return Failure(
                "INVALID_CUSTOM_ROUTE_ID",
                "A valid custom route ID (GUID) is required.");
        }

        bool removed =
            await _service.RemoveAsync(
                id,
                cancellationToken);

        if (!removed)
        {
            return Failure(
                "CUSTOM_ROUTE_NOT_FOUND",
                $"Custom route '{id}' was not found.");
        }

        IReadOnlyList<CustomRouteEntry> entries =
            await _service.GetAllAsync(cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message = "Custom route removed.",
            CustomRoutes = entries
        };
    }

    public async Task<ServiceResponse> ResolveAsync(
        CancellationToken cancellationToken = default)
    {
        CustomRouteResolutionResult result =
            await _resolver.ResolveAsync(cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message =
                result.AllSucceeded
                    ? $"Resolved {result.Prefixes.Count} prefix(es)."
                    : $"Resolved {result.Prefixes.Count} prefix(es); " +
                      $"{result.Failures.Count} failure(s).",
            CustomRouteResolution = result
        };
    }

    public async Task<ServiceResponse> CacheStatusAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CustomRouteDnsCacheStatus> statuses =
            await _dnsCache.GetStatusAsync(cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message = $"Retrieved {statuses.Count} domain(s).",
            CustomRouteDnsCacheStatuses = statuses
        };
    }

    public async Task<ServiceResponse> InvalidateCacheAsync(
        string? idText,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseId(idText, out Guid id) ||
            id == Guid.Empty)
        {
            return Failure(
                "INVALID_CUSTOM_ROUTE_ID",
                "A valid custom route ID (GUID) is required.");
        }

        try
        {
            bool removed =
                await _dnsCache.InvalidateAsync(
                    id,
                    cancellationToken);

            return new ServiceResponse
            {
                Success = true,
                Message =
                    removed
                        ? "Custom route cache invalidated."
                        : "No cached DNS record for this domain."
            };
        }
        catch (KeyNotFoundException exception)
        {
            return Failure(
                "CUSTOM_ROUTE_NOT_FOUND",
                exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure(
                "CACHE_OPERATION_FAILED",
                "The DNS cache operation failed.");
        }
    }

    public async Task<ServiceResponse> InvalidateAllCachesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            int removed =
                await _dnsCache.InvalidateAllAsync(
                    cancellationToken);

            return new ServiceResponse
            {
                Success = true,
                Message =
                    removed == 0
                        ? "No cached DNS records to invalidate."
                        : $"Invalidated {removed} DNS cache " +
                          $"record(s)."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure(
                "CACHE_OPERATION_FAILED",
                "The DNS cache operation failed.");
        }
    }

    private static bool TryParseId(
        string? idText,
        out Guid id) =>
        Guid.TryParse(idText, out id);

    private static ServiceResponse Failure(
        string errorCode,
        string message)
    {
        return new ServiceResponse
        {
            Success = false,
            ErrorCode = errorCode,
            Message = message
        };
    }
}
