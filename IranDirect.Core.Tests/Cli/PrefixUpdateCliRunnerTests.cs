using IranDirect.Cli;
using IranDirect.Core.Ipc;
using IranDirect.Core.Prefixes;

namespace IranDirect.Core.Tests.Cli;

public sealed class PrefixUpdateCliRunnerTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_NoSubcommand_ReturnsUsageExitCode()
    {
        (int exitCode, string stdout, string stderr) =
            await RunAsync([]);

        Assert.Equal(6, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task RunAsync_UnknownSubcommand_ReturnsUsageExitCode()
    {
        (int exitCode, _, string stderr) =
            await RunAsync(["download"]);

        Assert.Equal(6, exitCode);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task RunAsync_ExtraArgument_ReturnsUsageExitCode()
    {
        (int exitCode, _, string stderr) =
            await RunAsync(["check", "extra"]);

        Assert.Equal(6, exitCode);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task RunAsync_Check_SendsPrefixUpdateCheckNow()
    {
        IranDirectCommand? sent = null;
        FakeSender sender = new(
            (command, _, _) =>
            {
                sent = command;
                return SuccessResponse(
                    CreateSnapshot(
                        PrefixUpdateCheckStatus.Current));
            });

        (int exitCode, _, _) =
            await RunAsync(["check"], sender);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            IranDirectCommand.PrefixUpdateCheckNow,
            sent);
    }

    [Theory]
    [InlineData(PrefixUpdateCheckStatus.Current, "Current")]
    [InlineData(
        PrefixUpdateCheckStatus.UpdateAvailable,
        "Update Available")]
    [InlineData(PrefixUpdateCheckStatus.Unknown, "Unknown")]
    public async Task RunAsync_CompletedCheck_ReturnsSuccess(
        PrefixUpdateCheckStatus status,
        string expectedText)
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(
                CreateSnapshot(status)));

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["check"], sender);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.Contains($"Status: {expectedText}", stdout);
        Assert.Contains("Checked at:", stdout);
        Assert.Contains("Last successful check:", stdout);
        Assert.Contains("Consecutive failures:", stdout);
        Assert.Contains("Remote ETag:", stdout);
        Assert.Contains("Remote Last Modified:", stdout);
        Assert.Contains("Remote Content Length:", stdout);
        Assert.Contains("Reason:", stdout);
    }

    [Fact]
    public async Task RunAsync_FailedCheck_ReturnsExitCode1()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(
                CreateSnapshot(
                    PrefixUpdateCheckStatus.Failed,
                    reason: "Remote check failed: network down",
                    consecutiveFailures: 2)));

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["check"], sender);

        Assert.Equal(1, exitCode);
        Assert.Empty(stderr);
        Assert.Contains("Status: Failed", stdout);
        Assert.Contains("Consecutive failures: 2", stdout);
        Assert.Contains(
            "Reason: Remote check failed: network down",
            stdout);
    }

    [Fact]
    public async Task RunAsync_ServiceFailure_ReturnsExitCode1()
    {
        FakeSender sender = new(
            (_, _, _) => new ServiceResponse
            {
                Success = false,
                ErrorCode = "COMMAND_FAILED",
                Message = "boom"
            });

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["check"], sender);

        Assert.Equal(1, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("[COMMAND_FAILED] boom", stderr);
    }

    [Fact]
    public async Task RunAsync_MissingMonitorState_ReturnsExitCode1()
    {
        FakeSender sender = new(
            (_, _, _) => new ServiceResponse
            {
                Success = true,
                Message = "ok."
            });

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["check"], sender);

        Assert.Equal(1, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("monitor state", stderr);
    }

    [Fact]
    public async Task RunAsync_Timeout_ReturnsTimeoutExitCode()
    {
        FakeSender sender = new(
            (_, _, _) =>
                throw new TimeoutException(
                    "IranDirect Service is unavailable."));

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["check"], sender);

        Assert.Equal(3, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("unavailable", stderr);
    }

    [Fact]
    public async Task RunAsync_Canceled_ReturnsCanceledExitCode()
    {
        FakeSender sender = new(
            (_, _, _) =>
                throw new OperationCanceledException());

        (int exitCode, _, string stderr) =
            await RunAsync(["check"], sender);

        Assert.Equal(4, exitCode);
        Assert.Contains("canceled", stderr);
    }

    private static PrefixUpdateMonitorSnapshot CreateSnapshot(
        PrefixUpdateCheckStatus status,
        string? reason = null,
        int consecutiveFailures = 0) =>
        new()
        {
            CurrentResult = new PrefixUpdateCheckResult
            {
                Status = status,
                CheckedAt = BaseTime,
                Reason = reason,
                RemoteMetadata =
                    new PrefixUpdateCheckRemoteMetadata
                    {
                        ETag = "\"etag1\"",
                        LastModified = BaseTime,
                        ContentLength = 512
                    }
            },
            LastCheckedAt = BaseTime,
            LastSuccessfulCheckAt =
                status == PrefixUpdateCheckStatus.Failed
                    ? null
                    : BaseTime,
            ConsecutiveFailures = consecutiveFailures,
            Running = true
        };

    private static ServiceResponse SuccessResponse(
        PrefixUpdateMonitorSnapshot snapshot) =>
        new()
        {
            Success = true,
            Message = "Prefix update check completed.",
            PrefixUpdateMonitor = snapshot
        };

    private static async Task<(int ExitCode, string Stdout, string Stderr)>
        RunAsync(
            string[] args,
            FakeSender? sender = null)
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();

        int exitCode = await PrefixUpdateCliRunner.RunAsync(
            args,
            sender ?? new FakeSender(
                (_, _, _) => SuccessResponse(
                    CreateSnapshot(
                        PrefixUpdateCheckStatus.Current))),
            stdout,
            stderr);

        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed class FakeSender : ICustomRouteCommandSender
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
