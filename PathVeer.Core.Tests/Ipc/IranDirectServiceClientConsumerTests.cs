using PathVeer.Cli;
using PathVeer.Core.Ipc;
using PathVeer.Core.Testing.FaultInjection;
using PathVeer.Tray;

namespace PathVeer.Core.Tests.Ipc;

public sealed class IranDirectServiceClientConsumerTests
{
    [Fact]
    public async Task CliRunner_ThrowingClient_ReceivesExistingFailureOutcome()
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();

        int exitCode = await CustomRouteCliRunner.RunAsync(
            ["list"],
            new ThrowingSender(),
            stdout,
            stderr);

        Assert.Equal(1, exitCode);
        Assert.Empty(stdout.ToString());
        Assert.Contains(
            "PathVeer command failed: " +
            "Fault injected at NamedPipeSend.",
            stderr.ToString());
    }

    [Fact]
    public async Task TrayDialog_ThrowingClient_ReportsConciseError()
    {
        List<string> errors = [];
        using ExecutionPreviewDialog dialog = new(
            new ThrowingSender(),
            showError: errors.Add);

        await dialog.RefreshAsync();

        Assert.Contains(
            "Fault injected at NamedPipeSend.",
            errors);
    }

    private sealed class ThrowingSender :
        ICustomRouteCommandSender
    {
        public async Task<ServiceResponse> SendAsync(
            PathVeerCommand command,
            string? value = null,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            throw new FaultInjectionException(
                FaultInjectionPoint.NamedPipeSend);
        }
    }
}
