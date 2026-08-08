using PathVeer.Core.Diagnostics;
using PathVeer.Core.Diagnostics.Runtime;
using PathVeer.Core.Runtime.Execution;

namespace PathVeer.Core.Tests.Diagnostics.Runtime;

public sealed class RuntimeOperationDiagnosticCheckTests
{
    [Fact]
    public async Task CheckAsync_IdleNoCompletedAt_ReturnsPassed()
    {
        var status = new RuntimeOperationStatus();
        var check = new RuntimeOperationDiagnosticCheck(status);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Equal(DiagnosticSeverity.Info, result.Severity);
        Assert.Null(result.SuggestedAction);
    }

    [Fact]
    public async Task CheckAsync_FailedWithEmptyError_ReturnsFailed()
    {
        var status = new RuntimeOperationStatus();
        status.Begin(OperationState.Failed);
        status.Fail("");

        var check = new RuntimeOperationDiagnosticCheck(status);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
        Assert.NotNull(result.SuggestedAction);
    }

    [Fact]
    public async Task CheckAsync_CompletedWithoutTimestamp_ReturnsFailed()
    {
        var status = new RuntimeOperationStatus();
        status.Begin(OperationState.Repairing);
        status.SetPlannedSteps(1);

        var check = new RuntimeOperationDiagnosticCheck(status);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Equal(DiagnosticSeverity.Error, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_FailedWithNegativePlannedSteps_ReturnsFailed()
    {
        var status = new RuntimeOperationStatus();
        status.Begin(OperationState.Repairing);
        status.Fail("some error");
        status.SetPlannedSteps(-1);

        var check = new RuntimeOperationDiagnosticCheck(status);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("PlannedSteps", result.Message);
    }

    [Fact]
    public async Task CheckAsync_FailedConsistentState_ReturnsPassed()
    {
        var status = new RuntimeOperationStatus();
        status.Begin(OperationState.Repairing);
        status.Fail("some error");

        var check = new RuntimeOperationDiagnosticCheck(status);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Equal(DiagnosticSeverity.Info, result.Severity);
    }

    [Fact]
    public async Task CheckAsync_CompletedWithTimestamp_ReturnsPassed()
    {
        var status = new RuntimeOperationStatus();
        status.Begin(OperationState.Repairing);
        status.SetPlannedSteps(1);

        var progress = (IProgress<RuntimeExecutionProgress>)status;
        progress.Report(new RuntimeExecutionProgress(
            TotalSteps: 1,
            ProcessedSteps: 1,
            SucceededSteps: 1,
            FailedSteps: 0,
            CancelledSteps: 0,
            SkippedSteps: 0));

        var stepResult = new RuntimeExecutionStepResult
        {
            StepIdentity = "step-1",
            Kind = RuntimeExecutionStepKind.AddPrefixRoute,
            DestinationPrefix = "10.0.0.0",
            Status = RuntimeExecutionStepStatus.Succeeded
        };
        var execResult =
            RuntimeExecutionResult.Completed([stepResult]);
        status.Complete(execResult);

        var check = new RuntimeOperationDiagnosticCheck(status);

        DiagnosticResult result = await check.CheckAsync(
            CancellationToken.None);

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
    }
}
