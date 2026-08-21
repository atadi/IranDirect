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
        Action<string>? onDirectoryPrepared = null,
        Action<string>? onFilePersisted = null)
        : base(statePath, faultPolicy: faultPolicy,
              onDirectoryPrepared: onDirectoryPrepared,
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
    /// if no credential is held (not enrolled, revoked, failed, or the stored
    /// blob is undecryptable). A blob the LocalSystem Service cannot actually
    /// decrypt (corrupt, wrong DPAPI scope, or an attacker-planted document)
    /// is treated as no credential — validity is tied to what the Service can
    /// use, not to attacker-controlled JSON flags.
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

        try
        {
            return _protector.Unprotect(
                record.CredentialProtectedBase64);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Stored blob is unusable. Do not trust it; report no credential.
            return null;
        }
        catch (FormatException)
        {
            // Malformed protected representation. Same handling.
            return null;
        }
    }

    /// <summary>
    /// Authoritative measure of whether a Cloud registration is usable by the
    /// Service: for a non-revoked record the credential blob must actually be
    /// decryptable by the supplied protector. This is the SINGLE definition
    /// used by both the runtime store (via <see cref="CanUseStoredCredentialAsync"/>)
    /// and the migrator, so attacker-controlled JSON flags
    /// (IsEnrolled / State / DeviceId / OrganizationId / mere presence of a
    /// blob) are never treated as enrollment. A revoked record is explicitly
    /// non-usable (it intentionally carries no credential).
    /// </summary>
    public static bool IsUsableRegistration(
        CloudRegistrationRecord record,
        ICloudSecretProtector protector)
    {
        if (record is null
            || record.CredentialRevoked
            || string.IsNullOrEmpty(record.CredentialProtectedBase64))
        {
            return false;
        }

        try
        {
            _ = protector.Unprotect(record.CredentialProtectedBase64);
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when the Service can actually decrypt and use the stored
    /// credential. This — not the on-disk IsEnrolled/State flags — is the
    /// authoritative measure of whether a Cloud registration is usable, so an
    /// attacker-created "Connected" document is not trusted on its own.
    /// </summary>
    public async Task<bool> CanUseStoredCredentialAsync(
        CancellationToken cancellationToken = default)
    {
        CloudRegistrationRecord record =
            await LoadAsync(cancellationToken);
        return IsUsableRegistration(record, _protector);
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
    /// Returns a safe, credential-free view for the UI. Usability
    /// (HasUsableCredential) is derived from whether the Service can actually
    /// decrypt the stored blob — not from attacker-controlled JSON flags —
    /// so a planted "Connected" document is reflected honestly.
    /// </summary>
    public async Task<CloudRegistrationView> GetViewAsync(
        CancellationToken cancellationToken = default)
    {
        CloudRegistrationRecord record =
            await LoadAsync(cancellationToken);

        bool hasBlob = !string.IsNullOrEmpty(
                record.CredentialProtectedBase64)
            && !record.CredentialRevoked;

        return new CloudRegistrationView
        {
            State = record.State,
            DeviceId = record.DeviceId,
            OrganizationId = record.OrganizationId,
            DeviceLabel = record.DeviceLabel,
            EnrolledAtUtc = record.EnrolledAtUtc,
            LastHeartbeatUtc = record.LastHeartbeatUtc,
            HasCredential = hasBlob,
            HasUsableCredential = hasBlob
                && await CanUseStoredCredentialAsync(cancellationToken)
        };
    }
}
