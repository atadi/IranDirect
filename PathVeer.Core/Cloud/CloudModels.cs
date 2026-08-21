namespace PathVeer.Core.Cloud;

/// <summary>
/// Local connection state of this PathVeer installation relative to
/// PathVeer Cloud. This is purely local status; it never drives any
/// networking or routing decision.
/// </summary>
public enum CloudConnectionState
{
    /// <summary>No cloud registration exists on this machine.</summary>
    NotConnected,

    /// <summary>
    /// Enrolled and the last heartbeat succeeded (or none attempted yet).
    /// </summary>
    Connected,

    /// <summary>
    /// The device credential was rejected by Cloud as revoked. Local
    /// functionality is preserved; re-enrollment is required.
    /// </summary>
    Revoked,

    /// <summary>
    /// Enrollment was attempted but the local secure persistence failed,
    /// so the returned identity/credential was discarded. A recoverable
    /// local failure, not a cloud identity.
    /// </summary>
    EnrollmentFailed,

    /// <summary>
    /// The Service believes it is enrolled but the last heartbeat could
    /// not reach Cloud (transient). Local functionality is preserved.
    /// </summary>
    Degraded
}

/// <summary>
/// Wire request for POST /api/v1/devices/enroll.
/// Matches the certified PathVeer Cloud staging contract exactly.
/// </summary>
public sealed record EnrollRequest
{
    /// <summary>One-time enrollment secret supplied by the operator.</summary>
    public required string EnrollmentCode { get; init; }

    /// <summary>
    /// Optional human-entered label. The operator types this; it is never
    /// derived from the Windows hostname or any machine identifier.
    /// </summary>
    public string? DeviceLabel { get; init; }
}

/// <summary>
/// Wire response for a successful POST /api/v1/devices/enroll (HTTP 201).
/// </summary>
public sealed record EnrollResponse
{
    public required string DeviceId { get; init; }
    public required string OrganizationId { get; init; }

    /// <summary>
    /// The raw device credential. Returned exactly once by Cloud and only
    /// in this response body. It is a SECRET and must be encrypted at rest
    /// immediately; it must never be logged or displayed.
    /// </summary>
    public required string Credential { get; init; }
}

/// <summary>
/// Wire request for POST /api/v1/devices/heartbeat.
/// Only appVersion + protocolVersion are ever sent. No machine-identifying
/// telemetry.
/// </summary>
public sealed record HeartbeatRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("appVersion")]
    public required string AppVersion { get; init; }

    /// <summary>Wire protocol version. Fixed at "1" for this phase.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("protocolVersion")]
    public required string ProtocolVersion { get; init; }
}

/// <summary>
/// Wire response for a successful POST /api/v1/devices/heartbeat (HTTP 200).
/// </summary>
public sealed record HeartbeatResponse
{
    public required string DeviceId { get; init; }
    public required string OrganizationId { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset ServerTime { get; init; }
}

/// <summary>
/// Read-only view of the local cloud registration returned to the Tray/UI.
/// It deliberately omits the credential so UI code cannot leak it.
/// </summary>
public sealed record CloudRegistrationView
{
    public CloudConnectionState State { get; init; }
    public string? DeviceId { get; init; }
    public string? OrganizationId { get; init; }
    public string? DeviceLabel { get; init; }
    public DateTimeOffset? EnrolledAtUtc { get; init; }
    public DateTimeOffset? LastHeartbeatUtc { get; init; }
    public bool HasCredential { get; init; }
}
