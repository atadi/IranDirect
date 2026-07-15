namespace IranDirect.Service.Operations;

public sealed class OperationCoordinator : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await operation(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}