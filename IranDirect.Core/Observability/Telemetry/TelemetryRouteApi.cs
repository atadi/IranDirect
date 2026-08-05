using IranDirect.Core.Routing;

namespace IranDirect.Core.Observability.Telemetry;

/// <summary>
/// Narrow telemetry decorator around <see cref="IWindowsRouteApi"/>. Emits one
/// <c>Routes.*</c> activity and one set of route-operation measurements per
/// native system call, preserving the exact native behavior and exception
/// contract of the wrapped API.
///
/// This is the single approved place that turns a native route boundary call
/// into telemetry: <c>WindowsRouteApi</c> stays native-only and
/// <see cref="IranDirect.Core.Routing"/> stays telemetry-free, so this decorator
/// (in the <c>Observability.Telemetry</c> namespace) bridges the two without
/// either production boundary referencing the other's concerns.
///
/// Empty batches return before any native I/O and therefore emit no activity
/// and no metrics (mirroring <see cref="WindowsRouteApi"/>, which also guards
/// on an empty collection). The native command text, batch size, per-route
/// data, and process output are never placed on spans or metrics.
/// </summary>
public sealed class TelemetryRouteApi : IWindowsRouteApi
{
    private readonly IWindowsRouteApi _inner;

    public TelemetryRouteApi(IWindowsRouteApi inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public async Task<IReadOnlyList<SystemRoute>> EnumerateAsync(
        CancellationToken cancellationToken = default)
    {
        using var scope = RouteSystemCallTelemetry.StartEnumeration();
        try
        {
            IReadOnlyList<SystemRoute> result =
                await _inner.EnumerateAsync(cancellationToken);
            scope.CompleteSuccess();
            return result;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error; rethrow the original unchanged.
            scope.CompleteCancelled();
            throw;
        }
        catch (Exception ex)
        {
            scope.CompleteFailure(ex);
            throw;
        }
    }

    public async Task AddAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default)
    {
        if (routes.Count == 0)
        {
            // Empty batch: no native I/O, therefore no telemetry.
            return;
        }

        using var scope = RouteSystemCallTelemetry.StartCreate(
            KindOf(routes));
        try
        {
            await _inner.AddAsync(routes, cancellationToken);
            scope.CompleteSuccess();
        }
        catch (OperationCanceledException)
        {
            scope.CompleteCancelled();
            throw;
        }
        catch (Exception ex)
        {
            scope.CompleteFailure(ex);
            throw;
        }
    }

    public async Task DeleteAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default)
    {
        if (routes.Count == 0)
        {
            // Empty batch: no native I/O, therefore no telemetry.
            return;
        }

        using var scope = RouteSystemCallTelemetry.StartDelete(
            KindOf(routes));
        try
        {
            await _inner.DeleteAsync(routes, cancellationToken);
            scope.CompleteSuccess();
        }
        catch (OperationCanceledException)
        {
            scope.CompleteCancelled();
            throw;
        }
        catch (Exception ex)
        {
            scope.CompleteFailure(ex);
            throw;
        }
    }

    // The native WindowsRouteApi boundary receives ManagedRoute records that
    // carry no route-kind discriminator, so the entire-batch kind cannot be
    // authoritatively determined here. Per the Phase 32.6 contract we use
    // unknown rather than inspecting individual routes to infer one. If a
    // batch were ever homogeneous with a known discriminator this helper would
    // read it; today it conservatively returns unknown.
    private static RouteSystemCallTelemetry.RouteSystemCallKind KindOf(
        IReadOnlyCollection<ManagedRoute> routes) =>
        RouteSystemCallTelemetry.RouteSystemCallKind.Unknown;
}
