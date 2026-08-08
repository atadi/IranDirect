namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Bounded telemetry failure category. Never derived from exception text.
/// </summary>
public enum TelemetryFailureCategory
{
    Unknown = 0,
    Io = 1,
    Timeout = 2,
    Cancellation = 3,
    Http = 4,
    Dns = 5,
    Routing = 6,
    Serialization = 7,
    InvalidResponse = 8,
}
