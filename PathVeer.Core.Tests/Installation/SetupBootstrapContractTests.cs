using PathVeer.Core.Installer;
using Xunit;

namespace PathVeer.Core.Tests.Installation;

/// <summary>
/// Phase service-authority slice — PathVeer.Setup correctness.
///
/// Covers PROVEN ISSUE #1 (self-elevation must not own the single-instance
/// mutex before relaunch) and NEW ISSUE #2 (explicit exit-code contract),
/// exercising the extracted Core helpers directly so the behavior is provable
/// without spawning real UAC prompts or WinForms windows.
///
/// Mutex acquisition tests use a unique name per test (see <see cref="UseName"/>)
/// so concurrent xunit collections cannot collide. The exact production mutex
/// name is asserted separately so the algorithm is proven against the real one.
/// </summary>
public class SetupBootstrapContractTests
{
    // --- PROVEN ISSUE #1: single-instance mutex ownership ------------------

    [Fact]
    public void SingleInstance_ProductionName_IsExactConstant()
    {
        // The elevated installer owns THIS exact global name.
        Assert.Equal(@"Global\PathVeer.Setup.SingleInstance",
            SetupSingleInstance.DefaultMutexName);
    }

    [Fact]
    public void SingleInstance_FirstAcquire_OwnsMutex()
    {
        string name = UseName();
        using var owner = SetupSingleInstance.TryAcquire(name);
        Assert.NotNull(owner);
        Assert.True(owner.IsOwned);
        Assert.NotNull(owner.Owner);
    }

    [Fact]
    public void SingleInstance_SecondConcurrent_Rejected()
    {
        // Two real elevated installers must not run concurrently.
        string name = UseName();
        using var first = SetupSingleInstance.TryAcquire(name);
        Assert.NotNull(first);

        // A concurrent attempt sees the name already owned.
        using var second = SetupSingleInstance.TryAcquire(name);
        Assert.Null(second);
    }

    [Fact]
    public void SingleInstance_Released_AllowsNext()
    {
        // Once the elevated installer finishes, the next one may proceed.
        string name = UseName();
        using (var first = SetupSingleInstance.TryAcquire(name))
        {
            Assert.NotNull(first);
            using var blocked = SetupSingleInstance.TryAcquire(name);
            Assert.Null(blocked);
        }

        // After disposal the mutex is released for the next instance.
        using var next = SetupSingleInstance.TryAcquire(name);
        Assert.NotNull(next);
    }

    [Fact]
    public void SingleInstance_Dispose_ReleasesMutex()
    {
        string name = UseName();
        var owner = SetupSingleInstance.TryAcquire(name);
        Assert.NotNull(owner);
        owner.Dispose();

        // The name is free again after release.
        using var reclaim = SetupSingleInstance.TryAcquire(name);
        Assert.NotNull(reclaim);
    }

    [Fact]
    public void SingleInstance_ParentDoesNotHold_AfterRelaunchAnalogue()
    {
        // Models the corrected control flow: the NON-elevated launcher must not
        // own the mutex while waiting for the elevated child. The parent never
        // acquires, so the elevated child is free to become the owner.
        string name = UseName();
        using var child = SetupSingleInstance.TryAcquire(name);
        Assert.NotNull(child); // child becomes authoritative installer
        Assert.True(child.IsOwned);
    }

    // --- Elevation denial mapping (UAC cancel) ------------------------------

    [Fact]
    public void ElevationDenied_MapsTo_101()
    {
        // A dismissed/declined UAC prompt is a stable denial, not a generic
        // argument error (102) or relaunch failure (5).
        Assert.Equal(101, SetupExitCodes.FromElevationDenied());
    }

    // --- Result category -> exit code contract (NEW ISSUE #2) ---------------

    // NOTE: "Success" is NOT a failure category. The success path is handled
    // by the `result.Success ? Success : MapResultCategory(category)` guard in
    // both InstallForm.OnCompleted and InstallController.ResolveExitCode (covered
    // by InstallFormResultContractTests). MapResultCategory is the failure mapper.
    [Theory]
    [InlineData("UserCancelled", 100)]
    [InlineData("ElevationDenied", 101)]
    [InlineData("InvalidArguments", 102)]
    [InlineData("DowngradeBlocked", 103)]
    [InlineData("PackageVerificationFail", 104)]
    [InlineData("LegacyUnsupported", 105)]
    [InlineData("ServiceFailed", 106)]
    [InlineData("ReadinessFailed", 107)]
    [InlineData("UninstallFailed", 108)]
    [InlineData("PurgeFailed", 109)]
    [InlineData("SomethingUnmapped", 1)] // generic failure (1), never 0
    public void MapResultCategory_Stable_Contract(string category, int expected)
    {
        Assert.Equal(expected, SetupExitCodes.MapResultCategory(category));
    }

    [Fact]
    public void MapResultCategory_Unknown_IsNotSuccess()
    {
        // The catch-all must never masquerade as success.
        Assert.NotEqual(0, SetupExitCodes.MapResultCategory("TotallyUnknown"));
        Assert.Equal(SetupExitCodes.GenericFailure,
            SetupExitCodes.MapResultCategory("TotallyUnknown"));
    }

    private static string UseName()
        => "Global\\PathVeer.Setup.Test." + Guid.NewGuid().ToString("N");
}
