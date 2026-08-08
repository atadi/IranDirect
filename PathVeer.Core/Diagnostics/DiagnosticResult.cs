namespace PathVeer.Core.Diagnostics;

public sealed record DiagnosticResult(
    string Id,
    string Title,
    DiagnosticStatus Status,
    DiagnosticSeverity Severity,
    string Message,
    string? SuggestedAction);