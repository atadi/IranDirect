using PathVeer.Core.Installation;

namespace PathVeer.Core.Tests.Installation;

/// <summary>
/// Phase 36.7 — canonical install layout.
///
/// The central guarantee: an installed PathVeer never resolves back to a
/// developer checkout, and the three roots (repo / binaries / state) stay
/// distinct.
/// </summary>
public sealed class InstallLayoutTests
{
    private const string Root = @"C:\Program Files\PathVeer";

    [Fact]
    public void ComponentDirectoriesSitUnderTheInstallRoot()
    {
        InstallLayout layout = new(Root);

        Assert.Equal(@"C:\Program Files\PathVeer\Service", layout.ServiceDirectory);
        Assert.Equal(@"C:\Program Files\PathVeer\Cli", layout.CliDirectory);
        Assert.Equal(@"C:\Program Files\PathVeer\Tray", layout.TrayDirectory);
    }

    [Fact]
    public void ExecutablePathsUsePathVeerNames()
    {
        InstallLayout layout = new(Root);

        Assert.EndsWith(@"Service\PathVeer.Service.exe", layout.ServiceExecutablePath);
        Assert.EndsWith(@"Cli\PathVeer.Cli.exe", layout.CliExecutablePath);
        Assert.EndsWith(@"Tray\PathVeer.Tray.exe", layout.TrayExecutablePath);
    }

    [Fact]
    public void NoInstalledPathContainsLegacyBranding()
    {
        InstallLayout layout = new(Root);

        foreach (string path in new[]
                 {
                     layout.InstallRoot,
                     layout.ServiceExecutablePath,
                     layout.CliExecutablePath,
                     layout.TrayExecutablePath,
                     layout.ManifestPath,
                     layout.PathEnvironmentEntry
                 })
        {
            Assert.DoesNotContain(
                "IranDirect",
                path,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OnlyTheCliDirectoryIsExposedOnPath()
    {
        InstallLayout layout = new(Root);

        Assert.Equal(layout.CliDirectory, layout.PathEnvironmentEntry);
        Assert.NotEqual(layout.ServiceDirectory, layout.PathEnvironmentEntry);
        Assert.NotEqual(layout.TrayDirectory, layout.PathEnvironmentEntry);
    }

    [Fact]
    public void ContainsPathAcceptsPathsInsideTheInstallRoot()
    {
        InstallLayout layout = new(Root);

        Assert.True(layout.ContainsPath(layout.ServiceExecutablePath));
        Assert.True(layout.ContainsPath(
            "\"" + layout.ServiceExecutablePath + "\""));
    }

    [Fact]
    public void ContainsPathRejectsADeveloperCheckoutPath()
    {
        InstallLayout layout = new(Root);

        // This is the exact defect Phase 36.7 exists to remove: the previous
        // installer pointed the SCM at the repository's artifacts directory.
        Assert.False(layout.ContainsPath(
            @"C:\codespace\PathVeer\artifacts\PathVeer.Service\publish\PathVeer.Service.exe"));
    }

    [Fact]
    public void ContainsPathRejectsNullAndEmpty()
    {
        InstallLayout layout = new(Root);

        Assert.False(layout.ContainsPath(null));
        Assert.False(layout.ContainsPath("   "));
    }

    [Fact]
    public void StagingIsASiblingInsideTheInstallRoot()
    {
        InstallLayout layout = new(Root);

        // Same volume as the live directories, so the swap is a move.
        Assert.True(layout.ContainsPath(layout.StagingDirectory));
    }

    [Fact]
    public void DefaultLayoutResolvesUnderProgramFilesNotTheRepository()
    {
        InstallLayout layout = InstallLayout.CreateDefault();

        Assert.EndsWith("PathVeer", layout.InstallRoot);
        Assert.DoesNotContain(
            "codespace",
            layout.InstallRoot,
            StringComparison.OrdinalIgnoreCase);
    }
}
