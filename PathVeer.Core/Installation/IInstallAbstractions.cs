using System.Runtime.Versioning;

namespace PathVeer.Core.Installation;

/// <summary>
/// SCM operations the installer needs beyond start/stop (which
/// <c>IServiceControllerAdapter</c> already covers). Abstracted so the upgrade
/// state machine can be exercised without touching the real service database.
/// </summary>
[SupportedOSPlatform("windows")]
public interface IServiceInstaller
{
    /// <summary>Creates the service, or repoints an existing one at <paramref name="binaryPath"/>.</summary>
    void InstallOrUpdate(
        string serviceName,
        string displayName,
        string binaryPath);

    /// <summary>Current SCM binary path, or null when the service does not exist.</summary>
    string? GetBinaryPath(string serviceName);

    /// <summary>Sets start type to disabled so the service cannot auto-restart.</summary>
    void Disable(string serviceName);

    /// <summary>Deletes the service. Returns false when deletion is refused or pending.</summary>
    bool TryDelete(string serviceName);
}

/// <summary>
/// Filesystem, PATH and startup-entry side effects of installation, behind one
/// seam so the orchestrator can be tested with an in-memory fake.
/// </summary>
public interface IInstallFileSystem
{
    Task<InstallManifest?> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken = default);

    Task WriteManifestAsync(
        string manifestPath,
        InstallManifest manifest,
        CancellationToken cancellationToken = default);

    /// <summary>Verifies package integrity (SHA-256 manifest) before any teardown.</summary>
    Task<bool> VerifyPackageAsync(
        string packageDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Copies the package into the staging directory.</summary>
    Task StageAsync(
        string packageDirectory,
        string stagingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Confirms the staged tree contains the required executables.</summary>
    Task<bool> VerifyStagedLayoutAsync(
        InstallLayout layout,
        CancellationToken cancellationToken = default);

    /// <summary>Promotes staging into the live install root.</summary>
    Task SwapAsync(
        string stagingDirectory,
        string installRoot,
        CancellationToken cancellationToken = default);

    Task DiscardStagingAsync(
        string stagingDirectory,
        CancellationToken cancellationToken = default);

    Task RemoveDirectoryAsync(
        string directory,
        CancellationToken cancellationToken = default);

    /// <summary>Adds the CLI directory to the machine PATH. Idempotent.</summary>
    void EnsurePathEntry(string entry);

    /// <summary>Removes only PathVeer's own PATH entry.</summary>
    void RemovePathEntry(string entry);

    /// <summary>Removes the per-user Tray autorun entry if present.</summary>
    void RemoveTrayStartupEntry();

    /// <summary>Deletes %ProgramData%\PathVeer. Only ever called on explicit purge.</summary>
    Task PurgeStateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Post-start health gate. SCM "Running" alone is not accepted as proof that
/// the upgrade succeeded — the service must actually be serving IPC from the
/// expected state root.
/// </summary>
public interface IInstallReadinessProbe
{
    Task<InstallReadiness> ProbeAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the running service to release its managed routes ahead of
    /// uninstall, so routes are not orphaned with no authority to reconcile.
    /// </summary>
    Task<bool> TryReleaseManagedRoutesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Bounded readiness result. Deliberately carries no host or path detail.</summary>
public sealed record InstallReadiness(
    bool ProcessRunning,
    bool PrimaryPipeAvailable,
    bool StatusCommandSucceeded,
    bool StateRootIsCurrent,
    bool JournalRecoveryHealthy)
{
    public bool IsHealthy =>
        ProcessRunning
        && PrimaryPipeAvailable
        && StatusCommandSucceeded
        && StateRootIsCurrent
        && JournalRecoveryHealthy;

    public string Describe() =>
        $"process={ProcessRunning}, pipe={PrimaryPipeAvailable}, "
        + $"status={StatusCommandSucceeded}, stateRoot={StateRootIsCurrent}, "
        + $"journal={JournalRecoveryHealthy}";

    public static InstallReadiness Healthy { get; } =
        new(true, true, true, true, true);
}
