namespace IranDirect.Core.Diagnostics.Runtime;

public interface IIranDirectStatusProvider
{
    Task<IranDirectStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);
}
