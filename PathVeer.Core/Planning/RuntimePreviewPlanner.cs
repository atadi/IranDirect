using PathVeer.Core.Runtime;

namespace PathVeer.Core.Planning;

public sealed class RuntimePreviewPlanner : IRuntimePreviewPlanner
{
    private readonly IRuntimeDecisionBuilder _decisionBuilder;
    private readonly IExecutionPreviewBuilder _previewBuilder;

    public RuntimePreviewPlanner(
        IRuntimeDecisionBuilder decisionBuilder,
        IExecutionPreviewBuilder previewBuilder)
    {
        ArgumentNullException.ThrowIfNull(decisionBuilder);
        ArgumentNullException.ThrowIfNull(previewBuilder);

        _decisionBuilder = decisionBuilder;
        _previewBuilder = previewBuilder;
    }

    public Task<RuntimeDecision> BuildDecisionAsync(
        CancellationToken cancellationToken = default)
    {
        return _decisionBuilder.BuildAsync(cancellationToken);
    }

    public Task<ExecutionPreview> BuildPreviewAsync(
        CancellationToken cancellationToken = default)
    {
        return BuildPreviewCoreAsync(cancellationToken);
    }

    private async Task<ExecutionPreview> BuildPreviewCoreAsync(
        CancellationToken cancellationToken)
    {
        RuntimeDecision decision =
            await _decisionBuilder.BuildAsync(cancellationToken);

        ExecutionPreview preview =
            _previewBuilder.Build(decision);

        return preview;
    }
}
