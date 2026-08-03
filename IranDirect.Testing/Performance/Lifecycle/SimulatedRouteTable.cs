using System.Net;
using IranDirect.Core.Routing;

namespace IranDirect.Testing.Performance.Lifecycle;

/// <summary>
/// Thread-safe in-memory Windows route table used by the lifecycle
/// simulation harness. Mirrors the observable contract of
/// <see cref="IWindowsRouteApi"/> without touching the real network.
/// </summary>
public sealed class SimulatedRouteTable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SystemRoute> _routes =
        new(StringComparer.OrdinalIgnoreCase);

    public int AddCalls { get; private set; }
    public int DeleteCalls { get; private set; }
    public int EnumerateCalls { get; private set; }
    public int AddedCount { get; private set; }
    public int DeletedCount { get; private set; }

    public void Add(IEnumerable<ManagedRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        ManagedRoute[] batch = routes.ToArray();
        if (batch.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            AddCalls++;
            AddedCount += batch.Length;

            foreach (ManagedRoute route in batch)
            {
                _routes[route.Identity] = new SystemRoute
                {
                    DestinationPrefix = route.DestinationPrefix,
                    NextHop = route.Gateway,
                    InterfaceIndex = route.InterfaceIndex,
                    RouteMetric = route.Metric
                };
            }
        }
    }

    public void Delete(IEnumerable<ManagedRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        ManagedRoute[] batch = routes.ToArray();
        if (batch.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            DeleteCalls++;
            DeletedCount += batch.Length;

            foreach (ManagedRoute route in batch)
            {
                _routes.Remove(route.Identity);
            }
        }
    }

    public IReadOnlyList<SystemRoute> Snapshot()
    {
        lock (_gate)
        {
            EnumerateCalls++;

            return _routes.Values
                .OrderBy(
                    route => route.DestinationPrefix,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(route => route.NextHop.ToString())
                .ThenBy(route => route.InterfaceIndex)
                .ToArray();
        }
    }

    public IReadOnlyList<SystemRoute> Peek()
    {
        lock (_gate)
        {
            return _routes.Values.ToArray();
        }
    }

    public bool Contains(
        string destinationPrefix,
        IPAddress nextHop,
        uint interfaceIndex)
    {
        string identity =
            $"{destinationPrefix}|{nextHop}|{interfaceIndex}";

        lock (_gate)
        {
            return _routes.ContainsKey(identity);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _routes.Clear();
        }
    }
}
