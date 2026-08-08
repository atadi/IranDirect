namespace PathVeer.Core.Diagnostics;

public sealed record PathVeerDiagnostics
{
    public DateTimeOffset GeneratedAt { get; init; }

    public string Version { get; init; } = "";

    public DiagnosticSeverity OverallSeverity { get; init; }

    public IReadOnlyList<DiagnosticCheck> Checks
        { get; init; } = [];
}