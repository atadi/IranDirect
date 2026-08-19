using System.Diagnostics;
using Xunit;

namespace PathVeer.Setup.Tests;

/// <summary>
/// Verifies the Tray-launch de-elevation contract: the elevated installer must
/// NOT launch the Tray directly (which would inherit elevation). It defers via a
/// sentinel the NON-elevated parent consumes, so the Tray is spawned under the
/// ordinary user token. These tests pin the deferral logic; the actual
/// non-elevation is proven by SetupExecutableStartupTests + the operator Repair.
/// </summary>
public class TrayLaunchDeelevationTests
{
    [Fact]
    public void WriteLaunchTraySentinel_Creates_SentinelFile()
    {
        string sentinel = Path.Combine(
            Path.GetTempPath(), "PathVeer.Setup.LaunchTray.sentinel");
        if (File.Exists(sentinel)) File.Delete(sentinel);

        InstallForm.WriteLaunchTraySentinel();

        Assert.True(File.Exists(sentinel));
        File.Delete(sentinel);
    }

    [Fact]
    public void ConsumeLaunchTraySentinel_NoSentinel_IsNoOp()
    {
        string sentinel = Path.Combine(
            Path.GetTempPath(), "PathVeer.Setup.LaunchTray.sentinel");
        if (File.Exists(sentinel)) File.Delete(sentinel);

        // With no sentinel, Consume must not throw and must not spawn a Tray.
        int before = Process.GetProcessesByName("PathVeer.Tray").Length;
        InstallForm.ConsumeLaunchTraySentinel();
        int after = Process.GetProcessesByName("PathVeer.Tray").Length;

        Assert.Equal(before, after);
    }
}
