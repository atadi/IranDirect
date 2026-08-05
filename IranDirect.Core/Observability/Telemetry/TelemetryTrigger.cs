namespace IranDirect.Core.Observability.Telemetry;

/// <summary>
/// Bounded trigger for a runtime reconciliation cycle. Mirrors the
/// trigger vocabulary from Phase 32.1/32.2. Mapped from the controller's
/// known trigger strings; <see cref="Unknown"/> when indeterminate.
/// </summary>
public enum TelemetryTrigger
{
    Unknown = 0,
    Scheduled = 1,
    Forced = 2,
    Startup = 3,
    Repair = 4,
}
