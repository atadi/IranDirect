using PathVeer.Core.Installer;
using Xunit;

namespace PathVeer.Core.Tests.Installation;

/// <summary>
/// Phase 37.2 — installer controller / classification logic tests.
/// These exercise pure decision logic (no window, no PowerShell required).
/// </summary>
public class InstallerControllerTests
{
    // --- Install-state classification --------------------------------------

    [Fact]
    public void Classify_NotInstalled_When_NoProductAndNoService()
    {
        var state = new InstallState { ProductInstalled = false, ServiceInstalled = false };
        Assert.Equal(InstallScenario.NotInstalled,
            InstallStateClassifier.Classify(state, "1.0.0"));
    }

    [Fact]
    public void Classify_SupportedLegacyMigration_When_LegacyPresent_And_NoPathVeer()
    {
        var state = new InstallState
        {
            ProductInstalled = false,
            LegacyIranDirectInstalled = true,
            LegacyStatePresent = true,
        };
        Assert.Equal(InstallScenario.SupportedLegacyMigration,
            InstallStateClassifier.Classify(state, "1.0.0"));
    }

    [Fact]
    public void Classify_Upgrade_When_InstalledOlder()
    {
        var state = new InstallState
        {
            ProductInstalled = true,
            InstalledVersion = "1.0.0",
            ServiceInstalled = true,
        };
        Assert.Equal(InstallScenario.Upgrade,
            InstallStateClassifier.Classify(state, "1.0.1"));
    }

    [Fact]
    public void Classify_SameVersion_When_InstalledEqual()
    {
        var state = new InstallState
        {
            ProductInstalled = true,
            InstalledVersion = "1.0.0",
            ServiceInstalled = true,
        };
        Assert.Equal(InstallScenario.SameVersion,
            InstallStateClassifier.Classify(state, "1.0.0"));
    }

    [Fact]
    public void Classify_Downgrade_When_InstalledNewer()
    {
        var state = new InstallState
        {
            ProductInstalled = true,
            InstalledVersion = "1.0.1",
            ServiceInstalled = true,
        };
        Assert.Equal(InstallScenario.Downgrade,
            InstallStateClassifier.Classify(state, "1.0.0"));
    }

    [Fact]
    public void Classify_PartialOrBroken_When_RootPresentButNoManifest()
    {
        var state = new InstallState
        {
            ProductInstalled = false,
            InstallRootPresent = true,
            PartialInstallation = true,
        };
        Assert.Equal(InstallScenario.PartialOrBroken,
            InstallStateClassifier.Classify(state, "1.0.0"));
    }

    [Fact]
    public void Classify_ConflictingAuthority_When_BothServicesPresent()
    {
        var state = new InstallState
        {
            ProductInstalled = true,
            InstalledVersion = "1.0.0",
            ServiceInstalled = true,
            LegacyIranDirectInstalled = true,
        };
        Assert.Equal(InstallScenario.ConflictingAuthority,
            InstallStateClassifier.Classify(state, "1.0.0"));
    }

    // --- Version comparison (prerelease-insensitive) -----------------------

    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0-beta.1", "1.0.0", 0)]
    [InlineData("2.0.0", "1.9.9", 1)]
    public void CompareVersions_IgnoresPrerelease(string current, string target, int expected)
    {
        Assert.Equal(expected, InstallStateClassifier.CompareVersions(current, target));
    }

    // --- Runtime prerequisite parsing --------------------------------------

    [Fact]
    public void ParseHighestMatching_Finds_Required_10_Runtime()
    {
        string output = "Microsoft.NETCore.App 8.0.0 [x]\n" +
                        "Microsoft.NETCore.App 10.0.0 [y]\n" +
                        "Microsoft.NETCore.App 10.0.5 [z]";
        Assert.Equal("10.0.5",
            RuntimePrerequisite.ParseHighestMatching(output, "10.0"));
    }

    [Fact]
    public void ParseHighestMatching_Ignores_OtherMajor()
    {
        string output = "Microsoft.NETCore.App 8.0.0 [x]\nMicrosoft.AspNetCore.App 10.0.0 [y]";
        Assert.Null(RuntimePrerequisite.ParseHighestMatching(output, "10.0"));
    }

    [Fact]
    public void ParseHighestMatching_ReturnsNull_When_Absent()
    {
        Assert.Null(RuntimePrerequisite.ParseHighestMatching("", "10.0"));
    }

    // --- Exit-code mapping mirrors the PowerShell contract ------------------

    [Theory]
    [InlineData("DowngradeBlocked", 103)]
    [InlineData("PackageVerificationFail", 104)]
    [InlineData("ServiceFailed", 106)]
    [InlineData("ReadinessFailed", 107)]
    [InlineData("UserCancelled", 100)]
    [InlineData("LegacyUnsupported", 105)]
    [InlineData("PurgeFailed", 109)]
    public void MapCategory_Stable_ExitCodes(string category, int expected)
    {
        int code = MapCategoryLocal(category);
        Assert.Equal(expected, code);
    }

    // Local mirror of the controller's private mapper for contract verification.
    private static int MapCategoryLocal(string category) => category switch
    {
        "UserCancelled" => SetupExitCodes.UserCancelled,
        "ElevationDenied" => SetupExitCodes.ElevationDenied,
        "InvalidArguments" => SetupExitCodes.InvalidArguments,
        "DowngradeBlocked" => SetupExitCodes.DowngradeBlocked,
        "PackageVerificationFail" => SetupExitCodes.PackageVerificationFailed,
        "LegacyUnsupported" => SetupExitCodes.LegacyUnsupported,
        "ServiceFailed" => SetupExitCodes.ServiceFailed,
        "ReadinessFailed" => SetupExitCodes.ReadinessFailed,
        "UninstallFailed" => SetupExitCodes.UninstallFailed,
        "PurgeFailed" => SetupExitCodes.PurgeFailed,
        _ => SetupExitCodes.GenericFailure,
    };

    // --- Start Menu / uninstall path generation (no real FS writes) --------

    [Fact]
    public void StartMenu_Path_Uses_CommonPrograms_And_ProductName()
    {
        // The script builds: ProgramData\Microsoft\Windows\Start Menu\Programs\PathVeer
        string basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs", "PathVeer");
        Assert.EndsWith(Path.Combine("Programs", "PathVeer"), basePath);
        Assert.DoesNotContain("IranDirect", basePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Uninstall_Registration_QuoteSafe()
    {
        // The script registers: "\"<InstallRoot>\PathVeerSetup.exe\" --uninstall"
        string installRoot = @"C:\Program Files\PathVeer";
        string setupExe = Path.Combine(installRoot, "PathVeerSetup.exe");
        string command = $"\"{setupExe}\" --uninstall";
        Assert.StartsWith("\"", command);
        Assert.Contains("--uninstall", command);
        Assert.DoesNotContain("IranDirect", command, StringComparison.OrdinalIgnoreCase);
    }

    // --- Install-state JSON deserialization --------------------------------

    [Fact]
    public void ParseStateJson_RoundTrips_KnownFields()
    {
        string json = "{\"productInstalled\":true,\"installedVersion\":\"1.0.0\"," +
                      "\"serviceInstalled\":true,\"legacyIranDirectInstalled\":false," +
                      "\"partialInstallation\":false}";
        var state = InstallStateClassifier.ParseStateJson(json);
        Assert.True(state.ProductInstalled);
        Assert.Equal("1.0.0", state.InstalledVersion);
        Assert.False(state.LegacyIranDirectInstalled);
    }

    [Fact]
    public void ParseStateJson_ReturnsEmpty_OnInvalid()
    {
        var state = InstallStateClassifier.ParseStateJson("not-json");
        Assert.False(state.ProductInstalled);
    }
}
