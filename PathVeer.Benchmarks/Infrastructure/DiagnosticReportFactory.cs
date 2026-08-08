using PathVeer.Core.Diagnostics;

namespace PathVeer.Benchmarks.Infrastructure;

public static class DiagnosticReportFactory
{
    private static readonly string[] s_knownCheckIds =
    [
        "desired-configuration",
        "prefix-configuration",
        "prefix-metadata",
        "prefix-history",
        "custom-routes",
        "runtime-state",
        "runtime-snapshot",
        "runtime-operation",
        "route-inventory",
        "windows-route-table",
        "route-ownership",
        "managed-route-consistency",
        "vpn-profile",
        "prefix-source",
        "updates"
    ];

    public static DiagnosticReport Create(int resultCount)
    {
        if (resultCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(resultCount));
        }

        var results = new List<DiagnosticResult>(resultCount);

        for (int i = 0; i < resultCount; i++)
        {
            string checkId = s_knownCheckIds[i % s_knownCheckIds.Length];

            DiagnosticStatus status = (i % 20) switch
            {
                0 => DiagnosticStatus.Failed,
                1 => DiagnosticStatus.Warning,
                _ => DiagnosticStatus.Passed
            };

            DiagnosticSeverity severity = status switch
            {
                DiagnosticStatus.Failed => DiagnosticSeverity.Error,
                DiagnosticStatus.Warning => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Pass
            };

            results.Add(
                new DiagnosticResult(
                    Id: $"{checkId}.{i}",
                    Title: checkId,
                    Status: status,
                    Severity: severity,
                    Message: $"Diagnostic result {i} for {checkId}.",
                    SuggestedAction: i % 3 == 0
                        ? $"Recommended action {i}."
                        : null));
        }

        return new DiagnosticReport(
            CapturedAt: FixedTime.Value,
            Results: results);
    }
}
