using System.Runtime.Versioning;
using System.ServiceProcess;
using PathVeer.Core.ServiceLifecycle;

namespace PathVeer.Core.Installation;

/// <summary>
/// The full install/upgrade orchestration for PathVeer, expressed as a
/// deterministic state machine over injected abstractions.
///
/// This extends <see cref="ServiceIdentityMigration"/> (Phase 36.4, which owned
/// only stop-legacy → start-new) into the complete Phase 36.7 contract:
/// legacy detection, teardown, binary staging/swap, service (re)configuration,
/// readiness validation, legacy removal and manifest publication.
///
/// The controlling invariant, enforced at every step and re-checked after the
/// new service starts:
///
/// <b>The legacy IranDirect service and the PathVeer service must never both be
/// running.</b> Any failure path must leave at most one authority active — the
/// machine is allowed to end up with nothing running, never with two.
///
/// Every side effect is routed through an interface so the whole flow is unit
/// testable with fakes and never touches the real SCM, filesystem or PATH.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class InstallOrchestrator
{
    private readonly IServiceControllerAdapter _legacyService;
    private readonly IServiceControllerAdapter _pathVeerService;
    private readonly IServiceInstaller _serviceInstaller;
    private readonly IInstallFileSystem _fileSystem;
    private readonly IInstallReadinessProbe _readinessProbe;
    private readonly InstallLayout _layout;
    private readonly TimeSpan _statusTimeout;
    private readonly TimeProvider _timeProvider;

    public InstallOrchestrator(
        IServiceControllerAdapter legacyService,
        IServiceControllerAdapter pathVeerService,
        IServiceInstaller serviceInstaller,
        IInstallFileSystem fileSystem,
        IInstallReadinessProbe readinessProbe,
        InstallLayout layout,
        TimeSpan? statusTimeout = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(legacyService);
        ArgumentNullException.ThrowIfNull(pathVeerService);
        ArgumentNullException.ThrowIfNull(serviceInstaller);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(readinessProbe);
        ArgumentNullException.ThrowIfNull(layout);

        _legacyService = legacyService;
        _pathVeerService = pathVeerService;
        _serviceInstaller = serviceInstaller;
        _fileSystem = fileSystem;
        _readinessProbe = readinessProbe;
        _layout = layout;
        _statusTimeout = statusTimeout ?? TimeSpan.FromSeconds(30);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Runs the install/upgrade. Idempotent: running it repeatedly against an
    /// already-current installation converges without duplicating the service,
    /// the PATH entry or the installed files.
    /// </summary>
    /// <param name="request">Package source and version being installed.</param>
    public async Task<InstallReport> InstallAsync(
        InstallRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<string> steps = [];

        // ---- 1. inspect the existing installation ------------------------
        InstallManifest? existing =
            await _fileSystem.ReadManifestAsync(
                _layout.ManifestPath,
                cancellationToken);

        string? previousVersion =
            existing is null || existing.IsEmpty
                ? null
                : existing.ProductVersion;

        steps.Add(previousVersion is null
            ? "detected: fresh install"
            : $"detected: existing install {previousVersion}");

        // ---- 2. verify the package before touching anything --------------
        // Integrity is checked up front so a corrupt package can never reach
        // the point where the live installation has already been torn down.
        if (!await _fileSystem.VerifyPackageAsync(
                request.PackageDirectory,
                cancellationToken))
        {
            throw new InstallFailedException(
                "Package integrity verification failed for "
                + $"'{request.PackageDirectory}'. Installation aborted before "
                + "any change was made to the existing installation.");
        }

        steps.Add("package integrity verified");

        // ---- 3. stop the legacy authority --------------------------------
        bool legacyExisted = SafeExists(_legacyService);

        if (legacyExisted)
        {
            StopAndVerify(
                _legacyService,
                "legacy IranDirect");

            steps.Add("legacy IranDirect service stopped and verified");

            // Prevent an automatic restart racing the rest of the upgrade.
            // Disabling is reversible; deletion happens only after PathVeer is
            // proven healthy, so a mid-upgrade abort can still be rolled back.
            _serviceInstaller.Disable(LegacyServiceNames.ServiceName);

            steps.Add("legacy service start type set to disabled");
        }
        else
        {
            steps.Add("no legacy IranDirect service present");
        }

        // ---- 4. stop the current PathVeer service ------------------------
        // Binaries cannot be replaced underneath a running process.
        if (SafeExists(_pathVeerService))
        {
            StopAndVerify(
                _pathVeerService,
                "PathVeer");

            steps.Add("existing PathVeer service stopped");
        }

        // ---- 5. stage, verify, swap --------------------------------------
        // Staging to a sibling directory and swapping keeps the live install
        // directory from ever being observed half-updated.
        await _fileSystem.StageAsync(
            request.PackageDirectory,
            _layout.StagingDirectory,
            cancellationToken);

        steps.Add($"package staged to {_layout.StagingDirectory}");

        if (!await _fileSystem.VerifyStagedLayoutAsync(
                _layout,
                cancellationToken))
        {
            await _fileSystem.DiscardStagingAsync(
                _layout.StagingDirectory,
                cancellationToken);

            throw new InstallFailedException(
                "Staged payload is missing required PathVeer executables. "
                + "Staging discarded; the existing installation is untouched.");
        }

        steps.Add("staged layout verified");

        await _fileSystem.SwapAsync(
            _layout.StagingDirectory,
            _layout.InstallRoot,
            cancellationToken);

        steps.Add($"binaries swapped into {_layout.InstallRoot}");

        // ---- 6. install or repoint the service ---------------------------
        // A pre-existing PathVeer service may still point at a developer
        // checkout from an earlier phase; repointing is mandatory, not optional.
        string? currentBinaryPath =
            _serviceInstaller.GetBinaryPath(
                PathVeerServiceNames.ServiceName);

        bool needsRepoint =
            currentBinaryPath is null
            || !PathsEquivalent(
                currentBinaryPath,
                _layout.ServiceExecutablePath);

        if (needsRepoint)
        {
            _serviceInstaller.InstallOrUpdate(
                PathVeerServiceNames.ServiceName,
                PathVeerServiceNames.DisplayName,
                _layout.ServiceExecutablePath);

            steps.Add(currentBinaryPath is null
                ? "PathVeer service created"
                : $"PathVeer service repointed from '{currentBinaryPath}'");
        }
        else
        {
            steps.Add("PathVeer service binary path already canonical");
        }

        // ---- 7. PATH entry (idempotent) ----------------------------------
        _fileSystem.EnsurePathEntry(_layout.PathEnvironmentEntry);

        steps.Add("machine PATH entry ensured (idempotent)");

        // ---- 8. start and prove readiness --------------------------------
        _pathVeerService.Start(_statusTimeout);

        if (_pathVeerService.GetStatus() != ServiceControllerStatus.Running)
        {
            throw new InstallFailedException(
                "PathVeer service did not reach Running. The legacy service "
                + "remains stopped and disabled; no second authority is "
                + "active. Roll back or retry.");
        }

        steps.Add("PathVeer service started");

        InstallReadiness readiness =
            await _readinessProbe.ProbeAsync(cancellationToken);

        if (!readiness.IsHealthy)
        {
            // Running-but-unhealthy is a failed upgrade. Stop the new service
            // so the machine is left with zero authorities rather than one
            // broken one, and leave the legacy service intact for rollback.
            StopQuietly(_pathVeerService);

            throw new InstallFailedException(
                "PathVeer started but failed readiness validation: "
                + readiness.Describe()
                + ". PathVeer was stopped; the legacy service was not removed "
                + "so rollback remains possible.");
        }

        steps.Add("readiness validated: " + readiness.Describe());

        // ---- 9. final single-authority guard -----------------------------
        if (legacyExisted
            && _legacyService.GetStatus() == ServiceControllerStatus.Running)
        {
            StopQuietly(_pathVeerService);

            throw new InstallFailedException(
                "Legacy IranDirect service is running after PathVeer became "
                + "healthy — single authority violated. PathVeer stopped.");
        }

        // ---- 10. retire the legacy service -------------------------------
        // Only now, with PathVeer proven healthy, is legacy removal safe.
        // %ProgramData%\IranDirect is deliberately left in place (Phase 36.3).
        bool legacyRemoved = false;

        if (legacyExisted)
        {
            legacyRemoved =
                _serviceInstaller.TryDelete(
                    LegacyServiceNames.ServiceName);

            steps.Add(legacyRemoved
                ? "legacy IranDirect service deleted"
                : "legacy IranDirect service could not be deleted; left "
                  + "stopped and disabled (harmless, retry on next upgrade)");
        }

        // ---- 11. publish the manifest ------------------------------------
        InstallManifest manifest = new()
        {
            SchemaVersion = InstallManifest.CurrentSchemaVersion,
            ProductVersion = request.ProductVersion,
            InstallRoot = _layout.InstallRoot,
            ServiceExecutablePath = _layout.ServiceExecutablePath,
            CliExecutablePath = _layout.CliExecutablePath,
            TrayExecutablePath = _layout.TrayExecutablePath,
            InstalledAtUtc = _timeProvider.GetUtcNow(),
            LegacyServiceMigrationCompleted = legacyExisted,
            UpgradedFromVersion = previousVersion
        };

        await _fileSystem.WriteManifestAsync(
            _layout.ManifestPath,
            manifest,
            cancellationToken);

        steps.Add("install manifest written");

        return new InstallReport(
            WasFreshInstall: previousVersion is null,
            PreviousVersion: previousVersion,
            InstalledVersion: request.ProductVersion,
            LegacyServiceExisted: legacyExisted,
            LegacyServiceRemoved: legacyRemoved,
            ServiceBinaryPath: _layout.ServiceExecutablePath,
            SingleAuthorityPreserved: true,
            Steps: steps);
    }

    /// <summary>
    /// Uninstall. Removes the service, the installed binaries, the PATH entry
    /// and the Tray startup entry.
    ///
    /// Persistent state is NOT removed unless <paramref name="purgeState"/> is
    /// explicitly set: <c>%ProgramData%\PathVeer</c> holds route ownership,
    /// the mutation journal and configuration, and silently destroying it would
    /// orphan managed routes that are still applied to the machine.
    /// <c>%ProgramData%\IranDirect</c> is never touched here at all.
    /// </summary>
    public async Task<UninstallReport> UninstallAsync(
        bool purgeState = false,
        CancellationToken cancellationToken = default)
    {
        List<string> steps = [];

        // Ask the running service to release its managed routes before it
        // disappears. Without this, uninstalling leaves PathVeer-owned routes
        // applied with no authority left to reconcile them.
        bool routesReleased = false;

        if (SafeExists(_pathVeerService)
            && _pathVeerService.GetStatus() == ServiceControllerStatus.Running)
        {
            routesReleased =
                await _readinessProbe.TryReleaseManagedRoutesAsync(
                    cancellationToken);

            steps.Add(routesReleased
                ? "managed routes released via service command"
                : "route release command failed; routes remain applied and "
                  + "state is retained so a reinstall can reconcile them");
        }
        else
        {
            steps.Add("service not running; no route release attempted");
        }

        if (SafeExists(_pathVeerService))
        {
            StopQuietly(_pathVeerService);
            _serviceInstaller.TryDelete(PathVeerServiceNames.ServiceName);

            steps.Add("PathVeer service stopped and removed");
        }

        _fileSystem.RemovePathEntry(_layout.PathEnvironmentEntry);
        steps.Add("machine PATH entry removed");

        _fileSystem.RemoveTrayStartupEntry();
        steps.Add("Tray startup entry removed");

        await _fileSystem.RemoveDirectoryAsync(
            _layout.InstallRoot,
            cancellationToken);

        steps.Add($"installed binaries removed from {_layout.InstallRoot}");

        bool stateRemoved = false;

        if (purgeState)
        {
            await _fileSystem.PurgeStateAsync(cancellationToken);
            stateRemoved = true;

            steps.Add("EXPLICIT PURGE: %ProgramData%\\PathVeer deleted");
        }
        else
        {
            steps.Add(
                "persistent state retained at %ProgramData%\\PathVeer "
                + "(use an explicit purge to delete it)");
        }

        return new UninstallReport(
            ServiceRemoved: true,
            ManagedRoutesReleased: routesReleased,
            StateRemoved: stateRemoved,
            Steps: steps);
    }

    private void StopAndVerify(
        IServiceControllerAdapter service,
        string label)
    {
        if (service.GetStatus() != ServiceControllerStatus.Stopped)
        {
            service.Stop(_statusTimeout);
        }

        if (service.GetStatus() != ServiceControllerStatus.Stopped)
        {
            throw new InstallFailedException(
                $"The {label} service could not be stopped. Aborting before "
                + "any binary is replaced so single authority is preserved.");
        }
    }

    private void StopQuietly(IServiceControllerAdapter service)
    {
        try
        {
            if (service.GetStatus() != ServiceControllerStatus.Stopped)
            {
                service.Stop(_statusTimeout);
            }
        }
        catch (Exception)
        {
            // Best effort on an already-failing path; the caller is throwing.
        }
    }

    private static bool SafeExists(IServiceControllerAdapter service)
    {
        try
        {
            return service.Exists();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool PathsEquivalent(string left, string right)
    {
        static string Normalize(string value) =>
            Path.GetFullPath(value.Trim().Trim('"'));

        try
        {
            return string.Equals(
                Normalize(left),
                Normalize(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Input to an install/upgrade run.</summary>
public sealed record InstallRequest(
    string PackageDirectory,
    string ProductVersion);

public sealed record InstallReport(
    bool WasFreshInstall,
    string? PreviousVersion,
    string InstalledVersion,
    bool LegacyServiceExisted,
    bool LegacyServiceRemoved,
    string ServiceBinaryPath,
    bool SingleAuthorityPreserved,
    IReadOnlyList<string> Steps);

public sealed record UninstallReport(
    bool ServiceRemoved,
    bool ManagedRoutesReleased,
    bool StateRemoved,
    IReadOnlyList<string> Steps);

/// <summary>Raised when an install/upgrade aborts. Never leaves two authorities.</summary>
public sealed class InstallFailedException : Exception
{
    public InstallFailedException(string message)
        : base(message)
    {
    }
}
