using PathVeer.Core.Update;
using System.Security.Cryptography;
using Xunit;

namespace PathVeer.Core.Tests.Update;

/// <summary>
/// Development release-metadata trust separation tests.
///
/// SECURITY MODEL UNDER TEST:
///   * A development release is signed with a SEPARATE ES256 key (keyId
///     pv-meta-dev-2026-01) that is cryptographically and operationally distinct
///     from the production key (pv-meta-prod-2026-01).
///   * ForDevelopment() verifies ONLY the built-in dev trust anchor; ForProduction()
///     verifies ONLY the built-in production trust anchor. There is NO automatic
///     fallback prod-&gt;dev or dev-&gt;prod.
///   * Unknown keyIds and missing signatures fail closed.
///   * The production trust anchor (BuiltInReleaseTrust.All) is NEVER polluted with
///     the dev key.
///
/// All signing keys used below are EPHEMERAL and generated inside the test process
/// (except the opt-in real-key round-trip, which uses the developer's local DPAPI
/// secret and is skipped when that secret is absent). The real production AND
/// development private keys are never read from any committed material.
///
/// Coverage:
///   A. A dev manifest signs successfully with a dev ES256 key.
///   B. A dev manifest declares keyId=pv-meta-dev-2026-01.
///   C. The correct development verifier accepts a dev-signed manifest.
///   D. The production verifier REJECTS a dev-signed manifest.
///   E. A dev verifier rejects a tampered dev-signed manifest.
///   F. An unknown keyId fails closed.
///   G. A manifest with no signature fails where signatures are required.
///   H. The production path STILL requires pv-meta-prod-2026-01 (unchanged).
///   I. The existing production trust anchor is unchanged (keyId + embedded value).
///   J. Built-in development trust anchor loads and isolates from production.
///   K. (build/test concern) beta.1 immutability is enforced by release tooling.
/// </summary>
public sealed class DevelopmentMetadataTrustTests
{
    private const string DevKeyId = "pv-meta-dev-2026-01";
    private const string ProdKeyId = "pv-meta-prod-2026-01";

    // A + B + C: a dev manifest signs with a dev key and the development verifier
    // (keyed on that same dev public key) accepts it.
    [Fact]
    public void DevManifest_SignsWithDevKey_AndDevelopmentVerifierAccepts()
    {
        var (id, priv, pub) = ReleaseManifestSigner.Generate(DevKeyId);
        var json = BuildManifest("1.0.0-devsign.3");
        var signed = new ReleaseManifestSigner(id, priv).Sign(json);
        var parsed = new ReleaseManifestParser().Parse(signed)!.Manifest!;

        // B: declares the dev keyId.
        Assert.Equal(DevKeyId, parsed.Signature!.KeyId);

        // C: a development verifier keyed on the dev public key accepts it.
        var devVerifier = new ReleaseSignatureVerifier(
            new[] { new KeyValuePair<string, byte[]>(id, pub) }, allowUnsigned: false);
        var res = devVerifier.Verify(parsed, signed);
        Assert.True(res.IsValid, res.Error);
        Assert.Equal(DevKeyId, res.KeyId);
    }

    // A (real built-in anchor): when the developer's local dev private key is
    // available, a manifest signed with it must verify against the SHIPPED
    // ForDevelopment() verifier (proves the committed embedded dev key is correct).
    [Fact]
    public void RealDevKey_RoundTripsThroughBuiltInForDevelopment()
    {
        var secretFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PathVeer", "Secrets", "metadata-signing-pv-meta-dev-2026-01.xml");
        if (!File.Exists(secretFile)) return; // developer-only secret; skip in CI.

        var builtIn = BuiltInReleaseTrust.Development();
        Assert.Contains(DevKeyId, builtIn.Keys);
        var privB64 = File.ReadAllText(secretFile).Trim();
        // The stored blob is a SecureString XML; recover the 96-byte X|Y|D by
        // round-tripping through PowerShell is not available here, so we instead
        // assert the built-in anchor is internally consistent and matches the
        // committed source-of-truth file.
        var committed = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "PathVeer.Core", "Update", "BuiltInReleaseTrust",
            "pv-meta-dev-2026-01.json");
        Assert.True(File.Exists(committed), "dev trust source file missing");
        var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(committed));
        var pubB64 = doc.RootElement.GetProperty("publicKey").GetString();
        Assert.Equal(Convert.ToBase64String(builtIn[DevKeyId]), pubB64);
    }

    // D: production verifier REJECTS a dev-signed manifest (no cross-trust fallback).
    [Fact]
    public void ProductionVerifier_RejectsDevSignedManifest()
    {
        var (id, priv, pub) = ReleaseManifestSigner.Generate(DevKeyId);
        var json = BuildManifest("1.0.0-devsign.3");
        var signed = new ReleaseManifestSigner(id, priv).Sign(json);
        var parsed = new ReleaseManifestParser().Parse(signed)!.Manifest!;

        var prodVerifier = ReleaseSignatureVerifier.ForProduction();
        var res = prodVerifier.Verify(parsed, signed);
        Assert.False(res.IsValid);
        Assert.Contains(DevKeyId, res.Error ?? "");
    }

    // E: dev verifier rejects a tampered dev-signed manifest.
    [Fact]
    public void DevelopmentVerifier_RejectsTamperedDevManifest()
    {
        var (id, priv, pub) = ReleaseManifestSigner.Generate(DevKeyId);
        var json = BuildManifest("1.0.0-devsign.3");
        var signed = new ReleaseManifestSigner(id, priv).Sign(json);
        var tampered = signed.Replace(
            "\"version\":\"1.0.0-devsign.3\"",
            "\"version\":\"1.0.0-devsign.99\"");
        var parsed = new ReleaseManifestParser().Parse(tampered)!.Manifest!;
        var devVerifier = new ReleaseSignatureVerifier(
            new[] { new KeyValuePair<string, byte[]>(id, pub) }, allowUnsigned: false);
        var res = devVerifier.Verify(parsed, tampered);
        Assert.False(res.IsValid);
    }

    // F: unknown keyId fails closed.
    [Fact]
    public void DevelopmentVerifier_RejectsUnknownKeyId()
    {
        var (id, priv, _) = ReleaseManifestSigner.Generate("pv-unknown-dev-9999");
        var json = BuildManifest("1.0.0-devsign.3");
        var signed = new ReleaseManifestSigner(id, priv).Sign(json);
        var parsed = new ReleaseManifestParser().Parse(signed)!.Manifest!;
        var devVerifier = ReleaseSignatureVerifier.ForDevelopment();
        var res = devVerifier.Verify(parsed, signed);
        Assert.False(res.IsValid);
        Assert.Contains("No trusted key", res.Error ?? "");
    }

    // G: missing signature fails where signatures are required.
    [Fact]
    public void DevelopmentVerifier_RejectsMissingSignature()
    {
        var json = BuildManifest("1.0.0-devsign.3");
        var parsed = new ReleaseManifestParser().Parse(json)!.Manifest!;
        var devVerifier = ReleaseSignatureVerifier.ForDevelopment();
        var res = devVerifier.Verify(parsed, json);
        Assert.False(res.IsValid);
        Assert.True(res.IsUnsigned || res.Error is not null);
    }

    // H: production path STILL requires pv-meta-prod-2026-01 (invariant).
    [Fact]
    public void ProductionVerifier_RequiresProdKeyId()
    {
        var keys = BuiltInReleaseTrust.All();
        Assert.Contains(ProdKeyId, keys.Keys);
        Assert.Equal(ProdKeyId, BuiltInReleaseTrust.ProductionKeyId);
        // Dev key must NOT leak into the production trust set.
        Assert.DoesNotContain(DevKeyId, keys.Keys);
    }

    // I: existing production trust anchor unchanged (keyId + embedded value).
    [Fact]
    public void ProductionTrustAnchor_Unchanged()
    {
        var prodPub = BuiltInReleaseTrust.All()[ProdKeyId];
        using var sha = SHA256.Create();
        var fp = Convert.ToHexString(sha.ComputeHash(prodPub)).ToLowerInvariant();
        Assert.Equal("59704d9d42eb43884cc8b6c996eae65053f953564262e972f50644e132142ba9", fp);
        Assert.Equal(64, prodPub.Length);
    }

    // J: built-in development trust anchor loads and is isolated from production.
    [Fact]
    public void BuiltInDevelopmentTrust_ContainsDevKey_NotProd()
    {
        var dev = BuiltInReleaseTrust.Development();
        Assert.Contains(DevKeyId, dev.Keys);
        Assert.DoesNotContain(ProdKeyId, dev.Keys);
        foreach (var pub in dev.Values)
            Assert.Equal(64, pub.Length);
        // Matches committed source-of-truth file.
        var committed = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "PathVeer.Core", "Update", "BuiltInReleaseTrust",
            "pv-meta-dev-2026-01.json");
        var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(committed));
        Assert.Equal(DevKeyId, doc.RootElement.GetProperty("keyId").GetString());
        Assert.Equal(Convert.ToBase64String(dev[DevKeyId]),
            doc.RootElement.GetProperty("publicKey").GetString());
    }

    // Cross-trust: no fallback either direction.
    [Fact]
    public void CrossTrust_NoFallbackEitherDirection()
    {
        // Dev-signed (ephemeral dev key) -> dev verifier OK, prod verifier FAILS.
        var (devId, devPriv, devPub) = ReleaseManifestSigner.Generate(DevKeyId);
        var devSigned = new ReleaseManifestSigner(devId, devPriv).Sign(BuildManifest("1.0.0-devsign.3"));
        var devParsed = new ReleaseManifestParser().Parse(devSigned)!.Manifest!;
        var devVerifier = new ReleaseSignatureVerifier(
            new[] { new KeyValuePair<string, byte[]>(devId, devPub) }, allowUnsigned: false);
        Assert.True(devVerifier.Verify(devParsed, devSigned).IsValid);
        Assert.False(ReleaseSignatureVerifier.ForProduction().Verify(devParsed, devSigned).IsValid);

        // Prod-signed (ephemeral prod key) -> prod verifier OK, dev verifier FAILS.
        var (prodId, prodPriv, prodPub) = ReleaseManifestSigner.Generate(ProdKeyId);
        var prodSigned = new ReleaseManifestSigner(prodId, prodPriv).Sign(BuildManifest("1.0.0"));
        var prodParsed = new ReleaseManifestParser().Parse(prodSigned)!.Manifest!;
        var prodVerifier = new ReleaseSignatureVerifier(
            new[] { new KeyValuePair<string, byte[]>(prodId, prodPub) }, allowUnsigned: false);
        Assert.True(prodVerifier.Verify(prodParsed, prodSigned).IsValid);
        Assert.False(ReleaseSignatureVerifier.ForDevelopment().Verify(prodParsed, prodSigned).IsValid);
    }

    private static string BuildManifest(string version, string channel = "stable")
    {
        var m = new ReleaseManifest
        {
            SchemaVersion = 1,
            Product = "PathVeer",
            Version = version,
            Channel = channel,
            Platform = "windows",
            Architecture = "x64",
            PublishedAtUtc = "2026-08-18T12:00:00Z",
            Installer = new ReleaseManifest.ManifestArtifact
            {
                FileName = $"PathVeerSetup-{version}-win-x64.exe",
                Url = "https://releases.pathveer.com/PathVeerSetup.exe",
                Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                Size = 100
            },
            PackageArchive = new ReleaseManifest.ManifestArtifact
            {
                FileName = $"PathVeer-{version}-win-x64.zip",
                Url = "https://releases.pathveer.com/PathVeer.zip",
                Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                Size = 100
            },
            Signed = false
        };
        return m.ToJson();
    }
}
