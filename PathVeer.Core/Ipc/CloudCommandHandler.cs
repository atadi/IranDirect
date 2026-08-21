using PathVeer.Core.Cloud;

namespace PathVeer.Core.Ipc;

/// <summary>
/// Handles Service-side Cloud commands received over IPC from the Tray/UI.
///
/// The Tray supplies the operator-entered enrollment code + label; this
/// handler performs the authoritative enrollment (network exchange +
/// secure persistence) inside the Service. It never returns the credential.
/// </summary>
public sealed class CloudCommandHandler
{
    private readonly CloudEnrollmentCoordinator _coordinator;
    private readonly CloudRegistrationStore _store;

    public CloudCommandHandler(
        CloudEnrollmentCoordinator coordinator,
        CloudRegistrationStore store)
    {
        _coordinator = coordinator;
        _store = store;
    }

    /// <summary>
    /// Enrolls using the code in <paramref name="enrollmentCode"/> and an
    /// optional label in <paramref name="deviceLabel"/>. <paramref name="force"/>
    /// is required to replace an existing registration.
    /// </summary>
    public async Task<ServiceResponse> EnrollAsync(
        string? enrollmentCode,
        string? deviceLabel,
        bool force,
        CancellationToken cancellationToken = default)
    {
        CloudEnrollmentResult result =
            await _coordinator.EnrollAsync(
                enrollmentCode ?? string.Empty,
                deviceLabel,
                force,
                cancellationToken);

        if (!result.Success)
        {
            return new ServiceResponse
            {
                Success = false,
                ErrorCode = "CLOUD_ENROLL_FAILED",
                Message = result.Error ?? "Cloud enrollment failed."
            };
        }

        CloudRegistrationView view =
            await _store.GetViewAsync(cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message = "Connected to PathVeer Cloud.",
            CloudRegistration = view
        };
    }

    /// <summary>Returns the current local cloud registration view.</summary>
    public async Task<ServiceResponse> StatusAsync(
        CancellationToken cancellationToken = default)
    {
        CloudRegistrationView view =
            await _store.GetViewAsync(cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message = "Cloud registration status retrieved.",
            CloudRegistration = view
        };
    }

    /// <summary>
    /// Conservative reset of the local cloud registration. Requires explicit
    /// operator intent. Preserves no credential; the machine becomes
    /// NotConnected. Does NOT contact Cloud (no device deletion in this phase).
    /// </summary>
    public async Task<ServiceResponse> ResetAsync(
        CancellationToken cancellationToken = default)
    {
        await _store.ClearAsync(cancellationToken);

        CloudRegistrationView view =
            await _store.GetViewAsync(cancellationToken);

        return new ServiceResponse
        {
            Success = true,
            Message = "PathVeer Cloud registration cleared locally. " +
                      "Re-enroll to reconnect.",
            CloudRegistration = view
        };
    }
}
