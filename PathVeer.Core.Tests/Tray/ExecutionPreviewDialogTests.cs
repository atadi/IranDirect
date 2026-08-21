using PathVeer.Core.Ipc;
using PathVeer.Core.Planning;
using PathVeer.Tray;

namespace PathVeer.Core.Tests.Tray;

public sealed class ExecutionPreviewDialogTests
{
    [Fact]
    public async Task RefreshAsync_DispatchesExecutionPreviewCommand()
    {
        RecordingSender sender = new();
        using ExecutionPreviewDialog dialog = new(sender);

        await dialog.RefreshAsync();

        Assert.Equal(
            [PathVeerCommand.ExecutionPreview],
            sender.Commands);
    }

    [Fact]
    public async Task RefreshAsync_FailedResponse_ReportsError()
    {
        List<string> errors = [];
        RecordingSender sender = new(
            success: false,
            message: "service unavailable");
        using ExecutionPreviewDialog dialog = new(
            sender,
            showError: errors.Add);

        await dialog.RefreshAsync();

        Assert.Single(sender.Commands);
        Assert.Contains("service unavailable", errors);
    }

    [Fact]
    public async Task RefreshAsync_ThrowingSender_ReportsError()
    {
        List<string> errors = [];
        RecordingSender sender = new(throwOnSend: true);
        using ExecutionPreviewDialog dialog = new(
            sender,
            showError: errors.Add);

        await dialog.RefreshAsync();

        Assert.Single(sender.Commands);
        Assert.Contains("boom", errors);
    }

    [Fact]
    public async Task RefreshAsync_NullPreview_ReportsError()
    {
        List<string> errors = [];
        RecordingSender sender = new(hasPreview: false);
        using ExecutionPreviewDialog dialog = new(
            sender,
            showError: errors.Add);

        await dialog.RefreshAsync();

        Assert.Single(sender.Commands);
        Assert.Contains(
            "Execution preview computed.", errors);
    }

    [Fact]
    public async Task RefreshAsync_ExactlyOneIpcCall()
    {
        int callCount = 0;
        RecordingSender sender = new(
            onSend: _ => callCount++);
        using ExecutionPreviewDialog dialog = new(sender);

        await dialog.RefreshAsync();
        await dialog.RefreshAsync();

        Assert.Equal(2, callCount);
        Assert.Equal(2, sender.Commands.Count);
    }

    [Fact]
    public void Dialog_DoesNotDirectlyReferencePlanner()
    {
        Type dialogType = typeof(ExecutionPreviewDialog);
        System.Reflection.Assembly core =
            typeof(PathVeer.Core.Ipc.PathVeerServiceClient)
                .Assembly;
        Type planner = core.GetType(
            "PathVeer.Core.Planning.IRuntimePreviewPlanner",
            throwOnError: true)!;

        System.Reflection.ConstructorInfo[] ctors =
            dialogType.GetConstructors();

        foreach (System.Reflection.ConstructorInfo ctor in ctors)
        {
            System.Reflection.ParameterInfo[] parameters =
                ctor.GetParameters();

            foreach (System.Reflection.ParameterInfo parameter in
                parameters)
            {
                Assert.NotEqual(
                    planner,
                    parameter.ParameterType);
            }
        }
    }

    private sealed class RecordingSender :
        ICustomRouteCommandSender
    {
        private readonly bool _success;
        private readonly string _message;
        private readonly bool _throwOnSend;
        private readonly bool _hasPreview;
        private readonly Action<PathVeerCommand>? _onSend;

        public RecordingSender(
            bool success = true,
            string message = "Execution preview computed.",
            bool throwOnSend = false,
            bool hasPreview = true,
            Action<PathVeerCommand>? onSend = null)
        {
            _success = success;
            _message = message;
            _throwOnSend = throwOnSend;
            _hasPreview = hasPreview;
            _onSend = onSend;
        }

        public List<PathVeerCommand> Commands { get; } = [];

        public Task<ServiceResponse> SendAsync(
            PathVeerCommand command,
            string? value = null,
            string? description = null,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            _onSend?.Invoke(command);

            if (_throwOnSend)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult(new ServiceResponse
            {
                Success = _success,
                Message = _message,
                Preview = _hasPreview && _success
                    ? new ExecutionPreview
                    {
                        CapturedAt = DateTimeOffset.UtcNow,
                        Summary = new ExecutionPreviewSummary
                        {
                            CreateCount = 0,
                            DeleteCount = 0,
                            VerifyCount = 0,
                            InventoryUpdates = 0,
                            CustomRouteUpdates = 0,
                            VpnEndpointUpdates = 0
                        },
                        Steps = []
                    }
                    : null
            });
        }
    }
}
