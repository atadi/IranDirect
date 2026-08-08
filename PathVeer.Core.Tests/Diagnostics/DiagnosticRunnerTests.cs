using PathVeer.Core.Diagnostics;

namespace PathVeer.Core.Tests.Diagnostics;

public sealed class DiagnosticRunnerTests
{
    private sealed class PassingCheck : IDiagnosticCheck
    {
        public string Id => "passing-check";
        public string Title => "Passing Check";

        public Task<DiagnosticResult> CheckAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Passed,
                    Severity: DiagnosticSeverity.Info,
                    Message: "All good.",
                    SuggestedAction: null));
        }
    }

    private sealed class FailingCheck : IDiagnosticCheck
    {
        public string Id => "failing-check";
        public string Title => "Failing Check";

        public Task<DiagnosticResult> CheckAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "Something is wrong.",
                    SuggestedAction: "Fix it."));
        }
    }

    private sealed class ThrowingCheck : IDiagnosticCheck
    {
        public string Id => "throwing-check";
        public string Title => "Throwing Check";

        public Task<DiagnosticResult> CheckAsync(
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "Unexpected failure.");
        }
    }

    [Fact]
    public async Task RunAllAsync_AllPassing_ReportIsHealthy()
    {
        var runner = new DiagnosticRunner(
            new List<IDiagnosticCheck> { new PassingCheck() });

        DiagnosticReport report =
            await runner.RunAllAsync(
                CancellationToken.None);

        Assert.True(report.Healthy);
        Assert.Equal(1, report.PassedCount);
        Assert.Equal(0, report.WarningCount);
        Assert.Equal(0, report.FailedCount);
    }

    [Fact]
    public async Task RunAllAsync_OneFailing_ReportIsUnhealthy()
    {
        var runner = new DiagnosticRunner(
            new List<IDiagnosticCheck>
            {
                new PassingCheck(),
                new FailingCheck()
            });

        DiagnosticReport report =
            await runner.RunAllAsync(
                CancellationToken.None);

        Assert.False(report.Healthy);
        Assert.Equal(1, report.PassedCount);
        Assert.Equal(0, report.WarningCount);
        Assert.Equal(1, report.FailedCount);
    }

    [Fact]
    public async Task RunAllAsync_ThrowsException_BecomesFailedResult()
    {
        var runner = new DiagnosticRunner(
            new List<IDiagnosticCheck> { new ThrowingCheck() });

        DiagnosticReport report =
            await runner.RunAllAsync(
                CancellationToken.None);

        Assert.False(report.Healthy);
        Assert.Equal(0, report.PassedCount);
        Assert.Equal(0, report.WarningCount);
        Assert.Equal(1, report.FailedCount);

        DiagnosticResult result = report.Results[0];
        Assert.Equal("throwing-check", result.Id);
        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.Equal("Unexpected failure.", result.Message);
    }

    [Fact]
    public async Task RunAllAsync_OneFailureDoesNotStopRemaining()
    {
        var runner = new DiagnosticRunner(
            new List<IDiagnosticCheck>
            {
                new FailingCheck(),
                new PassingCheck()
            });

        DiagnosticReport report =
            await runner.RunAllAsync(
                CancellationToken.None);

        Assert.Equal(2, report.Results.Count);
        Assert.Equal(
            DiagnosticStatus.Failed,
            report.Results[0].Status);
        Assert.Equal(
            DiagnosticStatus.Passed,
            report.Results[1].Status);
    }

    [Fact]
    public async Task RunAllAsync_CancellationPropagates()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var runner = new DiagnosticRunner(
            new List<IDiagnosticCheck> { new PassingCheck() });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAllAsync(cts.Token));
    }

    [Fact]
    public async Task RunAllAsync_ResultOrderMatchesRegistrationOrder()
    {
        var check1 = new PassingCheck();
        var check2 = new FailingCheck();

        var runner = new DiagnosticRunner(
            new List<IDiagnosticCheck> { check1, check2 });

        DiagnosticReport report =
            await runner.RunAllAsync(
                CancellationToken.None);

        Assert.Equal("passing-check", report.Results[0].Id);
        Assert.Equal("failing-check", report.Results[1].Id);
    }

    [Fact]
    public async Task RunAllAsync_EmptyCheckList_ReturnsEmptyReport()
    {
        var runner = new DiagnosticRunner(
            new List<IDiagnosticCheck>());

        DiagnosticReport report =
            await runner.RunAllAsync(
                CancellationToken.None);

        Assert.Empty(report.Results);
        Assert.Equal(0, report.PassedCount);
        Assert.Equal(0, report.WarningCount);
        Assert.Equal(0, report.FailedCount);
        Assert.True(report.Healthy);
    }

    [Fact]
    public async Task RunAllAsync_CapturedAtIsRecent()
    {
        var runner = new DiagnosticRunner(
            new List<IDiagnosticCheck> { new PassingCheck() });

        DiagnosticReport report =
            await runner.RunAllAsync(
                CancellationToken.None);

        Assert.True(
            report.CapturedAt >= DateTimeOffset.UtcNow.AddSeconds(-1));
    }
}