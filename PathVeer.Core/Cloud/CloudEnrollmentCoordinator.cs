using PathVeer.Core.Installation;

namespace PathVeer.Core.Cloud;

/// <summary>
/// Service-side orchestrator for enrolling this machine with PathVeer Cloud.
///
/// This is the authoritative enrollment operation: it validates the operator
/// input, performs the Cloud exchange via <see cref="PathVeerCloudClient"/>,
/// encrypts the credential with the machine-scoped protector, and persists a
/// single coherent registration record. The Tray/UI only forwards the code
/// and label here; it never sees the credential.
///
/// FAIL-CLOSED GUARANTEES:
/// - If local persistence fails AFTER Cloud accepted the code, the enrollment
///   is treated as failed. We do NOT pretend success and we do NOT generate or
///   persist a second identity. The operator must retry (the code is already
///   consumed server-side, so they will need a fresh code — this is a
///   documented, recoverable condition, not silent data loss).
/// - An already-enrolled machine is not silently overwritten. Re-enrollment
///   requires explicit operator intent passed via <paramref name="force"/>.
/// </summary>
public sealed class CloudEnrollmentCoordinator
{
    private readonly PathVeerCloudClient _client;
    private readonly CloudRegistrationStore _store;
    private readonly TimeProvider _timeProvider;

    public CloudEnrollmentCoordinator(
        PathVeerCloudClient client,
        CloudRegistrationStore store,
        TimeProvider? timeProvider = null)
    {
        _client = client;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CloudEnrollmentResult> EnrollAsync(
        string enrollmentCode,
        string? deviceLabel,
        bool force,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(enrollmentCode))
        {
            return CloudEnrollmentResult.Failed(
                "An enrollment code is required.");
        }

        CloudRegistrationRecord existing =
            await _store.LoadRegistrationAsync(cancellationToken);

        bool alreadyEnrolled = existing.IsEnrolled;
        if (alreadyEnrolled && !force)
        {
            // Do not silently overwrite an existing cloud identity.
            return CloudEnrollmentResult.Failed(
                "This installation is already enrolled. Use reset/re-enroll " +
                "to replace the existing registration.");
        }

        EnrollResponse response;
        try
        {
            response = await _client.EnrollAsync(
                new EnrollRequest
                {
                    EnrollmentCode = enrollmentCode,
                    DeviceLabel = deviceLabel
                },
                cancellationToken);
        }
        catch (CloudEnrollmentException ex)
            when (ex.Kind == EnrollmentFailureKind.CodeRejected)
        {
            return CloudEnrollmentResult.Failed(
                "The enrollment code was rejected or already used.");
        }
        catch (CloudEnrollmentException ex)
        {
            return CloudEnrollmentResult.Failed(
                "Could not reach PathVeer Cloud to enroll. " +
                "Check connectivity and try again.");
        }

        // Cloud accepted the code. Persist atomically. If this throws, we
        // propagate and do NOT mark the machine enrolled.
        try
        {
            await _store.SaveEnrolledAsync(
                response.DeviceId,
                response.OrganizationId,
                response.Credential,
                deviceLabel,
                _timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (Exception)
        {
            // The credential must NOT linger in memory longer than needed;
            // it goes out of scope here. We surface a clear recoverable
            // failure and do not generate a second identity.
            return CloudEnrollmentResult.Failed(
                "Enrollment succeeded on the server but could not be saved " +
                "securely on this machine. No local identity was created. " +
                "Contact support if this persists.");
        }

        return CloudEnrollmentResult.Succeeded(
            response.DeviceId,
            response.OrganizationId);
    }
}

public sealed record CloudEnrollmentResult
{
    public bool Success { get; init; }
    public string? DeviceId { get; init; }
    public string? OrganizationId { get; init; }
    public string? Error { get; init; }

    public static CloudEnrollmentResult Succeeded(
        string deviceId,
        string organizationId) =>
        new()
        {
            Success = true,
            DeviceId = deviceId,
            OrganizationId = organizationId
        };

    public static CloudEnrollmentResult Failed(string error) =>
        new() { Success = false, Error = error };
}
