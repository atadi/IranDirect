namespace IranDirect.Benchmarks.Infrastructure;

public static class Sizes
{
    public static readonly int[] DatasetSizes =
        [1_000, 5_000, 10_000, 25_000, 50_000];

    public static readonly int[] PreviewStepCounts =
        [0, 1_000, 5_000, 10_000, 25_000, 50_000];

    public static readonly int[] DiagnosticResultCounts =
        [10, 100, 1_000, 5_000];
}
