using PathVeer.Core.SystemTools;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Routing;

public sealed class WindowsRouteManager : IRouteManager
{
    private readonly IWindowsRouteApi _routeApi;
    private readonly IFaultInjectionPolicy _faultPolicy;

    public WindowsRouteManager(
        CommandRunner commandRunner,
        IWindowsRouteApi? routeApi = null,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(commandRunner);

        _routeApi = routeApi ?? new WindowsRouteApi(commandRunner);
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
    }

    public async Task<IReadOnlyList<SystemRoute>> GetIpv4RoutesAsync(
        CancellationToken cancellationToken = default)
    {
        if (ShouldFailAt(FaultInjectionPoint.RouteEnumeration))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.RouteEnumeration);
        }

        return await _routeApi.EnumerateAsync(cancellationToken);
    }

    public async Task AddRoutesAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default)
    {
        if (routes.Count == 0)
        {
            return;
        }

        if (ShouldFailAt(FaultInjectionPoint.RouteCreate))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.RouteCreate);
        }

        await _routeApi.AddAsync(routes, cancellationToken);
    }

    public async Task DeleteRoutesAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default)
    {
        if (routes.Count == 0)
        {
            return;
        }

        if (ShouldFailAt(FaultInjectionPoint.RouteDelete))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.RouteDelete);
        }

        await _routeApi.DeleteAsync(routes, cancellationToken);
    }

    private bool ShouldFailAt(FaultInjectionPoint point) =>
        FaultInjectionResolver.ShouldFail(_faultPolicy, point);
}
