using PathVeer.Core.Runtime;

namespace PathVeer.Core.Planning;

public interface IRuntimePreviewPlanner
{
    Task<RuntimeDecision> BuildDecisionAsync(
        CancellationToken cancellationToken = default);

    Task<ExecutionPreview> BuildPreviewAsync(
        CancellationToken cancellationToken = default);
}
