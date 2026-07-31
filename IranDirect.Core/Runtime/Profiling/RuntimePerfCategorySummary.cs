namespace IranDirect.Core.Runtime.Profiling;

/// <summary>
/// Aggregated statistics for one measured category within a single
/// runtime cycle.
/// </summary>
public sealed record RuntimePerfCategorySummary(
    RuntimePerfCategory Category,
    int Count,
    double TotalMs,
    double AverageMs,
    double MinMs,
    double MaxMs,
    double P95Ms);
