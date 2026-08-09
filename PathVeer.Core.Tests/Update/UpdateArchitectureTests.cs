using PathVeer.Core.Update;
using System.Diagnostics;
using System.Security.Cryptography;
using Xunit;

namespace PathVeer.Core.Tests.Update;

public sealed class UpdateArchitectureTests
{
    private const string ValidSha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static (string keyId, ECParameters priv, byte[] pub) NewKey(string id = "pv-meta-2026") =>
        ReleaseManifestSigner.Generate(id);

    private static string BuildManifest(string version, string channel = "stable",
        string? minUpgrade = null, string sha = ValidSha, long size = 100,
        string url = "https://releases.pathveer.com/PathVeerSetup.exe", int schema = 1)
    {
        var m = new ReleaseManifest
        {
            SchemaVersion = schema,
            Product = "PathVeer",
            Version = version,
            Channel = channel,
            Platform = "windows",
            Architecture = "x64",
            PublishedAtUtc = "2026-08-09T12:00:00Z",
            MinimumUpgradeVersion = minUpgrade,
            Installer = new ReleaseManifest.ManifestArtifact
            {
                FileName = $"PathVeerSetup-{version}-win-x64.exe",
                Url = url,
                Sha256 = sha,
                Size = size
            },
            PackageArchive = new ReleaseManifest.ManifestArtifact
            {
                FileName = $"PathVeer-{version}-win-x64.zip",
                Url = $"https://releases.pathveer.com/PathVeer-{version}-win-x64.zip",
                Sha256 = sha,
                Size = size
            },
            Signed = false
        };
        return m.ToJson();
    }

    private static string WriteTemp(string json)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pvman_" + Guid.NewGuid().ToString("N") + ".json");
        System.IO.File.WriteAllText(path, json);
        return path;
    }

    private static InstalledVersionSource InstalledTemp(string version)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pvinst_" + Guid.NewGuid().ToString("N") + ".json");
        System.IO.File.WriteAllText(path, "{\"productVersion\":\"" + version + "\"}");
        return new InstalledVersionSource(path);
    }

    [Fact]
    public void Parse_ValidManifest_Succeeds()
    {
        var json = BuildManifest("1.2.3");
        var r = new ReleaseManifestParser().Parse(json);
        Assert.True(r.Success);
        Assert.Equal("1.2.3", r.Manifest!.Version);
    }

    [Fact]
    public void Parse_MissingRequiredField_Fails()
    {
        var json = BuildManifest("1.2.3").Replace("\"fileName\"", "\"fileX\"");
        var r = new ReleaseManifestParser().Parse(json);
        Assert.False(r.Success);
    }

    [Fact]
    public void Parse_MalformedVersion_Fails()
    {
        var json = BuildManifest("not.a.version");
        var r = new ReleaseManifestParser().Parse(json);
        Assert.False(r.Success);
    }

    [Fact]
    public void Parse_WrongProduct_Fails()
    {
        var json = BuildManifest("1.0.0").Replace("\"PathVeer\"", "\"Other\"");
        var r = new ReleaseManifestParser().Parse(json);
        Assert.False(r.Success);
        Assert.Contains("Wrong product", r.Error ?? "");
    }

    [Fact]
    public void Parse_WrongPlatform_Fails()
    {
        var json = BuildManifest("1.0.0").Replace("\"windows\"", "\"linux\"");
        var r = new ReleaseManifestParser().Parse(json);
        Assert.False(r.Success);
    }

    [Fact]
    public void Parse_WrongArchitecture_Fails()
    {
        var json = BuildManifest("1.0.0").Replace("\"x64\"", "\"arm64\"");
        var r = new ReleaseManifestParser().Parse(json);
        Assert.False(r.Success);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    public void Parse_UnknownSchemaVersion_Fails(int schema)
    {
        var json = BuildManifest("1.0.0", schema: schema);
        var r = new ReleaseManifestParser().Parse(json);
        Assert.False(r.Success);
    }

    [Fact]
    public void Parse_UnsafeHttpProdUrl_Rejected()
    {
        var json = BuildManifest("1.0.0", url: "http://evil.example.com/x.exe");
        var r = new ReleaseManifestParser().Parse(json);
        Assert.False(r.Success);
    }

    [Fact]
    public void Parse_LocalhostHttpUrl_AcceptedForTests()
    {
        var json = BuildManifest("1.0.0", url: "http://localhost/x.exe");
        var r = new ReleaseManifestParser().Parse(json);
        Assert.True(r.Success);
    }

    [Fact]
    public void Signature_OriginalManifest_Valid()
    {
        var (id, priv, pub) = NewKey();
        var json = BuildManifest("1.0.0");
        var signed = new ReleaseManifestSigner(id, priv).Sign(json);
        var verifier = new ReleaseSignatureVerifier(new Dictionary<string, byte[]> { [id] = pub });
        var parsed = new ReleaseManifestParser().Parse(signed)!.Manifest!;
        var res = verifier.Verify(parsed, signed);
        Assert.True(res.IsValid);
    }

    [Fact]
    public void Signature_OneByteTamper_Invalid()
    {
        var (id, priv, pub) = NewKey();
        var json = BuildManifest("1.0.0");
        var signed = new ReleaseManifestSigner(id, priv).Sign(json);
        var tampered = signed.Replace("1.0.0", "1.0.1"); // version substring
        var verifier = new ReleaseSignatureVerifier(new Dictionary<string, byte[]> { [id] = pub });
        var parsed = new ReleaseManifestParser().Parse(tampered)!.Manifest!;
        var res = verifier.Verify(parsed, tampered);
        Assert.False(res.IsValid);
    }

    [Fact]
    public void Signature_WrongKey_Invalid()
    {
        var (_, priv, _) = NewKey("real");
        var (id2, _, pub2) = NewKey("other");
        var json = BuildManifest("1.0.0");
        var signed = new ReleaseManifestSigner("real", priv).Sign(json);
        var verifier = new ReleaseSignatureVerifier(new Dictionary<string, byte[]> { [id2] = pub2 });
        var parsed = new ReleaseManifestParser().Parse(signed)!.Manifest!;
        var res = verifier.Verify(parsed, signed);
        Assert.False(res.IsValid);
    }

    [Fact]
    public void Signature_Truncated_Invalid()
    {
        var (id, priv, pub) = NewKey();
        var json = BuildManifest("1.0.0");
        var signed = new ReleaseManifestSigner(id, priv).Sign(json);
        var parsed = new ReleaseManifestParser().Parse(signed)!.Manifest!;
        parsed.Signature!.Value = parsed.Signature.Value[..^4];
        var verifier = new ReleaseSignatureVerifier(new Dictionary<string, byte[]> { [id] = pub });
        var res = verifier.Verify(parsed, signed);
        Assert.False(res.IsValid);
    }

    [Fact]
    public void Signature_Unsigned_RejectedInProdMode()
    {
        var json = BuildManifest("1.0.0");
        var parsed = new ReleaseManifestParser().Parse(json)!.Manifest!;
        var verifier = new ReleaseSignatureVerifier(new Dictionary<string, byte[]> { ["k"] = new byte[64] });
        var res = verifier.Verify(parsed, json);
        Assert.False(res.IsValid);
        Assert.False(res.IsUnsigned);
    }

    [Fact]
    public void Signature_Unsigned_AllowedInDevMode()
    {
        var json = BuildManifest("1.0.0");
        var parsed = new ReleaseManifestParser().Parse(json)!.Manifest!;
        var verifier = new ReleaseSignatureVerifier(new Dictionary<string, byte[]> { ["k"] = new byte[64] }, allowUnsigned: true);
        var res = verifier.Verify(parsed, json);
        Assert.True(res.IsUnsigned);
    }

    [Theory]
    [InlineData("1.0.0-beta.1", "1.0.0-rc.1", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("2.0.0", "1.9.9", 1)]
    public void SemanticVersion_ComparesPrerelease(string a, string b, int expected)
    {
        SemanticVersion.TryParse(a, out var va);
        SemanticVersion.TryParse(b, out var vb);
        var cmp = va.CompareTo(vb);
        var sign = cmp == 0 ? 0 : (cmp < 0 ? -1 : 1);
        Assert.Equal(expected, sign);
    }

    [Fact]
    public void Channel_Stable_IgnoresBeta()
    {
        var beta = BuildManifest("1.1.0-beta.1", channel: "beta");
        Assert.False(ReleaseChannelPolicy.IsEligible(UpdateChannel.Stable,
            new ReleaseManifestParser().Parse(beta)!.Manifest!));
    }

    [Fact]
    public void Channel_Beta_AcceptsStableAndBeta()
    {
        var stable = BuildManifest("1.0.0", channel: "stable");
        var beta = BuildManifest("1.1.0-beta.1", channel: "beta");
        Assert.True(ReleaseChannelPolicy.IsEligible(UpdateChannel.Beta,
            new ReleaseManifestParser().Parse(stable)!.Manifest!));
        Assert.True(ReleaseChannelPolicy.IsEligible(UpdateChannel.Beta,
            new ReleaseManifestParser().Parse(beta)!.Manifest!));
    }

    [Fact]
    public void Checker_UpdateAvailable()
    {
        var (id, priv, pub) = NewKey();
        var json = new ReleaseManifestSigner(id, priv).Sign(BuildManifest("1.1.0"));
        var source = new LocalFileReleaseSource(WriteTemp(json));
        var verifier = new ReleaseSignatureVerifier(new Dictionary<string, byte[]> { [id] = pub });
        var installed = InstalledTemp("1.0.0");
        var checker = new UpdateChecker(source, verifier, installed);
        var res = checker.CheckAsync(UpdateChannel.Stable).GetAwaiter().GetResult();
        Assert.Equal(UpdateCheckState.UpdateAvailable, res.State);
    }

    [Fact]
    public void Checker_SameVersion_NoUpdate()
    {
        var (id, priv, pub) = NewKey();
        var json = new ReleaseManifestSigner(id, priv).Sign(BuildManifest("1.0.0"));
        var source = new LocalFileReleaseSource(WriteTemp(json));
        var verifier = new ReleaseSignatureVerifier(new Dictionary<string, byte[]> { [id] = pub });
        var installed = InstalledTemp("1.0.0");
        var checker = new UpdateChecker(source, verifier, installed);
        var res = checker.CheckAsync(UpdateChannel.Stable).GetAwaiter().GetResult();
        Assert.Equal(UpdateCheckState.NoUpdate, res.State);
    }

    [Fact]
    public void Checker_InstalledNewerThanFeed_CurrentNewer()
    {
        var (id, priv, pub) = NewKey();
        var json = new ReleaseManifestSigner(id, priv).Sign(BuildManifest("1.0.0"));
        var source = new LocalFileReleaseSource(WriteTemp(json));
        var verifier = new ReleaseSignatureVerifier(new Dictionary<string, byte[]> { [id] = pub });
        var installed = InstalledTemp("1.1.0");
        var checker = new UpdateChecker(source, verifier, installed);
        var res = checker.CheckAsync(UpdateChannel.Stable).GetAwaiter().GetResult();
        Assert.Equal(UpdateCheckState.CurrentVersionNewer, res.State);
    }

    [Fact]
    public void Checker_InvalidSignature_Fails()
    {
        var (_, priv, _) = NewKey("real");
        var (id2, _, pub2) = NewKey("other");
        var json = new ReleaseManifestSigner("real", priv).Sign(BuildManifest("1.1.0"));
        var source = new LocalFileReleaseSource(WriteTemp(json));
        var verifier = new ReleaseSignatureVerifier(new Dictionary<string, byte[]> { [id2] = pub2 });
        var installed = InstalledTemp("1.0.0");
        var checker = new UpdateChecker(source, verifier, installed);
        var res = checker.CheckAsync(UpdateChannel.Stable).GetAwaiter().GetResult();
        Assert.Equal(UpdateCheckState.InvalidSignature, res.State);
    }

    [Fact]
    public void Checker_MinimumUpgradeFloor_Blocks()
    {
        var (id, priv, pub) = NewKey();
        var json = new ReleaseManifestSigner(id, priv).Sign(BuildManifest("1.5.0", minUpgrade: "1.2.0"));
        var source = new LocalFileReleaseSource(WriteTemp(json));
        var verifier = new ReleaseSignatureVerifier(new Dictionary<string, byte[]> { [id] = pub });
        var installed = InstalledTemp("1.0.0");
        var checker = new UpdateChecker(source, verifier, installed);
        var res = checker.CheckAsync(UpdateChannel.Stable).GetAwaiter().GetResult();
        Assert.Equal(UpdateCheckState.MinimumUpgradeNotMet, res.State);
    }

    [Fact]
    public void Download_HashValid_Accepted()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pvtest_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var file = System.IO.Path.Combine(dir, "setup.exe");
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            System.IO.File.WriteAllBytes(file, bytes);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var artifact = new ReleaseManifest.ManifestArtifact { FileName = "setup.exe", Sha256 = sha, Size = bytes.Length };
            var res = new InstallerDownloadVerifier().Verify(file, artifact);
            Assert.True(res.IsValid);
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    [Fact]
    public void Download_HashMismatch_Rejected()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pvtest_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var file = System.IO.Path.Combine(dir, "setup.exe");
            System.IO.File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            var artifact = new ReleaseManifest.ManifestArtifact { FileName = "setup.exe", Sha256 = "deadbeef", Size = 3 };
            var res = new InstallerDownloadVerifier().Verify(file, artifact);
            Assert.False(res.IsValid);
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    [Fact]
    public void Download_SizeMismatch_Rejected()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pvtest_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var file = System.IO.Path.Combine(dir, "setup.exe");
            System.IO.File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            var sha = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })).ToLowerInvariant();
            var artifact = new ReleaseManifest.ManifestArtifact { FileName = "setup.exe", Sha256 = sha, Size = 999 };
            var res = new InstallerDownloadVerifier().Verify(file, artifact);
            Assert.False(res.IsValid);
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    [Fact]
    public void Staging_PartialNeverFinal()
    {
        var staging = new UpdateStagingPaths(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pvupd_" + Guid.NewGuid().ToString("N")));
        var ver = "1.0.0";
        staging.EnsureVersionDirectory(ver);
        var partial = staging.PartialPath(ver, "setup.exe");
        System.IO.File.WriteAllText(partial, "partial");
        Assert.False(System.IO.File.Exists(staging.FinalPath(ver, "setup.exe")));
        staging.CleanPartial(ver, "setup.exe");
        Assert.False(System.IO.File.Exists(partial));
    }

    [Fact]
    public void Staging_SanitizesVersionDir()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pvupd_" + Guid.NewGuid().ToString("N"));
        var staging = new UpdateStagingPaths(root);
        var d = staging.VersionDirectory("1.0.0/../evil");
        // No path traversal: the resolved full path must stay inside the updates
        // root (the sanitizer strips path separators, so "1.0.0/../evil" cannot
        // escape the root even though the sanitized name may contain literal dots).
        var full = System.IO.Path.GetFullPath(d).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var rootFull = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        Assert.StartsWith(rootFull, full);
    }

    [Fact]
    public void ManifestSigning_PowerShellSigned_VerifiesInCSharp()
    {
        // Generates an ephemeral P-256 key, signs a fixture manifest with the
        // PowerShell Sign-ReleaseManifest.ps1 (the real release pipeline signer),
        // then verifies the signature with the C# client verifier. Proves the
        // canonicalization + ECDsa P-256 signing agree across languages.
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var priv = ecdsa.ExportParameters(true);
        var qxqy = new byte[64];
        Array.Copy(priv.Q!.X!, 0, qxqy, 0, 32);
        Array.Copy(priv.Q!.Y!, 0, qxqy, 32, 32);
        var keyBlob = new byte[96];
        Array.Copy(qxqy, 0, keyBlob, 0, 64);
        Array.Copy(priv.D!, 0, keyBlob, 64, 32);
        var keyB64 = Convert.ToBase64String(keyBlob);

        var manifest = BuildManifest("1.4.2", channel: "beta");
        var manifestPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pvsign_" + Guid.NewGuid().ToString("N") + ".json");
        System.IO.File.WriteAllText(manifestPath, manifest);

        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoLogo -NoProfile -File tools/Sign-ReleaseManifest.ps1 -ManifestPath \"{manifestPath}\" -KeyId pv-meta-2026 -FailIfUnavailable",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = "C:\\codespace\\PathVeer"
        };
        psi.Environment["PATHVEER_META_SIGN_KEY"] = keyB64;
        using var proc = Process.Start(psi)!;
        var errOut = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            throw new Exception($"Signer exit {proc.ExitCode}: {errOut}");
        }

        var signed = System.IO.File.ReadAllText(manifestPath);
        var parsed = new ReleaseManifestParser().Parse(signed)!.Manifest!;
        var verifier = new ReleaseSignatureVerifier(new Dictionary<string, byte[]> { ["pv-meta-2026"] = qxqy });
        var res = verifier.Verify(parsed, signed);
        Assert.True(res.IsValid, res.Error ?? "verify failed");
    }
}
