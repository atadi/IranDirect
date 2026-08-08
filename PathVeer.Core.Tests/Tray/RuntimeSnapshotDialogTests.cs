using PathVeer.Core.Ipc;
using PathVeer.Core.Observability;
using PathVeer.Tray;

namespace PathVeer.Core.Tests.Tray;

public sealed class RuntimeSnapshotDialogTests
{
    [Fact]
    public async Task RefreshAsync_DispatchesRuntimeSnapshotCommand()
    {
        RecordingSender sender = new();
        using RuntimeSnapshotDialog dialog = new(sender);

        await dialog.RefreshAsync();

        Assert.Equal(
            [PathVeerCommand.RuntimeSnapshot],
            sender.Commands);
    }

    [Fact]
    public async Task RefreshAsync_FailedResponse_ReportsError()
    {
        List<string> errors = [];
        RecordingSender sender = new(
            success: false,
            message: "service unavailable");
        using RuntimeSnapshotDialog dialog = new(
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
        using RuntimeSnapshotDialog dialog = new(
            sender,
            showError: errors.Add);

        await dialog.RefreshAsync();

        Assert.Single(sender.Commands);
        Assert.Contains("boom", errors);
    }

    private sealed class RecordingSender :
        ICustomRouteCommandSender
    {
        private readonly bool _success;
        private readonly string _message;
        private readonly bool _throwOnSend;

        public RecordingSender(
            bool success = true,
            string message = "Runtime snapshot captured.",
            bool throwOnSend = false)
        {
            _success = success;
            _message = message;
            _throwOnSend = throwOnSend;
        }

        public List<PathVeerCommand> Commands { get; } = [];

        public Task<ServiceResponse> SendAsync(
            PathVeerCommand command,
            string? value = null,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);

            if (_throwOnSend)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult(new ServiceResponse
            {
                Success = _success,
                Message = _message,
                Snapshot = _success
                    ? new RuntimeSnapshot
                    {
                        CapturedAt = DateTimeOffset.UtcNow
                    }
                    : null
            });
        }
    }
}
