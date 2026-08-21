using PathVeer.Core.Persistence;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Cloud;

/// <summary>
/// Durable, machine-scoped store for the PathVeer Cloud device registration.
///
/// The credential is persisted ONLY as a DPAPI-protected blob
/// (<see cref="CloudRegistrationRecord.CredentialProtectedBase64"/>). The
/// raw credential is never written to disk. Writes are atomic (tmp + move)
/// via <see cref="JsonStore{T}"/>, so the on-disk file can never represent a
/// half-enrolled state (identity present, credential missing).
/// </summary>
public sealed class CloudRegistrationStore :
    JsonStore<CloudRegistrationRecord>
{
    private readonly ICloudSecretProtector _protector;

    public CloudRegistrationStore(
        string statePath,
        ICloudSecretProtector protector,
        IFaultInjectionPolicy? faultPolicy = null,
        Action<string>? onFilePersisted = null)
        : base(statePath, faultPolicy: faultPolicy,
              onFilePersisted: onFilePersisted)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    /// <summary>
    /// Persists a fully-enrolled registration atomically. Encrypts the raw
    /// credential before writing. If encryption or the underlying write fails,
    /// the exception propagates and NOTHING is persisted (fail closed), so the
    /// previously-unenrolled (or prior) state is preserved — no half identity.
    /// </summary>
    public async Task SaveEnrolledAsync(
        string deviceId,
        string organizationId,
        string credential,
        string? deviceLabel,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);

        string protectedBlob = _protector.Protect(credential);

        await SaveAsync(
            new CloudRegistrationRecord
            {
                State = CloudConnectionState.Connected,
                DeviceId = deviceId,
                OrganizationId = organizationId,
                DeviceLabel = deviceLabel,
                EnrolledAtUtc = now,
                CredentialProtectedBase64 = protectedBlob,
                CredentialRevoked = false,
                LastHeartbeatUtc = null,
                ConsecutiveHeartbeatFailures = 0
            },
            cancellationToken);
    }

    /// <summary>
    /// Loads the current registration (or an empty, unenrolled record).
    /// </summary>
    public async Task<CloudRegistrationRecord> LoadRegistrationAsync(
        CancellationToken cancellationToken = default)
    {
        return await LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Recovers the raw credential for use by the heartbeat loop. Returns null
    /// if no credential is held (not enrolled, revoked, or failed).
    /// </summary>
    public async Task<string?> GetCredentialAsync(
        CancellationToken cancellationToken = default)
    {
        CloudRegistrationRecord record =
            await LoadAsync(cancellationToken);

        if (string.IsNullOrEmpty(
                record.CredentialProtectedBase64)
            || record.CredentialRevoked)
        {
            return null;
        }

        return _protector.Unprotect(
            record.CredentialProtectedBase64);
    }

    /// <summary>
    /// Marks the credential revoked locally: clears the protected blob so no
    /// further heartbeat uses it, and sets state to Revoked. Preserves the
    /// device identity for operator diagnostics.
    /// </summary>
    public async Task MarkRevokedAsync(
        CancellationToken cancellationToken = default)
    {
        await MutateAsync(record =>
        {
            record.CredentialRevoked = true;
            record.CredentialProtectedBase64 = null;
            record.State = CloudConnectionState.Revoked;
            record.ConsecutiveHeartbeatFailures = 0;
            return Task.FromResult(record);
        }, cancellationToken);
    }

    /// <summary>
    /// Records a successful heartbeat timestamp atomically.
    /// </summary>
    public async Task RecordHeartbeatSuccessAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await MutateAsync(record =>
        {
            record.LastHeartbeatUtc = now;
            record.ConsecutiveHeartbeatFailures = 0;
            if (record.State == CloudConnectionState.Degraded
                || record.State == CloudConnectionState.NotConnected)
            {
                record.State = CloudConnectionState.Connected;
            }
            return Task.FromResult(record);
        }, cancellationToken);
    }

    /// <summary>
    /// Records a transient heartbeat failure (no credential change).
    /// </summary>
    public async Task RecordHeartbeatFailureAsync(
        CancellationToken cancellationToken = default)
    {
        await MutateAsync(record =>
        {
            record.ConsecutiveHeartbeatFailures++;
            if (record.State == CloudConnectionState.Connected)
            {
                record.State = CloudConnectionState.Degraded;
            }
            return Task.FromResult(record);
        }, cancellationToken);
    }

    /// <summary>
    /// Replaces the existing registration with nothing (clear). Conservative
    /// reset used only on explicit operator intent. Preserves no credential.
    /// </summary>
    public async Task ClearAsync(
        CancellationToken cancellationToken = default)
    {
        await SaveAsync(
            new CloudRegistrationRecord(),
            cancellationToken);
    }

    /// <summary>
    /// Returns a safe, credential-free view for the UI.
    /// </summary>
    public async Task<CloudRegistrationView> GetViewAsync(
        CancellationToken cancellationToken = default)
    {
        CloudRegistrationRecord record =
            await LoadAsync(cancellationToken);

        return new CloudRegistrationView
        {
            State = record.State,
            DeviceId = record.DeviceId,
            OrganizationId = record.OrganizationId,
            DeviceLabel = record.DeviceLabel,
            EnrolledAtUtc = record.EnrolledAtUtc,
            LastHeartbeatUtc = record.LastHeartbeatUtc,
            HasCredential = !string.IsNullOrEmpty(
                record.CredentialProtectedBase64)
                && !record.CredentialRevoked
        };
    }
}
