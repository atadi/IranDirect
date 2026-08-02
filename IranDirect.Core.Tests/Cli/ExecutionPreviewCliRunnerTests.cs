using IranDirect.Cli;
using IranDirect.Core.Ipc;
using IranDirect.Core.Planning;

namespace IranDirect.Core.Tests.Cli;

public sealed class ExecutionPreviewCliRunnerTests
{
    [Fact]
    public async Task RunAsync_NoArgs_ReturnsSuccess()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(CreatePreview()));

        (int exitCode, string stdout, string stderr) =
            await RunAsync([], sender);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.Contains("=== Execution Preview ===", stdout);
    }

    [Fact]
    public async Task RunAsync_ShowsSummary()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(CreatePreview()));

        (int exitCode, string stdout, string stderr) =
            await RunAsync([], sender);

        Assert.Equal(0, exitCode);
        Assert.Contains("Summary", stdout);
    }

    [Fact]
    public async Task RunAsync_ShowsHasChanges()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(CreatePreview()));

        (int exitCode, string stdout, string stderr) =
            await RunAsync([], sender);

        Assert.Equal(0, exitCode);
        Assert.Contains("Has Changes: No", stdout);
    }

    [Fact]
    public async Task RunAsync_ExtraArg_ReturnsUsageExitCode()
    {
        (int exitCode, _, string stderr) =
            await RunAsync(["--unknown"]);

        Assert.Equal(6, exitCode);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task RunAsync_ServiceFailure_ReturnsExitCode2()
    {
        FakeSender sender = new(
            (_, _, _) => new ServiceResponse
            {
                Success = false,
                ErrorCode = "COMMAND_FAILED",
                Message = "boom"
            });

        (int exitCode, _, string stderr) =
            await RunAsync([], sender);

        Assert.Equal(2, exitCode);
        Assert.Contains("[COMMAND_FAILED] boom", stderr);
    }

    [Fact]
    public async Task RunAsync_NullPreview_ReturnsExitCode2()
    {
        FakeSender sender = new(
            (_, _, _) => new ServiceResponse
            {
                Success = true,
                Message = "ok."
            });

        (int exitCode, _, string stderr) =
            await RunAsync([], sender);

        Assert.Equal(2, exitCode);
        Assert.Contains("execution preview", stderr);
    }

    [Fact]
    public async Task RunAsync_Timeout_ReturnsExitCode3()
    {
        FakeSender sender = new(
            (_, _, _) =>
                throw new TimeoutException(
                    "IranDirect Service is unavailable."));

        (int exitCode, _, string stderr) =
            await RunAsync([], sender);

        Assert.Equal(3, exitCode);
        Assert.Contains("unavailable", stderr);
    }

    [Fact]
    public async Task RunAsync_Canceled_ReturnsExitCode4()
    {
        FakeSender sender = new(
            (_, _, _) =>
                throw new OperationCanceledException());

        (int exitCode, _, string stderr) =
            await RunAsync([], sender);

        Assert.Equal(4, exitCode);
        Assert.Contains("canceled", stderr);
    }

    [Fact]
    public async Task RunAsync_SendsExecutionPreviewCommand()
    {
        IranDirectCommand? sent = null;
        FakeSender sender = new(
            (command, _, _) =>
            {
                sent = command;
                return SuccessResponse(CreatePreview());
            });

        await RunAsync([], sender);

        Assert.Equal(
            IranDirectCommand.ExecutionPreview, sent);
    }

    [Fact]
    public async Task RunAsync_WithChanges_ShowsPlan()
    {
        ExecutionPreview preview = CreatePreviewWithSteps(
        [
            new ExecutionPreviewStep
            {
                Category =
                    ExecutionPreviewCategory.Route,
                Operation =
                    ExecutionPreviewOperation.Create,
                Target = "10.0.0.0/24",
                Reason = "Missing route"
            }
        ]);

        FakeSender sender = new(
            (_, _, _) => SuccessResponse(preview));

        (int exitCode, string stdout, _) =
            await RunAsync([], sender);

        Assert.Equal(0, exitCode);
        Assert.Contains("Execution Plan", stdout);
        Assert.Contains("[Create]", stdout);
        Assert.Contains("Route: 10.0.0.0/24", stdout);
    }

    private static ExecutionPreview CreatePreview()
    {
        return new ExecutionPreview
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
        };
    }

    private static ExecutionPreview CreatePreviewWithSteps(
        ExecutionPreviewStep[] steps)
    {
        int createCount =
            steps.Count(s =>
                s.Operation ==
                ExecutionPreviewOperation.Create);
        int deleteCount =
            steps.Count(s =>
                s.Operation ==
                ExecutionPreviewOperation.Delete);
        int vpnEndpointUpdates =
            steps.Count(s =>
                s.Category ==
                ExecutionPreviewCategory.VpnEndpoint);
        int inventoryUpdates =
            steps.Count(s =>
                s.Category ==
                ExecutionPreviewCategory.Route);

        return new ExecutionPreview
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Summary = new ExecutionPreviewSummary
            {
                CreateCount = createCount,
                DeleteCount = deleteCount,
                VerifyCount = 0,
                InventoryUpdates = inventoryUpdates,
                CustomRouteUpdates = 0,
                VpnEndpointUpdates = vpnEndpointUpdates
            },
            Steps = steps
        };
    }

    private static ServiceResponse SuccessResponse(
        ExecutionPreview preview) =>
        new()
        {
            Success = true,
            Message = "Execution preview computed.",
            Preview = preview
        };

    private static async Task<
        (int ExitCode, string Stdout, string Stderr)>
        RunAsync(
            string[] args,
            FakeSender? sender = null)
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();

        int exitCode =
            await ExecutionPreviewCliRunner.RunAsync(
                args,
                sender ?? new FakeSender(
                    (_, _, _) => SuccessResponse(
                        CreatePreview())),
                stdout,
                stderr);

        return (
            exitCode,
            stdout.ToString(),
            stderr.ToString());
    }

    private sealed class FakeSender :
        ICustomRouteCommandSender
    {
        private readonly Func<
            IranDirectCommand,
            string?,
            string?,
            ServiceResponse> _handler;

        public FakeSender(
            Func<
                IranDirectCommand,
                string?,
                string?,
                ServiceResponse> handler)
        {
            _handler = handler;
        }

        public Task<ServiceResponse> SendAsync(
            IranDirectCommand command,
            string? value = null,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _handler(command, value, description));
        }
    }
}
