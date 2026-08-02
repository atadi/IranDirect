using IranDirect.Core.Diagnostics;
using IranDirect.Core.Runtime.Execution;

namespace IranDirect.Core.Diagnostics.Runtime;

public sealed class RuntimeOperationDiagnosticCheck :
    IDiagnosticCheck
{
    private readonly RuntimeOperationStatus _operationStatus;

    public RuntimeOperationDiagnosticCheck(
        RuntimeOperationStatus operationStatus)
    {
        _operationStatus = operationStatus;
    }

    public string Id => "runtime-operation";
    public string Title => "Runtime operation";

    public Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        RuntimeOperationSnapshot snapshot =
            _operationStatus.CreateSnapshot();

        if (snapshot.State == OperationState.Idle &&
            snapshot.CompletedAt is null)
        {
            return Task.FromResult(new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message: "No active operation.",
                SuggestedAction: null));
        }

        if (snapshot.State == OperationState.Failed &&
            string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
        {
            return Task.FromResult(new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message:
                    "Operation is in Failed state but " +
                    "LastError is empty.",
                SuggestedAction:
                    "Ensure failed operations include an error message."));
        }

        if (snapshot.CompletedAt is null &&
            snapshot.State != OperationState.Idle)
        {
            return Task.FromResult(new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message:
                    "Completed operation has no CompletedAt timestamp.",
                SuggestedAction:
                    "Ensure completed operations record their completion time."));
        }

        if (snapshot.PlannedSteps < 0)
        {
            return Task.FromResult(new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message: "PlannedSteps is negative.",
                SuggestedAction:
                    "Ensure planned step count is non-negative."));
        }

        if (snapshot.CompletedSteps < 0)
        {
            return Task.FromResult(new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message: "CompletedSteps is negative.",
                SuggestedAction:
                    "Ensure completed step count is non-negative."));
        }

        if (snapshot.SucceededSteps < 0)
        {
            return Task.FromResult(new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message: "SucceededSteps is negative.",
                SuggestedAction:
                    "Ensure succeeded step count is non-negative."));
        }

        if (snapshot.FailedSteps < 0)
        {
            return Task.FromResult(new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message: "FailedSteps is negative.",
                SuggestedAction:
                    "Ensure failed step count is non-negative."));
        }

        if (snapshot.CompletedSteps > snapshot.PlannedSteps)
        {
            return Task.FromResult(new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message:
                    $"CompletedSteps ({snapshot.CompletedSteps}) " +
                    $"exceeds PlannedSteps ({snapshot.PlannedSteps}).",
                SuggestedAction:
                    "Ensure step counts are consistent."));
        }

        if (snapshot.SucceededSteps + snapshot.FailedSteps +
            snapshot.CancelledSteps + snapshot.SkippedSteps >
            snapshot.PlannedSteps)
        {
            return Task.FromResult(new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message:
                    "Sum of step outcome counts exceeds PlannedSteps.",
                SuggestedAction:
                    "Ensure step counts are consistent."));
        }

        return Task.FromResult(new DiagnosticResult(
            Id: Id,
            Title: Title,
            Status: DiagnosticStatus.Passed,
            Severity: DiagnosticSeverity.Info,
            Message: "Operation state is consistent.",
            SuggestedAction: null));
    }
}