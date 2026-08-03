using IranDirect.Core.Routing;

namespace IranDirect.Testing.Performance.Lifecycle;

/// <summary>
/// An <see cref="IWindowsRouteApi"/> implementation backed by an
/// in-memory <see cref="SimulatedRouteTable"/>. Intended to be wrapped
/// by the production <see cref="WindowsRouteManager"/> so that route
/// enumeration/create/delete flow through production code paths.
/// </summary>
public sealed class FakeWindowsRouteApi : IWindowsRouteApi
{
    private readonly SimulatedRouteTable _table;

    public FakeWindowsRouteApi(SimulatedRouteTable table)
    {
        _table = table ?? throw new ArgumentNullException(
            nameof(table));
    }

    public Task<IReadOnlyList<SystemRoute>> EnumerateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_table.Snapshot());
    }

    public Task AddAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _table.Add(routes);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        IReadOnlyCollection<ManagedRoute> routes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _table.Delete(routes);
        return Task.CompletedTask;
    }
}
