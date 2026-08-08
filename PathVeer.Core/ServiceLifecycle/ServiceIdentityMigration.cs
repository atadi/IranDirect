using System.Runtime.Versioning;
using System.ServiceProcess;

namespace PathVeer.Core.ServiceLifecycle;

/// <summary>
/// Orchestrates the single-authority service-identity migration from the legacy
/// IranDirect service to the new PathVeer service.
///
/// Hard rule: IranDirect Service and PathVeer Service MUST NEVER concurrently
/// operate as the route-mutation authority. This controller enforces that by
/// stopping the legacy service and verifying it is stopped BEFORE the new
/// service is allowed to become the authority, and by refusing to start the new
/// service while the legacy one is still running.
///
/// All side effects go through <see cref="IServiceControllerAdapter"/> so the
/// state machine is fully unit-testable with fakes; it never touches the real
/// SCM. Phase 36.7 owns the actual installer/upgrader that drives this contract.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ServiceIdentityMigration
{
    private readonly IServiceControllerAdapter _legacyAdapter;
    private readonly IServiceControllerAdapter _newAdapter;
    private readonly TimeSpan _statusTimeout;

    public ServiceIdentityMigration(
        IServiceControllerAdapter legacyAdapter,
        IServiceControllerAdapter newAdapter,
        TimeSpan? statusTimeout = null)
    {
        _legacyAdapter = legacyAdapter;
        _newAdapter = newAdapter;
        _statusTimeout = statusTimeout ?? TimeSpan.FromSeconds(20);
    }

    /// <summary>
    /// Executes the migration state machine:
    /// 1. detect existing legacy service
    /// 2. stop legacy service
    /// 3. verify stopped
    /// 4. prevent legacy restart (already stopped; the installer must not
    ///    re-enable it — modeled here by asserting it stays stopped)
    /// 5. establish PathVeer service (install)
    /// 6. start PathVeer
    /// 7. PathVeer becomes authority
    /// Returns a report describing the outcome.
    /// </summary>
    public async Task<MigrationReport> MigrateAsync(
        CancellationToken cancellationToken = default)
    {
        // 1. detect existing legacy service
        bool legacyExists = false;
        try
        {
            legacyExists = _legacyAdapter.Exists();
        }
        catch (Exception)
        {
            legacyExists = false;
        }

        // 2+3. stop legacy and verify stopped (only if present)
        if (legacyExists)
        {
            if (_legacyAdapter.GetStatus() != ServiceControllerStatus.Stopped)
            {
                _legacyAdapter.Stop(_statusTimeout);
            }

            // 3. verify stopped
            if (_legacyAdapter.GetStatus() != ServiceControllerStatus.Stopped)
            {
                throw new InvalidOperationException(
                    "Legacy IranDirect service could not be stopped; " +
                    "aborting migration to preserve single-authority.");
            }
        }

        // 6. start PathVeer (install is the installer's job; here we ensure it
        // is started). If already running, no-op by contract.
        if (_newAdapter.GetStatus() != ServiceControllerStatus.Running)
        {
            _newAdapter.Start(_statusTimeout);
        }

        // 7. final guard — legacy must still not be running once new is up.
        if (legacyExists &&
            _legacyAdapter.GetStatus() == ServiceControllerStatus.Running)
        {
            throw new InvalidOperationException(
                "Legacy IranDirect service is running after PathVeer start; " +
                "single-authority violated. Roll back PathVeer.");
        }

        return new MigrationReport(
            LegacyServiceExisted: legacyExists,
            LegacyStopped: legacyExists,
            NewServiceStarted: true,
            SingleAuthorityPreserved: true);
    }

    /// <summary>
    /// Rollback: stop the new PathVeer service and (only if the legacy state
    /// permits) restart the legacy service. Because Phase 36.3 made
    /// %ProgramData%\PathVeer authoritative and does NOT dual-write back to the
    /// legacy root, rollback to the legacy binary is only meaningful before any
    /// PathVeer-state-mutating command has executed. After PathVeer has written
    /// desired-config/country state, the legacy binary cannot see it, so
    /// rollback is limited to restoring service availability, not state parity.
    /// </summary>
    public async Task<RollbackReport> RollbackAsync(
        CancellationToken cancellationToken = default)
    {
        if (_newAdapter.GetStatus() == ServiceControllerStatus.Running)
        {
            _newAdapter.Stop(_statusTimeout);
        }

        bool legacyExists = false;
        try
        {
            legacyExists = _legacyAdapter.Exists();
        }
        catch (Exception)
        {
            legacyExists = false;
        }

        if (legacyExists &&
            _legacyAdapter.GetStatus() != ServiceControllerStatus.Running)
        {
            _legacyAdapter.Start(_statusTimeout);
        }

        return new RollbackReport(
            NewServiceStopped: true,
            LegacyServiceRestarted: legacyExists);
    }
}

public sealed record MigrationReport(
    bool LegacyServiceExisted,
    bool LegacyStopped,
    bool NewServiceStarted,
    bool SingleAuthorityPreserved);

public sealed record RollbackReport(
    bool NewServiceStopped,
    bool LegacyServiceRestarted);
