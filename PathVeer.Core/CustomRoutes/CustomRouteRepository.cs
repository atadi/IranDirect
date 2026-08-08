namespace PathVeer.Core.CustomRoutes;

public sealed class CustomRouteRepository :
    ICustomRouteRepository
{
    private readonly CustomRouteStore _store;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public CustomRouteRepository(
        CustomRouteStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<CustomRouteEntry>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        CustomRouteCollection collection =
            await _store.LoadAsync(cancellationToken);

        return collection.Entries;
    }

    public async Task MutateAsync(
        Func<CustomRouteCollection, CustomRouteCollection> transform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transform);

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            CustomRouteCollection current =
                await _store.LoadAsync(cancellationToken);

            CustomRouteCollection updated =
                transform(current);

            await _store.SaveAsync(
                updated,
                cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }
}
