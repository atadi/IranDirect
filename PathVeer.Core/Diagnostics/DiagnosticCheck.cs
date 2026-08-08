namespace PathVeer.Core.Diagnostics;

public sealed record DiagnosticCheck
{
    public required string Name { get; init; }

    public required DiagnosticSeverity Severity { get; init; }

    public required string Message { get; init; }
}