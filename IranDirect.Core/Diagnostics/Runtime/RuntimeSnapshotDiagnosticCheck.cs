using IranDirect.Core.Diagnostics;
using IranDirect.Core.Observability;

namespace IranDirect.Core.Diagnostics.Runtime;

public sealed class RuntimeSnapshotDiagnosticCheck :
    IDiagnosticCheck
{
    private readonly IRuntimeSnapshotProvider _snapshotProvider;

    public RuntimeSnapshotDiagnosticCheck(
        IRuntimeSnapshotProvider snapshotProvider)
    {
        _snapshotProvider = snapshotProvider;
    }

    public string Id => "runtime-snapshot";
    public string Title => "Runtime snapshot";

    public async Task<DiagnosticResult> CheckAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            RuntimeSnapshot snapshot =
                await _snapshotProvider.GetSnapshotAsync(
                    cancellationToken);

            if (snapshot.CapturedAt == default)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "CapturedAt is default.",
                    SuggestedAction:
                        "Ensure the snapshot captures a valid timestamp.");
            }

            if (snapshot.SchemaVersion != 1)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message:
                        $"Unsupported schema version: " +
                        $"{snapshot.SchemaVersion}.",
                    SuggestedAction:
                        "Update the snapshot schema to version 1.");
            }

            if (snapshot.Runtime is null)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "Runtime section is missing.",
                    SuggestedAction:
                        "Ensure the snapshot includes runtime state.");
            }

            if (snapshot.Configuration is null)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "Configuration section is missing.",
                    SuggestedAction:
                        "Ensure the snapshot includes configuration.");
            }

            if (snapshot.InstalledRouteCount < 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "InstalledRouteCount is negative.",
                    SuggestedAction:
                        "Ensure route counts are non-negative.");
            }

            if (snapshot.PrefixCount < 0)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "PrefixCount is negative.",
                    SuggestedAction:
                        "Ensure prefix counts are non-negative.");
            }

            if (snapshot.DnsCache is null)
            {
                return new DiagnosticResult(
                    Id: Id,
                    Title: Title,
                    Status: DiagnosticStatus.Failed,
                    Severity: DiagnosticSeverity.Error,
                    Message: "DnsCache is null.",
                    SuggestedAction:
                        "Ensure the snapshot includes a DNS cache list.");
            }

            if (snapshot.Performance is not null)
            {
                var perf = snapshot.Performance;

                if (perf.TotalMs < 0)
                {
                    return new DiagnosticResult(
                        Id: Id,
                        Title: Title,
                        Status: DiagnosticStatus.Failed,
                        Severity: DiagnosticSeverity.Error,
                        Message: "TotalMs is negative.",
                        SuggestedAction:
                            "Ensure performance durations are non-negative.");
                }

                if (perf.PlannedSteps < 0)
                {
                    return new DiagnosticResult(
                        Id: Id,
                        Title: Title,
                        Status: DiagnosticStatus.Failed,
                        Severity: DiagnosticSeverity.Error,
                        Message: "PlannedSteps is negative.",
                        SuggestedAction:
                            "Ensure performance planned steps is non-negative.");
                }

                if (perf.CompletedSteps < 0)
                {
                    return new DiagnosticResult(
                        Id: Id,
                        Title: Title,
                        Status: DiagnosticStatus.Failed,
                        Severity: DiagnosticSeverity.Error,
                        Message: "CompletedSteps is negative.",
                        SuggestedAction:
                            "Ensure performance completed steps is non-negative.");
                }
            }

            return new DiagnosticResult(
                Id: Id,
                Title: Title,
                Status: DiagnosticStatus.Passed,
                Severity: DiagnosticSeverity.Info,
                Message: "Runtime snapshot is valid.",
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
                    $"Could not obtain runtime snapshot: " +
                    $"{exception.Message}",
                SuggestedAction:
                    "Ensure the runtime snapshot provider is available.");
        }
    }
}