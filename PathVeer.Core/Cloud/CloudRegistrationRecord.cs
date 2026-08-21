namespace PathVeer.Core.Cloud;

/// <summary>
/// Durable, machine-scoped cloud registration persisted by the Service.
///
/// SAFETY: the raw device credential is NEVER stored in plaintext. It is
/// held only as <see cref="CredentialProtectedBase64"/>, an opaque blob
/// produced by <see cref="ICloudSecretProtector"/> (DPAPI on Windows). The
/// record is written atomically (tmp + move) by the store, so it can never
/// land on disk as "identity present but credential missing".
/// </summary>
public sealed class CloudRegistrationRecord
{
    /// <summary>Local connection state used by the UI and heartbeat loop.</summary>
    public CloudConnectionState State { get; set; } =
        CloudConnectionState.NotConnected;

    /// <summary>Cloud-issued random device identity (UUID).</summary>
    public string? DeviceId { get; set; }

    /// <summary>Organization that issued the enrollment code.</summary>
    public string? OrganizationId { get; set; }

    /// <summary>Operator-supplied label (may be null).</summary>
    public string? DeviceLabel { get; set; }

    /// <summary>UTC time the registration was durably persisted.</summary>
    public DateTimeOffset? EnrolledAtUtc { get; set; }

    /// <summary>
    /// Opaque, DPAPI-protected credential blob (base64). Null when no
    /// credential is held (e.g. revoked or never enrolled).
    /// </summary>
    public string? CredentialProtectedBase64 { get; set; }

    /// <summary>
    /// True once the credential has been confirmed revoked by Cloud. The
    /// record is preserved for operator diagnostics; the credential blob is
    /// cleared so no further heartbeat attempts use it.
    /// </summary>
    public bool CredentialRevoked { get; set; }

    /// <summary>UTC time of the last successful heartbeat, if any.</summary>
    public DateTimeOffset? LastHeartbeatUtc { get; set; }

    /// <summary>
    /// Counts consecutive heartbeat failures. Used to back off and to avoid
    /// hammering Cloud with a known-invalid credential.
    /// </summary>
    public int ConsecutiveHeartbeatFailures { get; set; }

    /// <summary>True once a complete, durably persisted registration exists.</summary>
    public bool IsEnrolled =>
        State is CloudConnectionState.Connected
            or CloudConnectionState.Revoked
            or CloudConnectionState.Degraded
        && !string.IsNullOrEmpty(DeviceId)
        && !string.IsNullOrEmpty(OrganizationId);
}
