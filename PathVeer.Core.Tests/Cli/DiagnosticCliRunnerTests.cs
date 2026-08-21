using PathVeer.Cli;
using PathVeer.Core.Diagnostics;
using PathVeer.Core.Ipc;

namespace PathVeer.Core.Tests.Cli;

public sealed class DiagnosticCliRunnerTests
{
    private static DiagnosticReport CreateReport(
        int passed = 0,
        int warnings = 0,
        int failed = 0)
    {
        var results = new List<DiagnosticResult>();

        for (int i = 0; i < passed; i++)
        {
            results.Add(new DiagnosticResult(
                Id: $"pass-{i}",
                Title: $"Check {i}",
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message: $"check {i} ok",
                SuggestedAction: null));
        }

        for (int i = 0; i < warnings; i++)
        {
            results.Add(new DiagnosticResult(
                Id: $"warn-{i}",
                Title: $"Warning {i}",
                Status: DiagnosticStatus.Warning,
                Severity: DiagnosticSeverity.Warning,
                Message: $"warning {i}",
                SuggestedAction: null));
        }

        for (int i = 0; i < failed; i++)
        {
            results.Add(new DiagnosticResult(
                Id: $"fail-{i}",
                Title: $"Failure {i}",
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message: $"failure {i}",
                SuggestedAction: "Fix this."));
        }

        return new DiagnosticReport(
            CapturedAt: DateTimeOffset.UtcNow,
            Results: results);
    }

    [Fact]
    public async Task RunAsync_NoArgs_ReturnsDetailedFormat()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(CreateReport(2)));

        (int exitCode, string stdout, string stderr) =
            await RunAsync([], sender);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.Contains("PathVeer Diagnostics", stdout);
        Assert.Contains("Summary", stdout);
    }

    [Fact]
    public async Task RunAsync_SummaryFlag_ReturnsSummaryFormat()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(CreateReport(1)));

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["--summary"], sender);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.Contains("PathVeer Diagnostics", stdout);
        Assert.Contains("Healthy: Yes", stdout);
        Assert.Contains("Passed: 1", stdout);
        Assert.DoesNotContain("Summary", stdout);
    }

    [Fact]
    public async Task RunAsync_CompactFlag_ReturnsCompactFormat()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(CreateReport(1)));

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["--compact"], sender);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.Contains("OK", stdout);
        Assert.Contains("1P", stdout);
    }

    [Fact]
    public async Task RunAsync_DetailedFlag_ReturnsDetailedFormat()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(CreateReport(1)));

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["--detailed"], sender);

        Assert.Equal(0, exitCode);
        Assert.Contains("Summary", stdout);
    }

    [Fact]
    public async Task RunAsync_ShortDFlag_ReturnsDetailedFormat()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(CreateReport(1)));

        (int exitCode, string stdout, string stderr) =
            await RunAsync(["-d"], sender);

        Assert.Equal(0, exitCode);
        Assert.Contains("Summary", stdout);
    }

    [Fact]
    public async Task RunAsync_UnknownArg_ReturnsUsageExitCode()
    {
        (int exitCode, _, string stderr) =
            await RunAsync(["--unknown"]);

        Assert.Equal(6, exitCode);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task RunAsync_Healthy_ReturnsExitCode0()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(
                CreateReport(passed: 3)));

        (int exitCode, _, _) =
            await RunAsync([], sender);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_WarningsOnly_ReturnsExitCode1()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(
                CreateReport(passed: 1, warnings: 1)));

        (int exitCode, _, _) =
            await RunAsync([], sender);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_Failed_ReturnsExitCode2()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(
                CreateReport(failed: 1)));

        (int exitCode, _, _) =
            await RunAsync([], sender);

        Assert.Equal(2, exitCode);
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
    public async Task RunAsync_NullReport_ReturnsExitCode2()
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
        Assert.Contains("diagnostic report", stderr);
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
    public async Task RunAsync_SendsDiagnosticsCommand()
    {
        PathVeerCommand? sent = null;
        FakeSender sender = new(
            (command, _, _) =>
            {
                sent = command;
                return SuccessResponse(CreateReport(1));
            });

        await RunAsync([], sender);

        Assert.Equal(
            PathVeerCommand.Diagnostics, sent);
    }

    [Fact]
    public async Task RunAsync_FailedChecks_IncludesNames()
    {
        FakeSender sender = new(
            (_, _, _) => SuccessResponse(
                CreateReport(failed: 2)));

        (int exitCode, string stdout, _) =
            await RunAsync(["--compact"], sender);

        Assert.Equal(2, exitCode);
        Assert.Contains("FAIL", stdout);
        Assert.Contains("Failure 0", stdout);
        Assert.Contains("Failure 1", stdout);
    }

    private static ServiceResponse SuccessResponse(
        DiagnosticReport report) =>
        new()
        {
            Success = true,
            Message = "Diagnostics completed.",
            Report = report
        };

    private static async Task<
        (int ExitCode, string Stdout, string Stderr)>
        RunAsync(
            string[] args,
            FakeSender? sender = null)
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();

        int exitCode = await DiagnosticCliRunner.RunAsync(
            args,
            sender ?? new FakeSender(
                (_, _, _) => SuccessResponse(
                    CreateReport(1))),
            stdout,
            stderr);

        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed class FakeSender : ICustomRouteCommandSender
    {
        private readonly Func<
            PathVeerCommand,
            string?,
            string?,
            ServiceResponse> _handler;

        public FakeSender(
            Func<
                PathVeerCommand,
                string?,
                string?,
                ServiceResponse> handler)
        {
            _handler = handler;
        }

        public Task<ServiceResponse> SendAsync(
            PathVeerCommand command,
            string? value = null,
            string? description = null,
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _handler(command, value, description));
        }
    }
}
