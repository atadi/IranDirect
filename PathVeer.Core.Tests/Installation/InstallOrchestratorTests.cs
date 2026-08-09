using System.ServiceProcess;
using PathVeer.Core.Installation;
using PathVeer.Core.ServiceLifecycle;

namespace PathVeer.Core.Tests.Installation;

/// <summary>
/// Phase 36.7 — install/upgrade orchestration.
///
/// The load-bearing property under test is SINGLE AUTHORITY: the legacy
/// IranDirect service and the PathVeer service must never both be running.
/// Every failure path is asserted to leave at most one authority active — the
/// machine may end up with nothing running, never with two.
/// </summary>
public sealed class InstallOrchestratorTests
{
    private const string InstallRoot = @"C:\Program Files\PathVeer";
    private const string PackageDirectory = @"D:\packages\PathVeer-1.0.0";

    private sealed class Harness
    {
        public List<string> Log { get; } = [];

        public FakeServiceController Legacy { get; }

        public FakeServiceController PathVeer { get; }

        public FakeServiceInstaller Installer { get; }

        public FakeInstallFileSystem FileSystem { get; }

        public FakeReadinessProbe Readiness { get; } = new();

        public InstallLayout Layout { get; } = new(InstallRoot);

        public Harness(
            bool legacyExists,
            ServiceControllerStatus legacyStatus =
                ServiceControllerStatus.Running,
            bool pathVeerExists = false)
        {
            Legacy = new FakeServiceController(
                Log, "legacy", legacyExists, legacyStatus);

            PathVeer = new FakeServiceController(
                Log, "pathveer", pathVeerExists,
                ServiceControllerStatus.Stopped);

            Installer = new FakeServiceInstaller(Log);
            FileSystem = new FakeInstallFileSystem(Log);
        }

        public InstallOrchestrator Build() =>
            new(Legacy,
                PathVeer,
                Installer,
                FileSystem,
                Readiness,
                Layout,
                TimeSpan.FromSeconds(1));

        public Task<InstallReport> InstallAsync(string version = "1.0.0") =>
            Build().InstallAsync(
                new InstallRequest(PackageDirectory, version));
    }

    // ---------------- fresh install ----------------

    [Fact]
    public async Task FreshInstallCreatesTheServiceAtTheCanonicalPath()
    {
        Harness harness = new(legacyExists: false);

        InstallReport report = await harness.InstallAsync();

        Assert.True(report.WasFreshInstall);
        Assert.Null(report.PreviousVersion);
        Assert.False(report.LegacyServiceExisted);

        Assert.Equal(
            harness.Layout.ServiceExecutablePath,
            report.ServiceBinaryPath);

        Assert.Equal(
            harness.Layout.ServiceExecutablePath,
            harness.Installer.GetBinaryPath(
                PathVeerServiceNames.ServiceName));
    }

    [Fact]
    public async Task FreshInstallNeverCreatesAnIranDirectService()
    {
        Harness harness = new(legacyExists: false);

        await harness.InstallAsync();

        Assert.DoesNotContain(
            LegacyServiceNames.ServiceName,
            harness.Installer.BinaryPaths.Keys);
    }

    [Fact]
    public async Task InstalledServicePathIsNeverADeveloperCheckout()
    {
        Harness harness = new(legacyExists: false);

        InstallReport report = await harness.InstallAsync();

        Assert.DoesNotContain(
            "codespace",
            report.ServiceBinaryPath,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "artifacts",
            report.ServiceBinaryPath,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(harness.Layout.ContainsPath(report.ServiceBinaryPath));
    }

    [Fact]
    public async Task FreshInstallWritesTheManifest()
    {
        Harness harness = new(legacyExists: false);

        await harness.InstallAsync("1.2.3");

        InstallManifest manifest = harness.FileSystem.Manifest!;

        Assert.Equal("1.2.3", manifest.ProductVersion);
        Assert.Equal(InstallRoot, manifest.InstallRoot);
        Assert.Null(manifest.UpgradedFromVersion);
        Assert.Equal(
            InstallManifest.CurrentSchemaVersion,
            manifest.SchemaVersion);
    }

    // ---------------- legacy upgrade ----------------

    [Fact]
    public async Task LegacyUpgradeStopsDisablesAndRemovesTheOldService()
    {
        Harness harness = new(legacyExists: true);

        InstallReport report = await harness.InstallAsync();

        Assert.True(report.LegacyServiceExisted);
        Assert.True(report.LegacyServiceRemoved);
        Assert.True(report.SingleAuthorityPreserved);

        Assert.Contains(
            LegacyServiceNames.ServiceName,
            harness.Installer.Disabled);

        Assert.Contains(
            LegacyServiceNames.ServiceName,
            harness.Installer.Deleted);
    }

    [Fact]
    public async Task LegacyIsStoppedBeforeAnyBinaryIsReplaced()
    {
        Harness harness = new(legacyExists: true);

        await harness.InstallAsync();

        int legacyStop = harness.Log.IndexOf("legacy:stop");
        int swap = harness.Log.IndexOf("fs:swap");

        Assert.True(legacyStop >= 0, "legacy service was never stopped");
        Assert.True(swap >= 0, "binaries were never swapped");
        Assert.True(
            legacyStop < swap,
            "binaries were replaced before the legacy authority was stopped");
    }

    [Fact]
    public async Task LegacyIsDeletedOnlyAfterPathVeerIsProvenHealthy()
    {
        Harness harness = new(legacyExists: true);

        await harness.InstallAsync();

        int start = harness.Log.IndexOf("pathveer:start");
        int delete = harness.Log.IndexOf(
            $"installer:delete:{LegacyServiceNames.ServiceName}");

        Assert.True(start >= 0 && delete >= 0);
        Assert.True(
            start < delete,
            "legacy service was deleted before PathVeer was healthy, "
            + "which would destroy the rollback target");
    }

    [Fact]
    public async Task LegacyIsDisabledBeforePathVeerStartsSoItCannotAutoRestart()
    {
        Harness harness = new(legacyExists: true);

        await harness.InstallAsync();

        int disable = harness.Log.IndexOf(
            $"installer:disable:{LegacyServiceNames.ServiceName}");
        int start = harness.Log.IndexOf("pathveer:start");

        Assert.True(disable >= 0 && start >= 0);
        Assert.True(disable < start);
    }

    [Fact]
    public async Task UpgradeFromAnEarlierPathVeerRecordsThePreviousVersion()
    {
        Harness harness = new(legacyExists: false, pathVeerExists: true);

        harness.FileSystem.Manifest = new InstallManifest
        {
            ProductVersion = "1.0.0",
            InstallRoot = InstallRoot
        };

        InstallReport report = await harness.InstallAsync("1.1.0");

        Assert.False(report.WasFreshInstall);
        Assert.Equal("1.0.0", report.PreviousVersion);
        Assert.Equal("1.1.0", report.InstalledVersion);
        Assert.Equal("1.0.0", harness.FileSystem.Manifest!.UpgradedFromVersion);
    }

    [Fact]
    public async Task ExistingServicePointingAtADeveloperCheckoutIsRepointed()
    {
        Harness harness = new(legacyExists: false, pathVeerExists: true);

        // Exactly the pre-36.7 defect: SCM pointed into the repo.
        harness.Installer.BinaryPaths[PathVeerServiceNames.ServiceName] =
            @"C:\codespace\PathVeer\artifacts\PathVeer.Service\publish\PathVeer.Service.exe";

        await harness.InstallAsync();

        Assert.Equal(
            harness.Layout.ServiceExecutablePath,
            harness.Installer.GetBinaryPath(
                PathVeerServiceNames.ServiceName));
    }

    // ---------------- single-authority failure paths ----------------

    [Fact]
    public async Task RefusesToProceedWhenLegacyCannotBeStopped()
    {
        Harness harness = new(legacyExists: true);
        harness.Legacy.RefuseToStop = true;

        InstallFailedException error =
            await Assert.ThrowsAsync<InstallFailedException>(
                () => harness.InstallAsync());

        Assert.Contains("could not be stopped", error.Message);

        // Nothing was replaced and PathVeer never started.
        Assert.False(harness.FileSystem.Swapped);
        Assert.NotEqual(
            ServiceControllerStatus.Running,
            harness.PathVeer.Status);
    }

    [Fact]
    public async Task NeverLeavesTwoAuthoritiesWhenLegacyRestartsItself()
    {
        Harness harness = new(legacyExists: true);
        harness.Legacy.RestartsItselfAfterStop = true;

        await Assert.ThrowsAsync<InstallFailedException>(
            () => harness.InstallAsync());

        bool bothRunning =
            harness.Legacy.Status == ServiceControllerStatus.Running
            && harness.PathVeer.Status == ServiceControllerStatus.Running;

        Assert.False(bothRunning, "two route authorities were left running");
    }

    [Fact]
    public async Task UnhealthyReadinessStopsPathVeerAndKeepsLegacyForRollback()
    {
        Harness harness = new(legacyExists: true);

        harness.Readiness.Result = new InstallReadiness(
            ProcessRunning: true,
            PrimaryPipeAvailable: false,
            StatusCommandSucceeded: false,
            StateRootIsCurrent: true,
            JournalRecoveryHealthy: true);

        InstallFailedException error =
            await Assert.ThrowsAsync<InstallFailedException>(
                () => harness.InstallAsync());

        Assert.Contains("readiness", error.Message);

        // PathVeer was stopped, and the legacy service was NOT deleted.
        Assert.Equal(
            ServiceControllerStatus.Stopped,
            harness.PathVeer.Status);

        Assert.DoesNotContain(
            LegacyServiceNames.ServiceName,
            harness.Installer.Deleted);
    }

    [Fact]
    public async Task RunningButUnhealthyIsTreatedAsAFailedUpgrade()
    {
        Harness harness = new(legacyExists: false);

        harness.Readiness.Result = new InstallReadiness(
            ProcessRunning: true,
            PrimaryPipeAvailable: true,
            StatusCommandSucceeded: true,
            StateRootIsCurrent: false,   // wrong state root
            JournalRecoveryHealthy: true);

        await Assert.ThrowsAsync<InstallFailedException>(
            () => harness.InstallAsync());
    }

    [Fact]
    public async Task CorruptPackageAbortsBeforeTouchingTheInstallation()
    {
        Harness harness = new(legacyExists: true);
        harness.FileSystem.PackageValid = false;

        await Assert.ThrowsAsync<InstallFailedException>(
            () => harness.InstallAsync());

        // The legacy authority is untouched: never stopped, never disabled.
        Assert.DoesNotContain("legacy:stop", harness.Log);
        Assert.Empty(harness.Installer.Disabled);
        Assert.False(harness.FileSystem.Swapped);
    }

    [Fact]
    public async Task IncompleteStagedPayloadIsDiscardedWithoutSwapping()
    {
        Harness harness = new(legacyExists: false);
        harness.FileSystem.StagedLayoutValid = false;

        await Assert.ThrowsAsync<InstallFailedException>(
            () => harness.InstallAsync());

        Assert.True(harness.FileSystem.StagingDiscarded);
        Assert.False(harness.FileSystem.Swapped);
    }

    [Fact]
    public async Task FailedLegacyDeletionStillLeavesItStoppedAndDisabled()
    {
        Harness harness = new(legacyExists: true);
        harness.Installer.DeleteFails = true;

        InstallReport report = await harness.InstallAsync();

        Assert.False(report.LegacyServiceRemoved);
        Assert.True(report.SingleAuthorityPreserved);

        Assert.Contains(
            LegacyServiceNames.ServiceName,
            harness.Installer.Disabled);

        Assert.NotEqual(
            ServiceControllerStatus.Running,
            harness.Legacy.Status);
    }

    [Fact]
    public async Task AnUnavailableScmForLegacyIsTreatedAsAbsentNotFatal()
    {
        Harness harness = new(legacyExists: true);
        harness.Legacy.ExistsThrows = true;

        InstallReport report = await harness.InstallAsync();

        Assert.False(report.LegacyServiceExisted);
    }

    // ---------------- idempotency ----------------

    [Fact]
    public async Task RepeatedInstallsConvergeWithoutDuplicatingAnything()
    {
        Harness harness = new(legacyExists: true);

        await harness.InstallAsync();

        string pathAfterFirst = harness.FileSystem.PathValue;

        await harness.InstallAsync();
        await harness.InstallAsync();

        // PATH did not grow.
        Assert.Equal(pathAfterFirst, harness.FileSystem.PathValue);

        Assert.Single(
            harness.FileSystem.PathValue.Split(';'),
            segment => segment.Contains(
                "PathVeer",
                StringComparison.OrdinalIgnoreCase));

        // Exactly one PathVeer service entry exists.
        Assert.Single(harness.Installer.BinaryPaths);
    }

    [Fact]
    public async Task ReinstallingTheSameVersionIsSafe()
    {
        Harness harness = new(legacyExists: false);

        await harness.InstallAsync("1.0.0");
        InstallReport second = await harness.InstallAsync("1.0.0");

        Assert.Equal("1.0.0", second.PreviousVersion);
        Assert.Equal("1.0.0", second.InstalledVersion);
        Assert.True(second.SingleAuthorityPreserved);
    }

    [Fact]
    public async Task RetryAfterAnInterruptedUpgradeConverges()
    {
        Harness harness = new(legacyExists: true);
        harness.Readiness.Result = new InstallReadiness(
            true, false, false, true, true);

        await Assert.ThrowsAsync<InstallFailedException>(
            () => harness.InstallAsync());

        // Operator fixes the environment and retries.
        harness.Readiness.Result = InstallReadiness.Healthy;

        InstallReport report = await harness.InstallAsync();

        Assert.True(report.SingleAuthorityPreserved);
        Assert.Equal(
            harness.Layout.ServiceExecutablePath,
            report.ServiceBinaryPath);
    }

    [Fact]
    public async Task PathEntryIsAddedExactlyOnceAcrossManyInstalls()
    {
        Harness harness = new(legacyExists: false);

        for (int i = 0; i < 5; i++)
        {
            await harness.InstallAsync();
        }

        int occurrences = harness.FileSystem.PathValue
            .Split(';')
            .Count(segment => segment.Contains(
                "PathVeer",
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(1, occurrences);
    }

    // ---------------- uninstall ----------------

    [Fact]
    public async Task UninstallRemovesTheServiceButRetainsPersistentState()
    {
        Harness harness = new(legacyExists: false);
        await harness.InstallAsync();

        UninstallReport report =
            await harness.Build().UninstallAsync(purgeState: false);

        Assert.True(report.ServiceRemoved);
        Assert.False(report.StateRemoved);
        Assert.False(harness.FileSystem.StatePurged);

        Assert.Contains(
            PathVeerServiceNames.ServiceName,
            harness.Installer.Deleted);
    }

    [Fact]
    public async Task UninstallReleasesManagedRoutesBeforeRemovingTheService()
    {
        Harness harness = new(legacyExists: false);
        await harness.InstallAsync();

        harness.PathVeer.Status = ServiceControllerStatus.Running;

        UninstallReport report = await harness.Build().UninstallAsync();

        Assert.True(harness.Readiness.RouteReleaseAttempted);
        Assert.True(report.ManagedRoutesReleased);
    }

    [Fact]
    public async Task UninstallRemovesThePathEntryAndTrayStartup()
    {
        Harness harness = new(legacyExists: false);
        await harness.InstallAsync();

        await harness.Build().UninstallAsync();

        Assert.False(PathEnvironmentEditor.ContainsEntry(
            harness.FileSystem.PathValue,
            harness.Layout.PathEnvironmentEntry));

        Assert.True(harness.FileSystem.TrayStartupRemoved);
    }

    [Fact]
    public async Task UninstallDeletesStateOnlyWhenPurgeIsExplicit()
    {
        Harness harness = new(legacyExists: false);
        await harness.InstallAsync();

        UninstallReport report =
            await harness.Build().UninstallAsync(purgeState: true);

        Assert.True(report.StateRemoved);
        Assert.True(harness.FileSystem.StatePurged);
    }

    [Fact]
    public async Task UninstallRemovesTheInstalledBinaries()
    {
        Harness harness = new(legacyExists: false);
        await harness.InstallAsync();

        await harness.Build().UninstallAsync();

        Assert.Contains(InstallRoot, harness.FileSystem.RemovedDirectories);
    }
}
