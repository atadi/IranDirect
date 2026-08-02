using IranDirect.Core.Diagnostics;
using IranDirect.Core.Runtime.Profiling;

namespace IranDirect.Core.Support;

public sealed record SupportSnapshotSummary
{
    public required int DiagnosticPassedCount { get; init; }

    public required int DiagnosticWarningCount { get; init; }

    public required int DiagnosticFailedCount { get; init; }

    public required int PrefixCount { get; init; }

    public required int DnsDomainCount { get; init; }

    public required bool ExecutionPreviewHasChanges { get; init; }

    public required bool RuntimeAvailable { get; init; }

    public required bool PerformanceAvailable { get; init; }

    public bool Healthy =>
        DiagnosticFailedCount == 0
        && DiagnosticWarningCount == 0
        && RuntimeAvailable
        && !ExecutionPreviewHasChanges;

    public bool HasErrors =>
        DiagnosticFailedCount > 0;

    public bool HasWarnings =>
        DiagnosticWarningCount > 0
        || ExecutionPreviewHasChanges;
}
