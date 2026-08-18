using System.Security.Cryptography;
using PathVeer.Core.Installation;

namespace PathVeer.Core.Tests.Installation;

/// <summary>
/// Adversarial tests for the package integrity verifier
/// (<see cref="WindowsInstallFileSystem.VerifyPackageAsync"/>).
///
/// The verifier reads package-hashes.sha256 (&lt;hash&gt; &lt;relative-path&gt;) and
/// must FAIL CLOSED on anything that is not a safe, package-root-relative,
/// byte-exact entry. A good package passes; tampered / missing / traversal /
/// rooted / malformed entries all abort before any payload is touched.
///
/// These tests drive the REAL implementation against disposable temp roots; the
/// verifier only reads and hashes, it never mutates the install root, so they
/// are safe to run on a production-like machine.
/// </summary>
public sealed class PackageIntegrityVerifierTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PathVeer.PkgVerify.Tests", Guid.NewGuid().ToString("N"));

    public PackageIntegrityVerifierTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string Hash(string content)
    {
        byte[] bytes = SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Line(string hash, string relative) => $"{hash} {relative}";

    [Fact]
    public void Valid_FinalSignedPackage_Accepts()
    {
        Write(Path.Combine(_root, "Service", "PathVeer.Service.exe"), "svc-bytes");
        Write(Path.Combine(_root, "Cli", "PathVeer.Cli.exe"), "cli-bytes");
        Write(Path.Combine(_root, "Tray", "PathVeer.Tray.exe"), "tray-bytes");
        Write(Path.Combine(_root, "Cli", "clretwrc.dll"), "clr-bytes");

        var lines = new[]
        {
            Line(Hash("svc-bytes"), @"Service\PathVeer.Service.exe"),
            Line(Hash("cli-bytes"), @"Cli\PathVeer.Cli.exe"),
            Line(Hash("tray-bytes"), @"Tray\PathVeer.Tray.exe"),
            Line(Hash("clr-bytes"), @"Cli\clretwrc.dll"),
        };
        File.WriteAllLines(
            Path.Combine(_root, WindowsInstallFileSystem.PackageHashFileName), lines);

        var fs = new WindowsInstallFileSystem();
        Assert.True(
            fs.VerifyPackageAsync(_root).GetAwaiter().GetResult());
    }

    [Fact]
    public void MissingFile_Rejects()
    {
        // Manifest references a file that does not exist.
        var lines = new[] { Line(Hash("x"), @"Cli\PathVeer.Cli.exe") };
        File.WriteAllLines(
            Path.Combine(_root, WindowsInstallFileSystem.PackageHashFileName), lines);

        var fs = new WindowsInstallFileSystem();
        Assert.False(
            fs.VerifyPackageAsync(_root).GetAwaiter().GetResult());
    }

    [Fact]
    public void WrongHash_Rejects()
    {
        string file = Path.Combine(_root, "Cli", "PathVeer.Cli.exe");
        Write(file, "original");
        // Manifest has the wrong hash (tampered file vs manifest).
        var lines = new[]
        {
            Line(Hash("NOT-THE-ACTUAL-CONTENT"), @"Cli\PathVeer.Cli.exe")
        };
        File.WriteAllLines(
            Path.Combine(_root, WindowsInstallFileSystem.PackageHashFileName), lines);

        var fs = new WindowsInstallFileSystem();
        Assert.False(
            fs.VerifyPackageAsync(_root).GetAwaiter().GetResult());
    }

    [Fact]
    public void TamperedFile_Rejects()
    {
        string file = Path.Combine(_root, "Cli", "PathVeer.Cli.exe");
        Write(file, "original-content");
        var lines = new[]
        {
            Line(Hash("original-content"), @"Cli\PathVeer.Cli.exe")
        };
        File.WriteAllText(
            Path.Combine(_root, WindowsInstallFileSystem.PackageHashFileName),
            string.Join('\n', lines));

        // Now tamper the installed file.
        File.WriteAllText(file, "TAMPERED-content");

        var fs = new WindowsInstallFileSystem();
        Assert.False(
            fs.VerifyPackageAsync(_root).GetAwaiter().GetResult());
    }

    [Fact]
    public void TraversalPath_Rejects()
    {
        Write(Path.Combine(_root, "Cli", "PathVeer.Cli.exe"), "cli");
        // Path escapes the package root upward.
        var lines = new[]
        {
            Line(Hash("cli"), @"..\evil\PathVeer.Cli.exe")
        };
        File.WriteAllLines(
            Path.Combine(_root, WindowsInstallFileSystem.PackageHashFileName), lines);

        var fs = new WindowsInstallFileSystem();
        Assert.False(
            fs.VerifyPackageAsync(_root).GetAwaiter().GetResult());
    }

    [Fact]
    public void RootedPath_Rejects()
    {
        Write(Path.Combine(_root, "Cli", "PathVeer.Cli.exe"), "cli");
        var lines = new[]
        {
            Line(Hash("cli"), @"C:\Windows\System32\PathVeer.Cli.exe")
        };
        File.WriteAllLines(
            Path.Combine(_root, WindowsInstallFileSystem.PackageHashFileName), lines);

        var fs = new WindowsInstallFileSystem();
        Assert.False(
            fs.VerifyPackageAsync(_root).GetAwaiter().GetResult());
    }

    [Fact]
    public void MalformedLine_Rejects()
    {
        Write(Path.Combine(_root, "Cli", "PathVeer.Cli.exe"), "cli");
        // Only one token; the verifier requires exactly hash + path.
        File.WriteAllLines(
            Path.Combine(_root, WindowsInstallFileSystem.PackageHashFileName),
            new[] { "only-one-token" });

        var fs = new WindowsInstallFileSystem();
        Assert.False(
            fs.VerifyPackageAsync(_root).GetAwaiter().GetResult());
    }

    [Fact]
    public void MissingManifest_Rejects()
    {
        // No package-hashes.sha256 at all -> cannot make an integrity claim.
        var fs = new WindowsInstallFileSystem();
        Assert.False(
            fs.VerifyPackageAsync(_root).GetAwaiter().GetResult());
    }

    [Fact]
    public void DuplicateEntry_ValidSameFile_Accepts()
    {
        Write(Path.Combine(_root, "Cli", "PathVeer.Cli.exe"), "cli");
        string h = Hash("cli");
        var lines = new[]
        {
            Line(h, @"Cli\PathVeer.Cli.exe"),
            Line(h, @"Cli\PathVeer.Cli.exe"),
        };
        File.WriteAllLines(
            Path.Combine(_root, WindowsInstallFileSystem.PackageHashFileName), lines);

        var fs = new WindowsInstallFileSystem();
        Assert.True(
            fs.VerifyPackageAsync(_root).GetAwaiter().GetResult());
    }
}
