namespace PathVeer.Testing.Performance.Lifecycle;

/// <summary>
/// A per-metric trend line over the resource samples collected for a
/// simulation run. Values are informational.
/// </summary>
public sealed record ResourceTrendLine
{
    public required string Name { get; init; }

    public required double Initial { get; init; }

    public required double Final { get; init; }

    public required double Minimum { get; init; }

    public required double Maximum { get; init; }

    public double AbsoluteDelta => Final - Initial;

    public double? PercentageDelta =>
        Math.Abs(Initial) < double.Epsilon
            ? null
            : ((Final - Initial) / Math.Abs(Initial)) * 100.0;

    public override string ToString()
    {
        string percentage = PercentageDelta is { } value
            ? $", {value:0.0}%"
            : string.Empty;

        return $"{Name}: {Initial:0.##} -> {Final:0.##} " +
               $"(min {Minimum:0.##}, max {Maximum:0.##}, " +
               $"delta {AbsoluteDelta:0.##}{percentage})";
    }
}

/// <summary>
/// Computes per-metric trend lines (initial, final, min, max, deltas)
/// across an ordered set of <see cref="ResourceSample"/> values.
/// Informational only; no thresholds are enforced here.
/// </summary>
public static class ResourceTrendAnalyzer
{
    public static IReadOnlyList<ResourceTrendLine> Analyze(
        IReadOnlyList<ResourceSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count == 0)
        {
            return [];
        }

        ResourceSample initial = samples[0];
        ResourceSample final = samples[^1];

        return
        [
            Build("ManagedMemoryBytes", initial, final, s => s.ManagedMemoryBytes),
            Build("GcGen0", initial, final, s => s.GcGen0),
            Build("GcGen1", initial, final, s => s.GcGen1),
            Build("GcGen2", initial, final, s => s.GcGen2),
            Build("ThreadCount", initial, final, s => s.ThreadCount),
            Build("ActiveExecutionHandlerCalls", initial, final, s => s.ActiveExecutionHandlerCalls),
            Build("TotalExecutionHandlerCalls", initial, final, s => s.TotalExecutionHandlerCalls),
            Build("PeakExecutionHandlerCalls", initial, final, s => s.PeakExecutionHandlerCalls),
            Build("RouteTableEntries", initial, final, s => s.RouteTableEntries),
            Build("RouteInventoryCount", initial, final, s => s.RouteInventoryCount),
            Build("VpnEndpointInventoryCount", initial, final, s => s.VpnEndpointInventoryCount),
            Build("CustomRouteCount", initial, final, s => s.CustomRouteCount),
            Build("DnsCacheRecordCount", initial, final, s => s.DnsCacheRecordCount),
            Build("PrefixCount", initial, final, s => s.PrefixCount),
            Build("PrefixHistoryEntryCount", initial, final, s => s.PrefixHistoryEntryCount),
            Build("PerfReportFileCount", initial, final, s => s.PerfReportFileCount),
            Build("PendingTempFileCount", initial, final, s => s.PendingTempFileCount),
            Build("RouteAddCalls", initial, final, s => s.RouteAddCalls),
            Build("RouteDeleteCalls", initial, final, s => s.RouteDeleteCalls),
            Build("DnsResolutionCount", initial, final, s => s.DnsResolutionCount),
            Build("MonitorConsecutiveFailures", initial, final, s => s.MonitorConsecutiveFailures)
        ];
    }

    private static ResourceTrendLine Build(
        string name,
        ResourceSample initial,
        ResourceSample final,
        Func<ResourceSample, double> selector)
    {
        double first = selector(initial);
        double last = selector(final);

        return new ResourceTrendLine
        {
            Name = name,
            Initial = first,
            Final = last,
            Minimum = Math.Min(first, last),
            Maximum = Math.Max(first, last)
        };
    }
}
