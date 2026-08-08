namespace PathVeer.Core.Routing;

public interface IRouteInventoryPersistence
{
    Task<RouteInventory> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        RouteInventory inventory,
        CancellationToken cancellationToken = default);

    Task MutateAsync(
        Func<RouteInventory, RouteInventory> transform,
        CancellationToken cancellationToken = default);
}
