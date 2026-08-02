using IranDirect.Core.Diagnostics;
using IranDirect.Core.Ipc;
using IranDirect.Tray;

namespace IranDirect.Core.Tests.Tray;

public sealed class DiagnosticReportDialogTests
{
    [Fact]
    public async Task RefreshAsync_DispatchesDiagnosticsCommand()
    {
        RecordingSender sender = new();
        using DiagnosticReportDialog dialog = new(sender);

        await dialog.RefreshAsync();

        Assert.Equal(
            [IranDirectCommand.Diagnostics],
            sender.Commands);
    }

    [Fact]
    public async Task RefreshAsync_FailedResponse_ReportsError()
    {
        List<string> errors = [];
        RecordingSender sender = new(
            success: false,
            message: "service unavailable");
        using DiagnosticReportDialog dialog = new(
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
        using DiagnosticReportDialog dialog = new(
            sender,
            showError: errors.Add);

        await dialog.RefreshAsync();

        Assert.Single(sender.Commands);
        Assert.Contains("boom", errors);
    }

    [Fact]
    public async Task RefreshAsync_NullReport_ReportsError()
    {
        List<string> errors = [];
        RecordingSender sender = new(hasReport: false);
        using DiagnosticReportDialog dialog = new(
            sender,
            showError: errors.Add);

        await dialog.RefreshAsync();

        Assert.Single(sender.Commands);
        Assert.Contains("Diagnostics completed.", errors);
    }

    [Fact]
    public async Task RefreshAsync_ExactlyOneIpcCall()
    {
        int callCount = 0;
        RecordingSender sender = new(
            onSend: _ => callCount++);
        using DiagnosticReportDialog dialog = new(sender);

        await dialog.RefreshAsync();
        await dialog.RefreshAsync();

        Assert.Equal(2, callCount);
        Assert.Equal(2, sender.Commands.Count);
    }

    private sealed class RecordingSender :
        ICustomRouteCommandSender
    {
        private readonly bool _success;
        private readonly string _message;
        private readonly bool _throwOnSend;
        private readonly bool _hasReport;
        private readonly Action<IranDirectCommand>? _onSend;

        public RecordingSender(
            bool success = true,
            string message = "Diagnostics completed.",
            bool throwOnSend = false,
            bool hasReport = true,
            Action<IranDirectCommand>? onSend = null)
        {
            _success = success;
            _message = message;
            _throwOnSend = throwOnSend;
            _hasReport = hasReport;
            _onSend = onSend;
        }

        public List<IranDirectCommand> Commands { get; } = [];

        public Task<ServiceResponse> SendAsync(
            IranDirectCommand command,
            string? value = null,
            string? description = null,
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
                Report = _hasReport && _success
                    ? new DiagnosticReport(
                        DateTimeOffset.UtcNow,
                        [
                            new DiagnosticResult(
                                "test-check",
                                "Test Check",
                                DiagnosticStatus.Passed,
                                DiagnosticSeverity.Pass,
                                "All good",
                                null)
                        ])
                    : null
            });
        }
    }
}
