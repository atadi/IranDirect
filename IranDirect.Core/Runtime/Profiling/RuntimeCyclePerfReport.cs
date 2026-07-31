namespace IranDirect.Core.Runtime.Profiling;

/// <summary>
/// Final outcome of a profiled runtime cycle as persisted in a
/// <see cref="RuntimeCyclePerfReport"/>.
/// </summary>
public enum CycleCompletionStatus
{
    Completed,
    PartiallyCompleted,
    Failed,
    Cancelled
}

/// <summary>
/// Persistent performance report for one completed runtime cycle.
/// Written as a timestamped JSON file after every
/// enable/disable/repair cycle. Reports are produced even when the
/// underlying cycle fails or is partially completed.
/// </summary>
public sealed record RuntimeCyclePerfReport
{
    public required string Trigger { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public required double TotalMs { get; init; }

    public required IReadOnlyList<RuntimePerfCategorySummary>
        Categories { get; init; }

    public CycleCompletionStatus CompletionStatus { get; init; } =
        CycleCompletionStatus.Completed;

    public string? ErrorSummary { get; init; }

    public int PlannedSteps { get; init; }

    public int CompletedSteps { get; init; }
}
