namespace IranDirect.Core.Observability;

public interface IRuntimeSnapshotProvider
{
    Task<RuntimeSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}
