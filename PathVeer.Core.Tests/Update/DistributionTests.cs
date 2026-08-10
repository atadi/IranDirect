namespace PathVeer.Core.Tests.Update;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PathVeer.Core.Update;
using Xunit;

/// <summary>
/// Phase 37.4 — distribution surface tests.
///
/// These cover the release publisher (Publish-PathVeerRelease.ps1) and the
/// end-to-end client feed: a frozen bundle is published to a local static
/// origin, served over real HTTP, discovered by HttpReleaseSource + UpdateChecker,
/// the installer downloaded and verified by InstallerDownloadVerifier. No real
/// network, no Setup launch.
/// </summary>
public sealed class DistributionTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ToolsDir = Path.Combine(RepoRoot, "tools");

    // xUnit creates a fresh instance per test, so this runs before every test and
    // guarantees no stale temp state from a previous test in the same run bleeds in.
    public DistributionTests() => CleanStaleTempDirs();

    private static void CleanStaleTempDirs()
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(Path.GetTempPath(), "pv-*"))
            {
                try { Directory.Delete(d, recursive: true); } catch { }
            }
        }
        catch { }
    }

    private static (string keyId, ECParameters priv, byte[] pub) MakeKey(string keyId = "pv-test-2026")
        => ReleaseManifestSigner.Generate(keyId);

    /// <summary>96-byte X|Y|D private-blob base64 (what the PS verify gate accepts on this SDK).</summary>
    private static string PrivateBlobBase64(ECParameters priv)
    {
        var x = new byte[32]; Array.Copy(priv.Q.X!, 0, x, 0, 32);
        var y = new byte[32]; Array.Copy(priv.Q.Y!, 0, y, 0, 32);
        var d = new byte[32]; Array.Copy(priv.D!, 0, d, 0, 32);
        var blob = new byte[96];
        Array.Copy(x, 0, blob, 0, 32);
        Array.Copy(y, 0, blob, 32, 32);
        Array.Copy(d, 0, blob, 64, 32);
        return Convert.ToBase64String(blob);
    }

    private sealed record Bundle(string Dir, string Version, byte[] InstallerBytes, string ManifestPath, byte[] PubKey, string KeyId, ECParameters Priv);

    /// <summary>Creates a frozen release bundle (fake installer + signed manifest) with a
    /// localhost base URL, so the static server can serve it and the client can fetch it.</summary>
    private static Bundle MakeBundle(string version, int port, (string keyId, ECParameters priv, byte[] pub) key, string channel = "stable")
    {
        var dir = Path.Combine(Path.GetTempPath(), "pv-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var installerBytes = new byte[2048];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(installerBytes);
        var installerName = $"PathVeerSetup-{version}-win-x64.exe";
        var installerPath = Path.Combine(dir, installerName);
        File.WriteAllBytes(installerPath, installerBytes);
        var hash = Convert.ToHexString(SHA256.HashData(installerBytes)).ToLowerInvariant();

        var baseUrl = $"http://127.0.0.1:{port}";
        var m = new ReleaseManifest
        {
            Version = version,
            Channel = channel,
            Architecture = "x64",
            PublishedAtUtc = DateTime.UtcNow.ToString("o"),
            MinimumUpgradeVersion = version.StartsWith("0.") ? "0.0.0" : "1.0.0",
            Installer = new ReleaseManifest.ManifestArtifact
            {
                FileName = installerName,
                Url = $"{baseUrl}/windows/{version}/win-x64/{installerName}",
                Sha256 = hash,
                Size = installerBytes.Length
            }
        };
        // Sign with the PowerShell signer so the publisher's PS verify gate and the C#
        // client verifier both operate on the identical PS canonical form. (37.3 parity
        // test ManifestSigning_PowerShellSigned_VerifiesInCSharp proves PS-signed
        // manifests verify in C#.)
        var manifestPath = Path.Combine(dir, "release-manifest.json");
        var unsigned = m.ToJson().Replace("\"signed\":true", "\"signed\":false");
        File.WriteAllText(manifestPath, unsigned);
        var signArgs = $"-NoLogo -NoProfile -File \"{Path.Combine(ToolsDir, "Sign-ReleaseManifest.ps1")}\" " +
                       $"-ManifestPath \"{manifestPath}\" -KeyId {key.keyId}";
        var signPsi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = signArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment =
            {
                ["PATHVEER_META_SIGN_KEY"] = PrivateBlobBase64(key.priv)
            }
        };
        using (var sp = Process.Start(signPsi)!)
        {
            var so = sp.StandardOutput.ReadToEndAsync();
            var se = sp.StandardError.ReadToEndAsync();
            sp.WaitForExit();
            Task.WaitAll(so, se);
            if (sp.ExitCode != 0)
                throw new Exception($"Sign failed: {so.Result}\n{se.Result}");
        }
        return new Bundle(dir, version, installerBytes, manifestPath, key.pub, key.keyId, key.priv);
    }

    private static int RunPublisher(Bundle bundle, string channel, string env, string publishRoot, string publicBaseUrl, bool whatIf = false)
    {
        var trustedArg = $"{bundle.KeyId}:{PrivateBlobBase64(bundle.Priv)}";
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoLogo -NoProfile -File \"{Path.Combine(ToolsDir, "Publish-PathVeerRelease.ps1")}\" " +
                        $"-ReleaseDirectory \"{bundle.Dir}\" -Channel {channel} -Environment {env} " +
                        $"-Backend Local -PublishRoot \"{publishRoot}\" -PublicBaseUrl \"{publicBaseUrl}\" " +
                        $"-TrustedKeyBase64 \"{trustedArg}\"" + (whatIf ? " -WhatIf" : ""),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        // Read stdout/stderr concurrently to avoid the classic pipe-buffer deadlock.
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        Task.WaitAll(so, se);
        if (p.ExitCode != 0)
            throw new Exception($"Publisher exited {p.ExitCode}.\nSTDOUT:\n{so.Result}\nSTDERR:\n{se.Result}");
        return p.ExitCode;
    }

    // --- Publisher unit / gating behavior -------------------------------------

    [Fact]
    public void Publisher_DryRun_DoesNotWriteObjects()
    {
        var key = MakeKey();
        var pubRoot = Path.Combine(Path.GetTempPath(), "pv-dist-" + Guid.NewGuid().ToString("N"));
        using var server = new LocalStaticHttpServer(pubRoot);
        var bundle = MakeBundle("1.0.0", server.Port, key);

        Assert.Equal(0, RunPublisher(bundle, "stable", "staging", pubRoot, server.BaseUrl, whatIf: true));
        Assert.False(File.Exists(Path.Combine(pubRoot, "windows", "stable", "latest.json")));
        server.Dispose();
    }

    [Fact]
    public void Publisher_RejectsUnsignedInStagingWithoutSignature()
    {
        var key = MakeKey();
        var pubRoot = Path.Combine(Path.GetTempPath(), "pv-dist-" + Guid.NewGuid().ToString("N"));
        using var server = new LocalStaticHttpServer(pubRoot);
        var bundle = MakeBundle("1.0.0", server.Port, key);
        // Strip the signature to simulate an unsigned manifest.
        var doc = JsonNode.Parse(File.ReadAllText(bundle.ManifestPath))!.AsObject();
        doc.Remove("signature"); doc["signed"] = false;
        File.WriteAllText(bundle.ManifestPath, doc.ToString());

        Assert.Throws<Exception>(() => RunPublisher(bundle, "stable", "staging", pubRoot, server.BaseUrl));
        server.Dispose();
    }

    [Fact]
    public void Publisher_SameVersionSameBytes_Idempotent()
    {
        var key = MakeKey();
        var pubRoot = Path.Combine(Path.GetTempPath(), "pv-dist-" + Guid.NewGuid().ToString("N"));
        using var server = new LocalStaticHttpServer(pubRoot);
        var bundle = MakeBundle("1.0.0", server.Port, key);

        Assert.Equal(0, RunPublisher(bundle, "stable", "staging", pubRoot, server.BaseUrl));
        // Re-publishing identical bytes must be a clean no-op (exit 0).
        Assert.Equal(0, RunPublisher(bundle, "stable", "staging", pubRoot, server.BaseUrl));
        server.Dispose();
    }

    [Fact]
    public void Publisher_SameVersionDifferentBytes_Rejected()
    {
        var key = MakeKey();
        var pubRoot = Path.Combine(Path.GetTempPath(), "pv-dist-" + Guid.NewGuid().ToString("N"));
        using var server = new LocalStaticHttpServer(pubRoot);
        var bundle = MakeBundle("1.0.0", server.Port, key);
        Assert.Equal(0, RunPublisher(bundle, "stable", "staging", pubRoot, server.BaseUrl));

        // Second bundle: same version, different installer bytes -> overwrite rejected.
        var bundle2 = MakeBundle("1.0.0", server.Port, key);
        Assert.Throws<Exception>(() => RunPublisher(bundle2, "stable", "staging", pubRoot, server.BaseUrl));
        server.Dispose();
    }

    [Fact]
    public void Publisher_RejectsProductionWithoutConfirm()
    {
        var key = MakeKey();
        var pubRoot = Path.Combine(Path.GetTempPath(), "pv-dist-" + Guid.NewGuid().ToString("N"));
        using var server = new LocalStaticHttpServer(pubRoot);
        var bundle = MakeBundle("1.0.0", server.Port, key);

        var trustedArg = $"{bundle.KeyId}:{PrivateBlobBase64(bundle.Priv)}";
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoLogo -NoProfile -File \"{Path.Combine(ToolsDir, "Publish-PathVeerRelease.ps1")}\" " +
                        $"-ReleaseDirectory \"{bundle.Dir}\" -Channel stable -Environment Production " +
                        $"-Backend Local -PublishRoot \"{pubRoot}\" -PublicBaseUrl \"{server.BaseUrl}\" " +
                        $"-TrustedKeyBase64 \"{trustedArg}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        Task.WaitAll(so, se);
        // Production requires -ConfirmProduction; without it the publisher must refuse.
        Assert.NotEqual(0, p.ExitCode);
        server.Dispose();
    }

    // --- End-to-end feed (real HTTP, static origin) ---------------------------

    [Fact]
    public void Feed_UpdateCheck_FindsUpdate_AndVerifiesInstaller()
    {
        var key = MakeKey();
        var pubRoot = Path.Combine(Path.GetTempPath(), "pv-dist-" + Guid.NewGuid().ToString("N"));
        using var server = new LocalStaticHttpServer(pubRoot);

        var bundle = MakeBundle("1.0.1", server.Port, key);
        Assert.Equal(0, RunPublisher(bundle, "stable", "staging", pubRoot, server.BaseUrl));

        var installedPath = Path.Combine(Path.GetTempPath(), "pv-install-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(installedPath, "{\"productVersion\":\"1.0.0\"}");

        using var http = new HttpClient();
        try
        {
        var source = new HttpReleaseSource(http, (ch, plat, arch) => $"{server.BaseUrl}/windows/stable/latest.json");
        var verifier = new ReleaseSignatureVerifier(new[] { new KeyValuePair<string, byte[]>(bundle.KeyId, bundle.PubKey) });
        var checker = new UpdateChecker(source, verifier, new InstalledVersionSource(installedPath));
        var result = checker.CheckAsync(UpdateChannel.Stable).GetAwaiter().GetResult();
        Assert.Equal(UpdateCheckState.UpdateAvailable, result.State);
        Assert.NotNull(result.Manifest);

        // Download + verify installer.
        var bytes = http.GetByteArrayAsync(result.Manifest!.Installer!.Url).GetAwaiter().GetResult();
        var dl = Path.Combine(Path.GetTempPath(), "pv-dl-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(dl, bytes);
        var verify = new InstallerDownloadVerifier().Verify(dl, result.Manifest.Installer!);
        Assert.True(verify.IsValid, $"installer verify: {verify.Error}");
        File.Delete(dl);
        } catch { throw; }
        server.Dispose();
    }

    [Fact]
    public void Feed_TamperedInstaller_RejectedByHash()
    {
        var key = MakeKey();
        var pubRoot = Path.Combine(Path.GetTempPath(), "pv-dist-" + Guid.NewGuid().ToString("N"));
        using var server = new LocalStaticHttpServer(pubRoot);

        var bundle = MakeBundle("1.0.1", server.Port, key);
        Assert.Equal(0, RunPublisher(bundle, "stable", "staging", pubRoot, server.BaseUrl));

        // Corrupt the published installer bytes.
        var installerPath = Path.Combine(pubRoot, "windows", "1.0.1", "win-x64", $"PathVeerSetup-1.0.1-win-x64.exe");
        var tampered = new byte[2048];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(tampered);
        File.WriteAllBytes(installerPath, tampered);

        var installedPath = Path.Combine(Path.GetTempPath(), "pv-install-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(installedPath, "{\"productVersion\":\"1.0.0\"}");

        using var http = new HttpClient();
        var source = new HttpReleaseSource(http, (ch, plat, arch) => $"{server.BaseUrl}/windows/stable/latest.json");
        var verifier = new ReleaseSignatureVerifier(new[] { new KeyValuePair<string, byte[]>(bundle.KeyId, bundle.PubKey) });
        var checker = new UpdateChecker(source, verifier, new InstalledVersionSource(installedPath));
        var result = checker.CheckAsync(UpdateChannel.Stable).GetAwaiter().GetResult();
        Assert.Equal(UpdateCheckState.UpdateAvailable, result.State);

        var bytes = http.GetByteArrayAsync(result.Manifest!.Installer!.Url).GetAwaiter().GetResult();
        var dl = Path.Combine(Path.GetTempPath(), "pv-dl-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(dl, bytes);
        var verify = new InstallerDownloadVerifier().Verify(dl, result.Manifest.Installer);
        Assert.False(verify.IsValid);
        File.Delete(dl);

        server.Dispose();
    }

    [Fact]
    public void Feed_TamperedManifest_RejectedBySignature()
    {
        var key = MakeKey();
        var pubRoot = Path.Combine(Path.GetTempPath(), "pv-dist-" + Guid.NewGuid().ToString("N"));
        using var server = new LocalStaticHttpServer(pubRoot);

        var bundle = MakeBundle("1.0.1", server.Port, key);
        Assert.Equal(0, RunPublisher(bundle, "stable", "staging", pubRoot, server.BaseUrl));

        // Overwrite the published latest.json with a tampered (re-versioned) manifest.
        var latestPath = Path.Combine(pubRoot, "windows", "stable", "latest.json");
        var doc = JsonNode.Parse(File.ReadAllText(latestPath))!.AsObject();
        doc["version"] = "9.9.9";
        File.WriteAllText(latestPath, doc.ToString());

        var installedPath = Path.Combine(Path.GetTempPath(), "pv-install-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(installedPath, "{\"productVersion\":\"1.0.0\"}");

        using var http = new HttpClient();
        var source = new HttpReleaseSource(http, (ch, plat, arch) => $"{server.BaseUrl}/windows/stable/latest.json");
        var verifier = new ReleaseSignatureVerifier(new[] { new KeyValuePair<string, byte[]>(bundle.KeyId, bundle.PubKey) });
        var checker = new UpdateChecker(source, verifier, new InstalledVersionSource(installedPath));
        var result = checker.CheckAsync(UpdateChannel.Stable).GetAwaiter().GetResult();
        Assert.Equal(UpdateCheckState.InvalidSignature, result.State);

        server.Dispose();
    }

    [Fact]
    public void Feed_StaleLatest_ClientNewer_NoDowngrade()
    {
        var key = MakeKey();
        var pubRoot = Path.Combine(Path.GetTempPath(), "pv-dist-" + Guid.NewGuid().ToString("N"));
        using var server = new LocalStaticHttpServer(pubRoot);

        var bundle = MakeBundle("0.9.0", server.Port, key);
        Assert.Equal(0, RunPublisher(bundle, "stable", "staging", pubRoot, server.BaseUrl));

        var installedPath = Path.Combine(Path.GetTempPath(), "pv-install-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(installedPath, "{\"productVersion\":\"1.0.0\"}");

        using var http = new HttpClient();
        var source = new HttpReleaseSource(http, (ch, plat, arch) => $"{server.BaseUrl}/windows/stable/latest.json");
        var verifier = new ReleaseSignatureVerifier(new[] { new KeyValuePair<string, byte[]>(bundle.KeyId, bundle.PubKey) });
        var checker = new UpdateChecker(source, verifier, new InstalledVersionSource(installedPath));
        var result = checker.CheckAsync(UpdateChannel.Stable).GetAwaiter().GetResult();
        Assert.Equal(UpdateCheckState.CurrentVersionNewer, result.State);

        server.Dispose();
    }

    [Fact]
    public void Feed_BetaPublish_DoesNotMutateStablePointer()
    {
        var key = MakeKey();
        var pubRoot = Path.Combine(Path.GetTempPath(), "pv-dist-" + Guid.NewGuid().ToString("N"));
        using var server = new LocalStaticHttpServer(pubRoot);

        var stable = MakeBundle("1.0.0", server.Port, key);
        Assert.Equal(0, RunPublisher(stable, "stable", "staging", pubRoot, server.BaseUrl));

        // Publish beta 1.1.0-beta.1 — must NOT change stable/latest.json.
        var beta = MakeBundle("1.1.0-beta.1", server.Port, key, "beta");
        Assert.Equal(0, RunPublisher(beta, "beta", "staging", pubRoot, server.BaseUrl));

        // Channel-isolation claim: stable/latest still points at 1.0.0 and
        // beta/latest points at 1.1.0-beta.1. Assert the parsed version value
        // (formatting-independent) rather than a compact-JSON substring, since
        // the published manifest is emitted indented by the PowerShell signer.
        var stableLatest = JsonNode.Parse(File.ReadAllText(Path.Combine(pubRoot, "windows", "stable", "latest.json")))!;
        Assert.Equal("1.0.0", (string?)stableLatest["version"]);
        var betaLatest = JsonNode.Parse(File.ReadAllText(Path.Combine(pubRoot, "windows", "beta", "latest.json")))!;
        Assert.Equal("1.1.0-beta.1", (string?)betaLatest["version"]);

        server.Dispose();
    }

    [Fact]
    public void Feed_StableClient_IgnoresBetaPointer()
    {
        var key = MakeKey();
        var pubRoot = Path.Combine(Path.GetTempPath(), "pv-dist-" + Guid.NewGuid().ToString("N"));
        using var server = new LocalStaticHttpServer(pubRoot);

        var beta = MakeBundle("1.1.0-beta.1", server.Port, key, "beta");
        Assert.Equal(0, RunPublisher(beta, "beta", "staging", pubRoot, server.BaseUrl));

        var installedPath = Path.Combine(Path.GetTempPath(), "pv-install-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(installedPath, "{\"productVersion\":\"1.0.0\"}");

        using var http = new HttpClient();
        // Stable client fetches stable/latest.json, which does NOT exist -> no manifest.
        var source = new HttpReleaseSource(http, (ch, plat, arch) => $"{server.BaseUrl}/windows/stable/latest.json");
        var verifier = new ReleaseSignatureVerifier(new[] { new KeyValuePair<string, byte[]>(beta.KeyId, beta.PubKey) });
        var checker = new UpdateChecker(source, verifier, new InstalledVersionSource(installedPath));
        var result = checker.CheckAsync(UpdateChannel.Stable).GetAwaiter().GetResult();
        Assert.Equal(UpdateCheckState.NetworkUnavailable, result.State);

        server.Dispose();
    }
}
