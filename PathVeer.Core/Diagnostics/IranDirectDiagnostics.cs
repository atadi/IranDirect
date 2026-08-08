namespace PathVeer.Core.Diagnostics;

public sealed record IranDirectDiagnostics
{
    public DateTimeOffset GeneratedAt { get; init; }

    public string Version { get; init; } = "";

    public DiagnosticSeverity OverallSeverity { get; init; }

    public IReadOnlyList<DiagnosticCheck> Checks
        { get; init; } = [];
}