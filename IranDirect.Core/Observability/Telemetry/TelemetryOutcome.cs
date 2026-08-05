namespace IranDirect.Core.Observability.Telemetry;

/// <summary>
/// Bounded telemetry outcome. Mirrors the success/failure/cancelled/timeout/
/// no-change vocabulary defined in Phase 32.1. Backed by the existing runtime
/// status enums via <see cref="TelemetryOutcomeMapper"/>.
/// </summary>
public enum TelemetryOutcome
{
    Unknown = 0,
    Success = 1,
    Failure = 2,
    Cancelled = 3,
    Timeout = 4,
    NoChange = 5,
}
