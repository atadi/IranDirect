using System.IO;
using System.Net.Http;
using System.Text.Json;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Observability.Telemetry;

/// <summary>
/// Maps exception types (and fault-injection points) to a bounded
/// <see cref="TelemetryFailureCategory"/>. Category is never derived from
/// exception message text, and inner-exception graphs are not traversed.
/// </summary>
public static class TelemetryFailureCategoryMapper
{
    public static string ToCategoryString(TelemetryFailureCategory category) =>
        category switch
        {
            TelemetryFailureCategory.Io => PathVeerTagValues.FailureIo,
            TelemetryFailureCategory.Timeout => PathVeerTagValues.FailureTimeout,
            TelemetryFailureCategory.Cancellation =>
                PathVeerTagValues.FailureCancellation,
            TelemetryFailureCategory.Http => PathVeerTagValues.FailureHttp,
            TelemetryFailureCategory.Dns => PathVeerTagValues.FailureDns,
            TelemetryFailureCategory.Routing => PathVeerTagValues.FailureRouting,
            TelemetryFailureCategory.Serialization =>
                PathVeerTagValues.FailureSerialization,
            TelemetryFailureCategory.InvalidResponse =>
                PathVeerTagValues.FailureInvalidResponse,
            _ => PathVeerTagValues.FailureUnknown,
        };

    public static TelemetryFailureCategory Map(Exception exception) =>
        exception switch
        {
            null => TelemetryFailureCategory.Unknown,
            OperationCanceledException => TelemetryFailureCategory.Cancellation,
            TimeoutException => TelemetryFailureCategory.Timeout,
            HttpRequestException => TelemetryFailureCategory.Http,
            IOException => TelemetryFailureCategory.Io,
            UnauthorizedAccessException => TelemetryFailureCategory.Io,
            JsonException => TelemetryFailureCategory.Serialization,
            FaultInjectionException fault => MapFault(fault.Point),
            _ => TelemetryFailureCategory.Unknown,
        };

    /// <summary>
    /// Maps a fault-injection point to a bounded category. The single
    /// <see cref="FaultInjectionPoint.NamedPipeSend"/> case maps to
    /// <see cref="TelemetryFailureCategory.Io"/> (documented choice: the
    /// architecture document's "invalid_response or io" fork is resolved to
    /// io to avoid over-fitting IPC transport failures to a response-shape
    /// category).
    /// </summary>
    public static TelemetryFailureCategory MapFault(FaultInjectionPoint point) =>
        point switch
        {
            FaultInjectionPoint.HttpRequest => TelemetryFailureCategory.Http,
            FaultInjectionPoint.DnsLookup => TelemetryFailureCategory.Dns,
            FaultInjectionPoint.RouteEnumeration =>
                TelemetryFailureCategory.Routing,
            FaultInjectionPoint.RouteCreate => TelemetryFailureCategory.Routing,
            FaultInjectionPoint.RouteDelete => TelemetryFailureCategory.Routing,
            FaultInjectionPoint.FileRead => TelemetryFailureCategory.Io,
            FaultInjectionPoint.FileWrite => TelemetryFailureCategory.Io,
            FaultInjectionPoint.FileMove => TelemetryFailureCategory.Io,
            FaultInjectionPoint.JsonLoad => TelemetryFailureCategory.Serialization,
            FaultInjectionPoint.JsonSave => TelemetryFailureCategory.Serialization,
            FaultInjectionPoint.NamedPipeSend => TelemetryFailureCategory.Io,
            FaultInjectionPoint.SnapshotCapture => TelemetryFailureCategory.Unknown,
            FaultInjectionPoint.DiagnosticsRun => TelemetryFailureCategory.Unknown,
            _ => TelemetryFailureCategory.Unknown,
        };
}
