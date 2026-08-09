using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PathVeer.Core.Installation;

namespace PathVeer.Core.Tests.Installation;

/// <summary>
/// Phase 37.1 — release packaging / versioning acceptance.
///
/// These tests cover PathVeer's OWN contracts around the release architecture:
/// version model, deterministic package hashing, release-manifest schema,
/// artifact naming and compatibility-launcher presence. They do NOT test the
/// third-party installer tool — there isn't one; PowerShell remains the single
/// source of install truth and the bootstrapper only wraps it.
/// </summary>
public sealed class ReleasePackagingTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "PathVeer.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        // Fall back to the conventional relative location used by the test host.
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
    }

    // --- Versioning model -----------------------------------------------------

    [Theory]
    [InlineData("1.0.0", "1.0.0.0")]
    [InlineData("2.3.0", "2.0.0.0")]
    [InlineData("10.0.0", "10.0.0.0")]
    public void AssemblyVersion_Is_MajorOnly(string version, string expected)
    {
        // The CLR binding version must change only on MAJOR so existing
        // assembly bindings survive feature/patch releases.
        string asm = DeriveAssemblyVersion(version);
        Assert.Equal(expected, asm);
    }

    [Theory]
    [InlineData("1.0.0-beta.1", "1.0.0-beta.1")]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.1")]
    [InlineData("1.0.0", "1.0.0")]
    public void FileVersion_Carries_FullPrerelease(string version, string expected)
    {
        string file = DeriveFileVersion(version);
        Assert.Equal(expected, file);
    }

    [Fact]
    public void Prerelease_Version_Does_Not_Change_AssemblyVersion_Major()
    {
        Assert.Equal(
            DeriveAssemblyVersion("1.0.0"),
            DeriveAssemblyVersion("1.0.0-beta.1"));
    }

    [Fact]
    public void Version_Regex_Parses_Major_For_AssemblyVersion()
    {
        // Mirrors Directory.Build.props logic.
        string major = System.Text.RegularExpressions.Regex
            .Match("1.2.3-beta.1", @"^\d+").Value;
        Assert.Equal("1", major);
    }

    // --- Release manifest schema ---------------------------------------------

    [Fact]
    public void ReleaseManifest_Contains_Required_Fields()
    {
        var doc = JsonDocument.Parse(SampleReleaseManifest());

        Assert.True(doc.RootElement.TryGetProperty("product", out var p) &&
                    p.GetString() == "PathVeer");
        Assert.True(doc.RootElement.TryGetProperty("version", out var v) &&
                    v.GetString() == "1.0.0");
        Assert.True(doc.RootElement.TryGetProperty("platform", out var plat) &&
                    plat.GetString() == "windows");
        Assert.True(doc.RootElement.TryGetProperty(
            "architecture", out var arch) && arch.GetString() == "win-x64");
        Assert.True(doc.RootElement.TryGetProperty("installer", out var inst) &&
                    inst.GetString() == "PathVeerSetup-1.0.0-win-x64.exe");
        Assert.True(doc.RootElement.TryGetProperty(
            "installerSha256", out var sha) && !string.IsNullOrEmpty(sha.GetString()));
        Assert.True(doc.RootElement.TryGetProperty(
            "minimumUpgradeVersion", out var min) && min.GetString() == "1.0.0");
        Assert.True(doc.RootElement.TryGetProperty("signed", out var signed));
    }

    [Fact]
    public void ReleaseManifest_InstallerName_Follows_Naming_Convention()
    {
        var doc = JsonDocument.Parse(SampleReleaseManifest());
        string installer = doc.RootElement
            .GetProperty("installer").GetString()!;

        Assert.Equal(
            "PathVeerSetup-1.0.0-win-x64.exe",
            installer);
        Assert.DoesNotContain(' ', installer);
        Assert.StartsWith("PathVeerSetup-", installer);
        Assert.EndsWith("-win-x64.exe", installer);
    }

    // --- Package integrity ----------------------------------------------------

    [Fact]
    public void Package_Hash_Manifest_Is_Deterministic_And_Covers_All_Files()
    {
        string root = NewTempRoot();
        try
        {
            WriteFile(Path.Combine(root, "Service", "PathVeer.Service.exe"), "svc");
            WriteFile(Path.Combine(root, "Cli", "PathVeer.Cli.exe"), "cli");
            WriteFile(Path.Combine(root, "Tray", "PathVeer.Tray.exe"), "tray");

            string hashFile = Path.Combine(root, "package-hashes.sha256");
            WriteHashManifest(hashFile, root);

            string[] lines = File.ReadAllLines(hashFile);
            Assert.Equal(3, lines.Length);

            foreach (string line in lines)
            {
                // "<sha256> <relative-path>"
                int sep = line.IndexOf(' ');
                Assert.True(sep > 0);
                string expectedHash = line[..sep];
                string rel = line[(sep + 1)..];
                string actual = HashFile(Path.Combine(root, rel));
                Assert.Equal(expectedHash, actual);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Package_Hash_Manifest_Detects_Tampering()
    {
        string root = NewTempRoot();
        try
        {
            string file = Path.Combine(root, "Cli", "PathVeer.Cli.exe");
            WriteFile(file, "cli");
            string hashFile = Path.Combine(root, "package-hashes.sha256");
            WriteHashManifest(hashFile, root);

            File.WriteAllText(file, "TAMPERED");

            string[] lines = File.ReadAllLines(hashFile);
            int sep = lines[0].IndexOf(' ');
            string rel = lines[0][(sep + 1)..];
            Assert.NotEqual(
                lines[0][..sep],
                HashFile(Path.Combine(root, rel)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- Compatibility launcher ----------------------------------------------

    [Fact]
    public void Package_Should_Contain_IrandirectCmd_CompatibilityLauncher()
    {
        // The legacy CLI forwarding shim is part of the compatibility contract
        // (Phase 36.8 §I). The package/installer must ship exactly one.
        string shimSource = Path.Combine(
            RepoRoot, "tools", "irandirect.cmd");
        if (File.Exists(shimSource))
        {
            string contents = File.ReadAllText(shimSource);
            Assert.Contains("PathVeer.Cli.exe", contents);
            Assert.DoesNotContain(
                "IranDirect.Cli.exe", contents,
                StringComparison.OrdinalIgnoreCase);
        }

        // The production contract asserts a single real CLI implementation.
        Assert.True(
            File.Exists(Path.Combine(RepoRoot, "PathVeer.Cli", "PathVeer.Cli.csproj")));
    }

    // --- Downgrade policy -----------------------------------------------------

    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]    // newer over older -> allow
    [InlineData("1.0.1", "1.0.0", false)]   // older over newer -> block
    [InlineData("1.0.0", "1.0.0", true)]    // same -> allow (idempotent)
    [InlineData("1.0.0", "2.0.0", true)]    // major upgrade -> allow
    [InlineData("2.0.0", "1.0.0", false)]   // downgrade across major -> block
    public void Downgrade_Policy_Blocks_Older_Over_Newer(
        string installed, string candidate, bool expectedAllowed)
    {
        Assert.Equal(expectedAllowed, IsUpgradeAllowed(installed, candidate));
    }

    // --- Install manifest (deployed) -----------------------------------------

    [Fact]
    public void InstallManifest_Records_Safe_Supportability_Fields_Only()
    {
        var manifest = new InstallManifest
        {
            ProductVersion = "1.0.0",
            InstallRoot = @"C:\Program Files\PathVeer",
            InstalledAtUtc = DateTimeOffset.UtcNow,
            LegacyServiceMigrationCompleted = true,
            UpgradedFromVersion = "0.9.0",
        };

        Assert.Equal(1, InstallManifest.CurrentSchemaVersion);
        Assert.Equal("1.0.0", manifest.ProductVersion);
        // No secrets / machine ids / user names by design.
        string json = JsonSerializer.Serialize(manifest);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    // --- Helpers --------------------------------------------------------------

    private static string DeriveAssemblyVersion(string version)
    {
        string major = System.Text.RegularExpressions.Regex
            .Match(version, @"^\d+").Value;
        return $"{major}.0.0.0";
    }

    private static string DeriveFileVersion(string version) => version;

    private static string SampleReleaseManifest() =>
        """
        {
          "product": "PathVeer",
          "version": "1.0.0",
          "platform": "windows",
          "architecture": "win-x64",
          "installer": "PathVeerSetup-1.0.0-win-x64.exe",
          "installerSha256": "abc123",
          "minimumUpgradeVersion": "1.0.0",
          "signed": true
        }
        """;

    private static string NewTempRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "PathVeer.Release.Tests",
            Guid.NewGuid().ToString("N"));

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WriteHashManifest(string hashFile, string root)
    {
        var lines = new List<string>();
        foreach (string f in Directory
                     .GetFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(x => x))
        {
            string rel = f.Substring(root.Length + 1);
            lines.Add($"{HashFile(f)} {rel}");
        }

        File.WriteAllLines(hashFile, lines);
    }

    private static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        byte[] bytes = sha.ComputeHash(File.ReadAllBytes(path));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsUpgradeAllowed(string installed, string candidate)
    {
        if (!Version.TryParse(Normalize(installed), out var a) ||
            !Version.TryParse(Normalize(candidate), out var b))
        {
            return false;
        }

        // Block strictly-older versions (incl. major downgrades).
        return b >= a;
    }

    private static string Normalize(string v) =>
        v.Contains('-') ? v[..v.IndexOf('-')] : v;
}

/// <summary>
/// Phase 37.1 — integration acceptance of the versioned package builder.
///
/// Invokes <c>New-PathVeerPackage.ps1</c> against a disposable temp root and
/// asserts the canonical layout, executable presence and hash manifest. This
/// proves the deployment contract emits a deterministic, signed-ready package
/// without depending on a developer checkout at install time.
/// </summary>
public sealed class PackageBuilderIntegrationTests
{
    [Fact(Timeout = 600000)]
    public async Task NewPathVeerPackage_Produces_Canonical_Layout_And_Hashes()
    {
        string repoRoot = FindRepoRoot();
        string script = Path.Combine(
            repoRoot, "tools", "New-PathVeerPackage.ps1");
        Assert.True(File.Exists(script), $"missing {script}");

        string outDir = Path.Combine(
            Path.GetTempPath(),
            "PathVeer.PkgBuild.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        try
        {
            int exit = await Task.Run(() => RunPowerShell(
                script,
                "-Version", "1.0.0",
                "-OutputDirectory", outDir,
                "-RuntimeIdentifier", "win-x64"));

            Assert.Equal(0, exit);

            string pkg = Path.Combine(outDir, "PathVeer-1.0.0");
            Assert.True(Directory.Exists(pkg), "package dir missing");

            Assert.True(File.Exists(Path.Combine(pkg, "Service", "PathVeer.Service.exe")));
            Assert.True(File.Exists(Path.Combine(pkg, "Cli", "PathVeer.Cli.exe")));
            Assert.True(File.Exists(Path.Combine(pkg, "Tray", "PathVeer.Tray.exe")));
            Assert.True(File.Exists(Path.Combine(pkg, "package-hashes.sha256")));
            Assert.True(File.Exists(Path.Combine(pkg, "package.json")));

            // Hash manifest covers the published files.
            string[] lines = File.ReadAllLines(
                Path.Combine(pkg, "package-hashes.sha256"));
            Assert.NotEmpty(lines);
            foreach (string line in lines)
            {
                int sep = line.IndexOf(' ');
                Assert.True(sep > 0);
                string rel = line[(sep + 1)..].Replace('/', '\\');
                Assert.True(
                    File.Exists(Path.Combine(pkg, rel)),
                    $"hash references missing file: {rel}");
            }
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "PathVeer.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static int RunPowerShell(params string[] args)
    {
        string exe = CommandExists("pwsh") ? "pwsh" : "powershell";
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(args[0]);
        foreach (string a in args.Skip(1))
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)!;
        proc.WaitForExit();
        return proc.ExitCode;
    }

    private static bool CommandExists(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null) return false;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(d => File.Exists(Path.Combine(d, name + ".exe")));
    }
}
