namespace IranDirect.Core.Support;

public interface ISupportSnapshotProvider
{
    Task<SupportSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default);
}
