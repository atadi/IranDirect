namespace PathVeer.Core.Runtime;

public interface IRuntimePlanCoordinator
{
    Task<RuntimePlanSnapshot> BuildPlanAsync(
        CancellationToken cancellationToken = default);
}
