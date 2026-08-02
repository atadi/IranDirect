using IranDirect.Core.Planning;
using IranDirect.Core.Runtime;

namespace IranDirect.Core.Ipc;

public sealed class ExecutionPreviewCommandHandler
{
    private readonly IRuntimeDecisionBuilder _decisionBuilder;
    private readonly IExecutionPreviewBuilder _previewBuilder;

    public ExecutionPreviewCommandHandler(
        IRuntimeDecisionBuilder decisionBuilder,
        IExecutionPreviewBuilder previewBuilder)
    {
        ArgumentNullException.ThrowIfNull(decisionBuilder);
        ArgumentNullException.ThrowIfNull(previewBuilder);

        _decisionBuilder = decisionBuilder;
        _previewBuilder = previewBuilder;
    }

    public async Task<ServiceResponse> GetAsync(
        CancellationToken cancellationToken = default)
    {
        RuntimeDecision decision =
            await _decisionBuilder.BuildAsync(
                cancellationToken);

        ExecutionPreview preview =
            _previewBuilder.Build(decision);

        return new ServiceResponse
        {
            Success = true,
            Message =
                preview.HasChanges
                    ? "Execution preview computed."
                    : "No changes would be applied.",
            Preview = preview
        };
    }
}
