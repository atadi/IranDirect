using System.ServiceProcess;
using PathVeer.Core.Installation;
using PathVeer.Core.ServiceLifecycle;

namespace PathVeer.Core.Tests.Installation;

/// <summary>
/// In-memory fakes for the Phase 36.7 install abstractions.
///
/// Unit tests must never touch the real SCM, PATH or filesystem, so every
/// side effect the orchestrator performs is recorded here instead. The fakes
/// also record a chronological event log, which is what lets the tests assert
/// ORDERING invariants — e.g. that the legacy service was stopped strictly
/// before any binary was replaced.
/// </summary>
internal sealed class FakeServiceController : IServiceControllerAdapter
{
    private readonly List<string> _log;
    private readonly string _label;

    public FakeServiceController(
        List<string> log,
        string label,
        bool exists,
        ServiceControllerStatus status = ServiceControllerStatus.Stopped)
    {
        _log = log;
        _label = label;
        ServiceExists = exists;
        Status = status;
    }

    public bool ServiceExists { get; set; }

    public ServiceControllerStatus Status { get; set; }

    /// <summary>When true, Stop() leaves the service running (refuses to stop).</summary>
    public bool RefuseToStop { get; set; }

    /// <summary>When true, Start() throws.</summary>
    public bool FailToStart { get; set; }

    /// <summary>When true, the service silently restarts itself after being stopped.</summary>
    public bool RestartsItselfAfterStop { get; set; }

    public bool ExistsThrows { get; set; }

    public bool Exists()
    {
        if (ExistsThrows)
        {
            throw new InvalidOperationException("SCM unavailable.");
        }

        return ServiceExists;
    }

    public ServiceControllerStatus GetStatus() => Status;

    public void Start(TimeSpan timeout)
    {
        _log.Add($"{_label}:start");

        if (FailToStart)
        {
            throw new InvalidOperationException(
                $"{_label} failed to start.");
        }

        Status = ServiceControllerStatus.Running;
        ServiceExists = true;
    }

    public void Stop(TimeSpan timeout)
    {
        _log.Add($"{_label}:stop");

        if (RefuseToStop)
        {
            Status = ServiceControllerStatus.Running;
            return;
        }

        Status = RestartsItselfAfterStop
            ? ServiceControllerStatus.Running
            : ServiceControllerStatus.Stopped;
    }
}

internal sealed class FakeServiceInstaller : IServiceInstaller
{
    private readonly List<string> _log;

    public FakeServiceInstaller(List<string> log) => _log = log;

    public Dictionary<string, string> BinaryPaths { get; } = new(
        StringComparer.OrdinalIgnoreCase);

    public List<string> Disabled { get; } = [];

    public List<string> Deleted { get; } = [];

    public bool DeleteFails { get; set; }

    public int InstallOrUpdateCallCount { get; private set; }

    public void InstallOrUpdate(
        string serviceName,
        string displayName,
        string binaryPath)
    {
        InstallOrUpdateCallCount++;
        BinaryPaths[serviceName] = binaryPath;
        _log.Add($"installer:configure:{serviceName}");
    }

    public string? GetBinaryPath(string serviceName) =>
        BinaryPaths.TryGetValue(serviceName, out string? path)
            ? path
            : null;

    public void Disable(string serviceName)
    {
        Disabled.Add(serviceName);
        _log.Add($"installer:disable:{serviceName}");
    }

    public bool TryDelete(string serviceName)
    {
        _log.Add($"installer:delete:{serviceName}");

        if (DeleteFails)
        {
            return false;
        }

        Deleted.Add(serviceName);
        BinaryPaths.Remove(serviceName);

        return true;
    }
}

internal sealed class FakeInstallFileSystem : IInstallFileSystem
{
    private readonly List<string> _log;

    public FakeInstallFileSystem(List<string> log) => _log = log;

    public InstallManifest? Manifest { get; set; }

    public bool PackageValid { get; set; } = true;

    public bool StagedLayoutValid { get; set; } = true;

    public string PathValue { get; set; } = @"C:\Windows";

    public bool Swapped { get; private set; }

    public bool StagingDiscarded { get; private set; }

    public bool TrayStartupRemoved { get; private set; }

    public bool StatePurged { get; private set; }

    public List<string> RemovedDirectories { get; } = [];

    public Task<InstallManifest?> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Manifest);

    public Task WriteManifestAsync(
        string manifestPath,
        InstallManifest manifest,
        CancellationToken cancellationToken = default)
    {
        Manifest = manifest;
        _log.Add("fs:write-manifest");

        return Task.CompletedTask;
    }

    public Task<bool> VerifyPackageAsync(
        string packageDirectory,
        CancellationToken cancellationToken = default)
    {
        _log.Add("fs:verify-package");

        return Task.FromResult(PackageValid);
    }

    public Task StageAsync(
        string packageDirectory,
        string stagingDirectory,
        CancellationToken cancellationToken = default)
    {
        _log.Add("fs:stage");

        return Task.CompletedTask;
    }

    public Task<bool> VerifyStagedLayoutAsync(
        InstallLayout layout,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StagedLayoutValid);

    public Task SwapAsync(
        string stagingDirectory,
        string installRoot,
        CancellationToken cancellationToken = default)
    {
        Swapped = true;
        _log.Add("fs:swap");

        return Task.CompletedTask;
    }

    public Task DiscardStagingAsync(
        string stagingDirectory,
        CancellationToken cancellationToken = default)
    {
        StagingDiscarded = true;
        _log.Add("fs:discard-staging");

        return Task.CompletedTask;
    }

    public Task RemoveDirectoryAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        RemovedDirectories.Add(directory);

        return Task.CompletedTask;
    }

    public void EnsurePathEntry(string entry)
    {
        PathValue = PathEnvironmentEditor.AddEntry(PathValue, entry);
        _log.Add("fs:ensure-path");
    }

    public void RemovePathEntry(string entry) =>
        PathValue = PathEnvironmentEditor.RemoveEntry(PathValue, entry);

    public void RemoveTrayStartupEntry() => TrayStartupRemoved = true;

    public Task PurgeStateAsync(CancellationToken cancellationToken = default)
    {
        StatePurged = true;
        _log.Add("fs:purge-state");

        return Task.CompletedTask;
    }
}

internal sealed class FakeReadinessProbe : IInstallReadinessProbe
{
    public InstallReadiness Result { get; set; } = InstallReadiness.Healthy;

    public bool RouteReleaseSucceeds { get; set; } = true;

    public bool RouteReleaseAttempted { get; private set; }

    public Task<InstallReadiness> ProbeAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result);

    public Task<bool> TryReleaseManagedRoutesAsync(
        CancellationToken cancellationToken = default)
    {
        RouteReleaseAttempted = true;

        return Task.FromResult(RouteReleaseSucceeds);
    }
}
