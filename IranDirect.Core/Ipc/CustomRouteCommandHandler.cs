using IranDirect.Core.CustomRoutes;

namespace IranDirect.Core.Ipc;

public sealed class CustomRouteCommandHandler
{
    private readonly CustomRouteService _service;
    private readonly ICustomRouteResolver _resolver;

    public CustomRouteCommandHandler(
        CustomRouteService service,
        ICustomRouteResolver resolver)
    {
        _service = service;
        _resolver = resolver;
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
