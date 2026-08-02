using IranDirect.Core.Configuration;
using IranDirect.Core.Diagnostics;
using IranDirect.Core.Runtime.Execution;

namespace IranDirect.Core.Diagnostics.Runtime;

public sealed class RuntimeStateDiagnosticCheck :
    IDiagnosticCheck
{
    private readonly IIranDirectStatusProvider _statusProvider;

    public RuntimeStateDiagnosticCheck(
        IIranDirectStatusProvider statusProvider)
    {
        _statusProvider = statusProvider;
    }

    public string Id => "runtime-state";
    public string Title => "Runtime state";

    public async Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            IranDirectStatus status =
                await _statusProvider.GetStatusAsync(
                    cancellationToken);

            if (status is null)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "Runtime state is null.",
                    SuggestedAction:
                        "Ensure the runtime is initialized.");
            }

            if (status.DesiredEnabled is null)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "DesiredEnabled is not set.",
                    SuggestedAction:
                        "Ensure the desired configuration is loaded.");
            }

            bool desiredEnabled = status.DesiredEnabled.Value;
            bool appliedEnabled = status.Enabled;

            if (desiredEnabled && !appliedEnabled)
            {
                if (status.Operation is not null &&
                    status.Operation.State == OperationState.Idle)
                {
                    return new DiagnosticResult(
                        Id: Id,
                        Title: Title,
                        Status: DiagnosticStatus.Warning,
                        Severity: DiagnosticSeverity.Warning,
                        Message:
                            "Desired is enabled but applied is disabled " +
                            "with no active operation.",
                        SuggestedAction:
                            "Run a repair cycle to apply the desired state.");
                }
            }

            if (!desiredEnabled && appliedEnabled)
            {
                if (status.Operation is not null &&
                    status.Operation.State == OperationState.Idle)
                {
                    return new DiagnosticResult(
                        Id: Id,
                        Title: Title,
                        Status: DiagnosticStatus.Warning,
                        Severity: DiagnosticSeverity.Warning,
                        Message:
                            "Desired is disabled but applied is enabled " +
                            "with no active operation.",
                        SuggestedAction:
                            "Run a disable cycle to apply the desired state.");
                }
            }

            if (status.Operation is not null &&
                status.Operation.State != OperationState.Idle)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Passed,
                    Severity: DiagnosticSeverity.Info,
                    Message:
                        $"Operation in progress: " +
                        $"{status.Operation.State}.",
                    SuggestedAction: null);
            }

            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message: "Runtime state is consistent.",
                SuggestedAction: null);
        }
        catch (Exception exception)
        {
            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Failed,
                Severity: DiagnosticSeverity.Error,
                Message:
                    $"Could not read runtime state: " +
                    $"{exception.Message}",
                SuggestedAction:
                    "Ensure the runtime is initialized and the service is running.");
        }
    }
}