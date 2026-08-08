using PathVeer.Core.Planning;

namespace PathVeer.Core.Ipc;

public sealed class ExecutionPreviewCommandHandler
{
    private readonly IRuntimePreviewPlanner _planner;

    public ExecutionPreviewCommandHandler(
        IRuntimePreviewPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(planner);

        _planner = planner;
    }

    public async Task<ServiceResponse> GetAsync(
        CancellationToken cancellationToken = default)
    {
        ExecutionPreview preview =
            await _planner.BuildPreviewAsync(cancellationToken);

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
