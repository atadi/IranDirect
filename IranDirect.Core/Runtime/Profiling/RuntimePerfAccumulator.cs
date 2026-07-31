namespace IranDirect.Core.Runtime.Profiling;

/// <summary>
/// Thread-safe per-cycle sample collector. Samples are retained so
/// percentile statistics can be computed at the end of the cycle.
/// Overhead per sample is a single lock and a list append.
/// </summary>
public sealed class RuntimePerfAccumulator
{
    private readonly object _lock = new();

    private readonly Dictionary<RuntimePerfCategory, List<double>>
        _samples = new();

    public void Add(
        RuntimePerfCategory category,
        TimeSpan elapsed)
    {
        lock (_lock)
        {
            if (!_samples.TryGetValue(
                    category, out List<double>? list))
            {
                list = [];
                _samples[category] = list;
            }

            list.Add(elapsed.TotalMilliseconds);
        }
    }

    public IReadOnlyList<RuntimePerfCategorySummary> Snapshot()
    {
        lock (_lock)
        {
            return _samples
                .Select(pair => BuildSummary(pair.Key, pair.Value))
                .OrderBy(summary => (int)summary.Category)
                .ToArray();
        }
    }

    private static RuntimePerfCategorySummary BuildSummary(
        RuntimePerfCategory category,
        List<double> samples)
    {
        double[] sorted = [.. samples.OrderBy(value => value)];

        double total = 0;

        foreach (double value in sorted)
            total += value;

        double p95 = sorted[
            (int)Math.Ceiling(sorted.Length * 0.95) - 1];

        return new RuntimePerfCategorySummary(
            category,
            sorted.Length,
            total,
            total / sorted.Length,
            sorted[0],
            sorted[^1],
            p95);
    }
}
