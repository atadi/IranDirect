namespace IranDirect.Testing.Performance.Persistence;

/// <summary>
/// Informational counters collected while an endurance scenario runs.
/// Elapsed time and the process handle count are diagnostic only and
/// must never be used as assertion thresholds.
/// </summary>
public sealed record PersistenceEnduranceMetrics
{
    public required PersistenceEnduranceScenario Scenario { get; init; }

    public required int CyclesPlanned { get; init; }

    public required int CyclesCompleted { get; init; }

    public required int OperationsSucceeded { get; init; }

    public required int OperationsFailed { get; init; }

    public required int InjectedFaults { get; init; }

    public required int Recoveries { get; init; }

    public required int FilesOnDisk { get; init; }

    public required long TotalBytesOnDisk { get; init; }

    public required int OrphanTempFiles { get; init; }

    public required long ElapsedMilliseconds { get; init; }

    public required int ProcessHandleCount { get; init; }
}
