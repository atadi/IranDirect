namespace PathVeer.Core.Diagnostics.Runtime;

public interface IPathVeerStatusProvider
{
    Task<PathVeerStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);
}
