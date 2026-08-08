namespace PathVeer.Core.Observability;

public interface IRuntimeSnapshotProvider
{
    Task<RuntimeSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}
