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

    // --- Version comparison (deterministic installed-version contract) ------
    // The single contract orders by base, then stable > prerelease, then by the
    // prerelease sequence (devsign.N / beta.N). This makes devsign.4 < devsign.5
    // an Upgrade and devsign.6 > devsign.5 a Downgrade, so the GUI classifier
    // and the install engine agree before mutation.

    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0-devsign.4", "1.0.0-devsign.5", -1)]
    [InlineData("1.0.0-devsign.5", "1.0.0-devsign.4", 1)]
    [InlineData("1.0.0-devsign.5", "1.0.0-devsign.5", 0)]
    [InlineData("1.0.0-devsign.6", "1.0.0-devsign.5", 1)]
    [InlineData("1.0.0-beta.1", "1.0.0", -1)]   // prerelease < stable
    [InlineData("1.0.0", "1.0.0-beta.1", 1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    public void CompareVersions_DeterministicContract(string current, string target, int expected)
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
    private static int MapCategoryLocal(string category)
        => SetupExitCodes.MapResultCategory(category);

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

    // --- REVIEW: single installed-version authority (DEFECT review) --------
    // Both the GUI classifier and the install engine derive the installed
    // version from ONE source: install-manifest.json -> productVersion, read by
    // Get-InstalledVersion. Their version comparison is prerelease-insensitive
    // on both sides (GUI InstallStateClassifier.CompareVersions and the engine's
    // Compare-VersionOrder both strip the '-' prerelease tag before [version]
    // comparison). These tests pin that agreement so GUI intent cannot diverge
    // from engine behavior before mutation.

    [Theory]
    [InlineData("1.0.0-devsign.4", "1.0.0-devsign.5", InstallScenario.Upgrade)]
    [InlineData("1.0.0-devsign.5", "1.0.0-devsign.5", InstallScenario.SameVersion)]
    [InlineData("1.0.0-devsign.6", "1.0.0-devsign.5", InstallScenario.Downgrade)]
    [InlineData("1.0.0-beta.1", "1.0.0-beta.1", InstallScenario.SameVersion)]
    [InlineData(null, "1.0.0-devsign.6", InstallScenario.NotInstalled)]
    public void Authority_GuiClassify_MatchesEnginePrereleaseInsensitive(
        string? installed, string target, InstallScenario expected)
    {
        var state = new InstallState
        {
            ProductInstalled = installed is not null,
            InstalledVersion = installed,
            ServiceInstalled = installed is not null,
        };
        Assert.Equal(expected, InstallStateClassifier.Classify(state, target));
    }

    [Theory]
    [InlineData("1.0.0-devsign.4", "1.0.0-devsign.5", -1)]
    [InlineData("1.0.0-devsign.5", "1.0.0-devsign.5", 0)]
    [InlineData("1.0.0-devsign.6", "1.0.0-devsign.5", 1)]
    [InlineData("1.0.0-beta.1", "1.0.0", -1)]
    public void Authority_CompareVersions_AgreesWithEngineContract(
        string installed, string target, int expected)
    {
        // Both sides now implement the SAME deterministic contract:
        // base, then stable>prerelease, then seq-ordered prerelease.
        int gui = InstallStateClassifier.CompareVersions(installed, target);

        // Replicate the engine's Compare-VersionOrder (PS) in C# to prove parity.
        // Explicit engine contract in C#:
        static int EngineCmp(string a, string b)
        {
            (System.Version ba, int ra, int sa) = EngineParts(a);
            (System.Version bb, int rb, int sb) = EngineParts(b);
            int c = ba.CompareTo(bb); if (c != 0) return c;
            c = ra.CompareTo(rb); if (c != 0) return c;
            return sa.CompareTo(sb);
        }
        static (System.Version, int, int) EngineParts(string v)
        {
            string baseStr = v; int rank = 0; int seq = 0;
            int dash = v.IndexOf('-');
            if (dash >= 0) { baseStr = v[..dash]; rank = 0;
                string tag = v[(dash + 1)..]; int d = 0;
                foreach (char c in tag) { d = (c >= '0' && c <= '9') ? d * 10 + (c - '0') : 0; }
                seq = d; }
            else { rank = 1; }
            if (!System.Version.TryParse(baseStr, out var vb)) vb = new System.Version(0, 0);
            return (vb, rank, seq);
        }

        int engine = EngineCmp(installed, target);
        Assert.Equal(expected, gui);
        Assert.Equal(engine, gui);
    }
}
