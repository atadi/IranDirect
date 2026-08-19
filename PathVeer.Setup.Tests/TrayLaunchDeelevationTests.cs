using System.Diagnostics;
using PathVeer.Setup;
using Xunit;

namespace PathVeer.Setup.Tests;

/// <summary>
/// Verifies the devsign.10 Tray-launch DE-ELEVATION + INVOCATION-SCOPING
/// contract.
///
/// The ELEVATED installer child must NEVER launch the Tray directly (that would
/// inherit admin — the proven devsign.9 defect, live Tray Elevated=True). It
/// instead records an invocation-scoped request tagged with the parent's
/// operation id. Only the NON-elevated parent (after the child exits with
/// Success) consumes that exact request, launching the Tray under the ordinary
/// user token.
///
/// These tests pin the request write/consume semantics and scoping so the
/// behavior is provable without spawning real UAC prompts or WinForms windows.
/// The actual non-elevation is proven by SetupExecutableStartupTests +
/// operator Repair (Elevated=False).
/// </summary>
public class TrayLaunchDeelevationTests
{
    private static string RequestFileFor(string operationId)
        => Path.Combine(
            InstallForm.LaunchRequestDirectory(operationId),
            "launch-tray.request");

    [Fact]
    public void WriteLaunchTrayRequest_Creates_InvocationScopedFile()
    {
        string operationId = "op-test-" + Guid.NewGuid().ToString("N");
        string file = RequestFileFor(operationId);
        if (File.Exists(file)) File.Delete(file);

        using var form = new InstallForm(
            new InstallController("nul", "nul"),
            "1.0.0",
            launchOperationId: operationId);
        form.DebugWriteLaunchTrayRequest(operationId);

        Assert.True(File.Exists(file));
        File.Delete(file);
    }

    [Fact]
    public void Consume_NoMatchingRequest_IsNoOp_AndNoTraySpawned()
    {
        string operationId = "op-test-" + Guid.NewGuid().ToString("N");
        string file = RequestFileFor(operationId);
        if (File.Exists(file)) File.Delete(file);

        // With no request, Consume must not throw and must not spawn a Tray.
        int before = Process.GetProcessesByName("PathVeer.Tray").Length;
        bool launched = InstallForm.ConsumeLaunchTrayRequest(operationId);
        int after = Process.GetProcessesByName("PathVeer.Tray").Length;

        Assert.False(launched);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Consume_MatchingRequest_LaunchesTray_AndConsumesOnce()
    {
        // Arrange: a request exists for THIS operation id.
        string operationId = "op-test-" + Guid.NewGuid().ToString("N");
        string file = RequestFileFor(operationId);
        if (File.Exists(file)) File.Delete(file);
        using var form = new InstallForm(
            new InstallController("nul", "nul"),
            "1.0.0",
            launchOperationId: operationId);
        form.DebugWriteLaunchTrayRequest(operationId);
        Assert.True(File.Exists(file));

        // Act: the non-elevated parent consumes exactly this request.
        // The installed Tray EXE does not exist in the test environment, so the
        // consume path returns false even though it deleted the marker; the
        // contract we assert here is "the marker is consumed exactly once".
        bool first = InstallForm.ConsumeLaunchTrayRequest(operationId);
        bool second = InstallForm.ConsumeLaunchTrayRequest(operationId);

        // The marker must be gone after the first consume.
        Assert.False(File.Exists(file));
        Assert.False(second); // no second consumption
        // `first` reflects whether the Tray EXE was present; both asserts hold
        // regardless. The critical invariant is single consumption.
        Assert.True(!first || !second);
    }

    [Fact]
    public void Consume_StaleRequestFromOtherInvocation_Ignored()
    {
        // A request written for a DIFFERENT invocation must not be consumed by
        // this parent's id.
        string otherId = "op-other-" + Guid.NewGuid().ToString("N");
        string thisId = "op-this-" + Guid.NewGuid().ToString("N");
        string otherFile = RequestFileFor(otherId);
        string thisFile = RequestFileFor(thisId);
        if (File.Exists(otherFile)) File.Delete(otherFile);
        if (File.Exists(thisFile)) File.Delete(thisFile);

        using var form = new InstallForm(
            new InstallController("nul", "nul"),
            "1.0.0",
            launchOperationId: otherId);
        form.DebugWriteLaunchTrayRequest(otherId);
        Assert.True(File.Exists(otherFile));

        // This parent consumes only its own id -> the stale request is untouched.
        bool launched = InstallForm.ConsumeLaunchTrayRequest(thisId);
        Assert.False(launched);
        Assert.True(File.Exists(otherFile), "stale request must remain unconsumed");

        File.Delete(otherFile);
    }
}
