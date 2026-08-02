using System.Collections.ObjectModel;

namespace IranDirect.Core.Diagnostics;

public sealed record DiagnosticReport(
    DateTimeOffset CapturedAt,
    IReadOnlyList<DiagnosticResult> Results)
{
    public int PassedCount =>
        Results.Count(r => r.Status == DiagnosticStatus.Passed);

    public int WarningCount =>
        Results.Count(r => r.Status == DiagnosticStatus.Warning);

    public int FailedCount =>
        Results.Count(r => r.Status == DiagnosticStatus.Failed);

    public bool Healthy =>
        FailedCount == 0 && WarningCount == 0;
}