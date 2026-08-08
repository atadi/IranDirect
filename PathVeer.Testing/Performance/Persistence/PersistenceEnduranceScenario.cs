namespace PathVeer.Testing.Performance.Persistence;

public enum PersistenceEnduranceScenario
{
    RepeatedSequentialReadWrite,
    ConcurrentReadersWriters,
    InjectedFailureRecovery,
    StaleTempRecovery,
    Cancellation,
    CrossStoreIsolation,
    CorruptFileRecoveryContract,
    HistoryRetention,
    PerformanceReportRetentionListing
}
