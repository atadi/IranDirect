using PathVeer.Core.Cli;
using PathVeer.Core.Ipc;
using PathVeer.Core.Observability.Telemetry;
using PathVeer.Core.ServiceLifecycle;
using PathVeer.Core.State;
using PathVeer.Tray;

namespace PathVeer.Core.Tests.Branding;

/// <summary>
/// Permanent branding/UX regression tests for Phase 36.5 (PathVeer CLI/Tray/
/// support rebrand). These pin that active user-facing branding reads PathVeer
/// while the approved compatibility literals (legacy pipe, legacy state root,
/// telemetry identity) are deliberately retained.
/// </summary>
public sealed class BrandingTests
{
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null &&
               !File.Exists(Path.Combine(dir, "PathVeer.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException(
            "repo root not found");
    }

    #region CLI

    [Fact]
    public void Cli_SourceHasNoUserFacingIranDirectString()
    {
        // Active user-facing branding in the CLI must be PathVeer. Any remaining
        // "IranDirect" in CLI source must be a non-quoted type reference
        // (e.g. PathVeerDiagnostics), never a visible string shown to users.
        string cliRoot = Path.Combine(RepoRoot(), "PathVeer.Cli");
        foreach (string file in Directory.GetFiles(
                     cliRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Replace('\\', '/').Contains("/bin/") ||
                file.Replace('\\', '/').Contains("/obj/"))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            Assert.False(
                text.Contains("\"IranDirect"),
                $"CLI file {file} still contains a user-facing \"IranDirect\" string.");
            Assert.DoesNotContain("Usage: IranDirect.Cli", text);
            Assert.DoesNotContain("IranDirect command failed", text);
        }
    }

    [Fact]
    public void Cli_DiagnosticsHeadingIsPathVeer()
    {
        IEnumerable<string> lines = DiagnosticReportCliRenderer.Render(
            new PathVeer.Core.Diagnostics.DiagnosticReport(
                CapturedAt: System.DateTimeOffset.UtcNow,
                Results: []),
            PathVeer.Core.Diagnostics.DiagnosticFormat.Summary);

        Assert.Contains("PathVeer Diagnostics", lines);
        Assert.DoesNotContain("IranDirect Diagnostics", lines);
    }

    [Fact]
    public void Cli_SupportBundlePrefixIsPathVeer()
    {
        Assert.Equal("PathVeer-Support", SupportBundlePathBuilder.DefaultFilePrefix);
    }

    #endregion

    #region Tray

    [Fact]
    public void Tray_SourceHasNoUserFacingIranDirectString()
    {
        string trayRoot = Path.Combine(RepoRoot(), "PathVeer.Tray");
        foreach (string file in Directory.GetFiles(
                     trayRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Replace('\\', '/').Contains("/bin/") ||
                file.Replace('\\', '/').Contains("/obj/"))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            Assert.False(
                text.Contains("\"IranDirect"),
                $"Tray file {file} still contains a user-facing \"IranDirect\" string.");
        }
    }

    [Fact]
    public void Tray_SupportBundleDefaultFileNameIsPathVeer()
    {
        string name = SupportBundleDefaultFileName.Build(
            new FixedTimeProvider(
                new System.DateTimeOffset(
                    2026, 8, 3, 14, 22, 1, System.TimeSpan.Zero)));

        Assert.StartsWith("PathVeer-Support-", name);
        Assert.EndsWith(".zip", name);
        Assert.DoesNotContain("IranDirect", name);
    }

    [Fact]
    public void Tray_SaveDialogTitleIsPathVeer()
    {
        // The SaveFileDialogAdapter builds its Title from a PathVeer string.
        // Verified by source scan above; assert the literal here for the record.
        string adapterSource = File.ReadAllText(
            Path.Combine(RepoRoot(), "PathVeer.Tray/SaveFileDialogAdapter.cs"));
        Assert.Contains("Save PathVeer Support Bundle", adapterSource);
        Assert.Contains("PathVeer Support Bundle (*.zip)|*.zip|", adapterSource);
        Assert.DoesNotContain("IranDirect", adapterSource);
    }

    #endregion

    #region Source naming / compatibility literals

    [Fact]
    public void ServiceClient_TypeRenamedToPathVeer()
    {
        // The client type is now PathVeerServiceClient (wire-safe rename of the
        // old IranDirectServiceClient). The class body lives in
        // PathVeerServiceClient.cs; the type identity is what matters.
        string source = File.ReadAllText(
            Path.Combine(
                RepoRoot(), "PathVeer.Core/Ipc/PathVeerServiceClient.cs"));
        Assert.Contains("class PathVeerServiceClient", source);
    }

    [Fact]
    public void LegacyPipeLiteralPreserved()
    {
        Assert.Equal(
            "IranDirect.Control.v1", PathVeerPipeNames.LegacyPipeName);
        Assert.Equal(
            "PathVeer.Control.v1", PathVeerPipeNames.PrimaryPipeName);
    }

    [Fact]
    public void LegacyStateRootLiteralPreserved()
    {
        Assert.Equal("IranDirect", StateRootResolver.LegacyStateDirectoryName);
        Assert.Equal("PathVeer", StateRootResolver.CurrentStateDirectoryName);
    }

    [Fact]
    public void TelemetryIdentityFrozen()
    {
        // Phase 36.6 owns telemetry rename; the source/meter identity stays
        // IranDirect.Core until then.
        Assert.Equal("IranDirect.Core", IranDirectTelemetry.SourceName);
    }

    [Fact]
    public void NewServiceIdentityIsPathVeer()
    {
        Assert.Equal("PathVeer", PathVeerServiceNames.ServiceName);
        Assert.Equal("PathVeer Service", PathVeerServiceNames.DisplayName);
        Assert.Equal("IranDirect", LegacyServiceNames.ServiceName);
    }

    #endregion

    #region Helpers

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly System.DateTimeOffset _now;

        public FixedTimeProvider(System.DateTimeOffset now) => _now = now;

        public override System.DateTimeOffset GetUtcNow() => _now;
    }

    #endregion
}
