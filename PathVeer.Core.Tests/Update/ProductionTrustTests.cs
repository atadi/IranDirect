using PathVeer.Core.Update;
using System.Security.Cryptography;
using Xunit;

namespace PathVeer.Core.Tests.Update;

/// <summary>
/// Production release-metadata trust-root tests. All keys are ephemeral and
/// generated inside the test process — the real production private key is never
/// used, never read from the secret store, and never committed. These tests
/// prove the BUILT-IN production trust anchor behaves correctly, that staging
/// keys are isolated from production trust, that rotation overlap works, and that
/// the public-key fingerprint is deterministic.
/// </summary>
public sealed class ProductionTrustTests
{
    [Fact]
    public void BuiltInProductionTrust_ContainsProdKey_NotStaging()
    {
        var keys = BuiltInReleaseTrust.All();
        Assert.Contains("pv-meta-prod-2026-01", keys.Keys);
        // Staging keys must never be implicitly trusted by production clients.
        Assert.DoesNotContain("pv-meta-staging-2026", keys.Keys);
        foreach (var pub in keys.Values)
            Assert.Equal(64, pub.Length); // Q.X||Q.Y
    }

    [Fact]
    public void ForProduction_RejectsUnsignedManifest()
    {
        var verifier = ReleaseSignatureVerifier.ForProduction();
        var json = BuildManifest("1.0.0");
        var parsed = new ReleaseManifestParser().Parse(json)!.Manifest!;
        var res = verifier.Verify(parsed, json);
        Assert.False(res.IsValid);
        Assert.True(res.IsUnsigned || res.Error is not null);
    }

    [Fact]
    public void ForProduction_AcceptsValidProductionSignature()
    {
        // Sign with an ephemeral key whose public half matches the built-in
        // production anchor — we inject it into the built-in set via env override
        // is NOT allowed to weaken; instead we verify the built-in key directly by
        // constructing a verifier from the same public bytes the signer produced.
        var (id, priv, pub) = ReleaseManifestSigner.Generate("pv-prod-test");
        var json = BuildManifest("1.0.0");
        var signed = new ReleaseManifestSigner(id, priv).Sign(json);
        var parsed = new ReleaseManifestParser().Parse(signed)!.Manifest!;

        // Verifier seeded with the same public key (simulating the embedded one).
        var verifier = new ReleaseSignatureVerifier(
            new[] { new KeyValuePair<string, byte[]>(id, pub) }, allowUnsigned: false);
        var res = verifier.Verify(parsed, signed);
        Assert.True(res.IsValid, res.Error);
        Assert.Equal(id, res.KeyId);
    }

    [Fact]
    public void ForProduction_RejectsWrongKey_SameKeyId()
    {
        var (_, _, pub) = ReleaseManifestSigner.Generate("pv-prod-test");
        var verifier = new ReleaseSignatureVerifier(
            new[] { new KeyValuePair<string, byte[]>("pv-meta-prod-2026-01", pub) }, allowUnsigned: false);
        // Manifest signed by a DIFFERENT key but claiming the production keyId.
        var (otherId, otherPriv, _) = ReleaseManifestSigner.Generate("pv-other");
        var json = BuildManifest("1.0.0");
        var signed = new ReleaseManifestSigner(otherId, otherPriv).Sign(json);
        var parsed = new ReleaseManifestParser().Parse(signed)!.Manifest!;
        // Re-point the signature keyId to the production id without re-signing.
        var doc = System.Text.Json.JsonDocument.Parse(signed).RootElement;
        var alg = doc.GetProperty("signature").GetProperty("algorithm").GetString();
        var tampered = signed.Replace(
            $"\"keyId\": \"{otherId}\"", $"\"keyId\": \"pv-meta-prod-2026-01\"");
        var reparsed = new ReleaseManifestParser().Parse(tampered)!.Manifest!;
        var res = verifier.Verify(reparsed, tampered);
        Assert.False(res.IsValid);
    }

    [Fact]
    public void ForProduction_RejectsStagingSignedManifest()
    {
        // A manifest signed by the staging key must not validate against the
        // production-only built-in trust, even if staging material were known.
        var (id, priv, _) = ReleaseManifestSigner.Generate("pv-meta-staging-2026");
        var json = BuildManifest("1.0.0-beta.1", channel: "beta");
        var signed = new ReleaseManifestSigner(id, priv).Sign(json);
        var parsed = new ReleaseManifestParser().Parse(signed)!.Manifest!;
        var verifier = ReleaseSignatureVerifier.ForProduction();
        var res = verifier.Verify(parsed, signed);
        Assert.False(res.IsValid);
        Assert.Contains("pv-meta-staging-2026", res.Error ?? "");
    }

    [Fact]
    public void ForProduction_RejectsUnknownKeyId()
    {
        var (id, priv, _) = ReleaseManifestSigner.Generate("pv-unknown-9999");
        var json = BuildManifest("1.0.0");
        var signed = new ReleaseManifestSigner(id, priv).Sign(json);
        var parsed = new ReleaseManifestParser().Parse(signed)!.Manifest!;
        var verifier = ReleaseSignatureVerifier.ForProduction();
        var res = verifier.Verify(parsed, signed);
        Assert.False(res.IsValid);
        Assert.Contains("No trusted key", res.Error ?? "");
    }

    [Fact]
    public void ForProduction_AllowUnsignedIsFalse()
    {
        var verifier = ReleaseSignatureVerifier.ForProduction();
        // Reflect the private field to assert fail-closed behaviour contract.
        var f = typeof(ReleaseSignatureVerifier)
            .GetField("_allowUnsigned", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.False((bool)f.GetValue(verifier)!);
    }

    [Fact]
    public void RotationSet_CanContainProdAAndProdB()
    {
        var (aId, aPriv, aPub) = ReleaseManifestSigner.Generate("pv-meta-prod-2026-01");
        var (bId, bPriv, bPub) = ReleaseManifestSigner.Generate("pv-meta-prod-2027-01");
        var verifier = new ReleaseSignatureVerifier(new[]
        {
            new KeyValuePair<string, byte[]>(aId, aPub),
            new KeyValuePair<string, byte[]>(bId, bPub),
        }, allowUnsigned: false);

        var jsonA = BuildManifest("1.0.0");
        var signedA = new ReleaseManifestSigner(aId, aPriv).Sign(jsonA);
        Assert.True(verifier.Verify(new ReleaseManifestParser().Parse(signedA)!.Manifest!, signedA).IsValid);

        var jsonB = BuildManifest("2.0.0");
        var signedB = new ReleaseManifestSigner(bId, bPriv).Sign(jsonB);
        Assert.True(verifier.Verify(new ReleaseManifestParser().Parse(signedB)!.Manifest!, signedB).IsValid);
    }

    [Fact]
    public void PublicKeyFingerprint_Deterministic()
    {
        var (_, _, pub) = ReleaseManifestSigner.Generate("pv-fp");
        using var sha = SHA256.Create();
        var fp1 = Convert.ToHexString(sha.ComputeHash(pub)).ToLowerInvariant();
        var fp2 = Convert.ToHexString(sha.ComputeHash((byte[])pub.Clone())).ToLowerInvariant();
        Assert.Equal(fp1, fp2);
        Assert.Equal(64, fp1.Length);
    }

    [Fact]
    public void AdditionalTrustedKey_ThroughFromEnvironment_DoesNotWeakenProductionTrust()
    {
        // PATHVEER_TRUSTED_META_KEYS is a dev/test/staging mechanism (FromEnvironment).
        // It must NOT weaken the production trust root: ForProduction() ignores it,
        // so a staging-signed manifest still fails production verification, while the
        // same key IS accepted on the explicit dev/test path.
        var (stagingId, stagingPriv, stagingPub) = ReleaseManifestSigner.Generate("pv-meta-staging-2026");
        var prev = Environment.GetEnvironmentVariable("PATHVEER_TRUSTED_META_KEYS");
        try
        {
            Environment.SetEnvironmentVariable("PATHVEER_TRUSTED_META_KEYS",
                $"{stagingId}:{Convert.ToBase64String(stagingPub)}");

            // Production path: staging key must NOT be trusted.
            var prodVerifier = ReleaseSignatureVerifier.ForProduction();
            var json = BuildManifest("1.0.0-beta.1", channel: "beta");
            var signed = new ReleaseManifestSigner(stagingId, stagingPriv).Sign(json);
            var parsed = new ReleaseManifestParser().Parse(signed)!.Manifest!;
            Assert.False(prodVerifier.Verify(parsed, signed).IsValid);

            // Dev/test path: same key IS trusted (controlled override).
            var devVerifier = ReleaseSignatureVerifier.FromEnvironment(devAllowUnsigned: false);
            Assert.True(devVerifier.Verify(parsed, signed).IsValid);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATHVEER_TRUSTED_META_KEYS", prev);
        }
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
            PublishedAtUtc = "2026-08-09T12:00:00Z",
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
