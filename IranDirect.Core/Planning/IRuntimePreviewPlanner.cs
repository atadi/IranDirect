using IranDirect.Core.Runtime;

namespace IranDirect.Core.Planning;

public interface IRuntimePreviewPlanner
{
    Task<RuntimeDecision> BuildDecisionAsync(
        CancellationToken cancellationToken = default);

    Task<ExecutionPreview> BuildPreviewAsync(
        CancellationToken cancellationToken = default);
}
