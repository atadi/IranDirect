namespace IranDirect.Core.Diagnostics;

public sealed record DiagnosticSummary(
    int TotalChecks,
    int PassedCount,
    int WarningCount,
    int FailedCount,
    bool Healthy,
    DiagnosticSeverity HighestSeverity);
