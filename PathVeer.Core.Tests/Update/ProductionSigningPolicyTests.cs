namespace PathVeer.Core.Tests.Update;

using PathVeer.Core.Update;

/// <summary>
/// Production publisher-policy design tests (Gap A / §6). The expected publisher
/// MUST come from exactly one authoritative location (ProductionSigningPolicy)
/// and MUST NOT be derivable from the manifest, R2, or any remote source.
/// Until a real production Authenticode certificate exists, the policy is
/// UNPROVISIONED and every production installer signature check fails closed.
/// </summary>
public sealed class ProductionSigningPolicyTests
{
    [Fact]
    public void CurrentPublisher_DefaultsToUnprovisioned()
    {
        Assert.Equal(CodeSignatureVerifier.UnprovisionedPublisher,
            ProductionSigningPolicy.CurrentPublisher);
    }

    [Fact]
    public void IsProvisioned_False_WhileUnprovisioned()
    {
        Assert.False(ProductionSigningPolicy.IsProvisioned);
    }

    [Fact]
    public void CreateInstallerVerifier_FailsClosed_WhileUnprovisioned()
    {
        var verifier = ProductionSigningPolicy.CreateInstallerVerifier();
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "signed-installer-bytes");
            var art = new ReleaseManifest.ManifestArtifact
            {
                FileName = "x.exe",
                Url = "https://releases.pathveer.com/x.exe",
                Sha256 = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes("signed-installer-bytes")))
                    .ToLowerInvariant(),
                Size = "signed-installer-bytes".Length
            };
            var res = verifier.Verify(tmp, art);
            // Even with a correct hash, the UNPROVISIONED policy must reject.
            Assert.False(res.IsValid);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void ExpectedPublisher_IsNotReadFromManifest()
    {
        // ReleaseManifest.ManifestArtifact has no publisher field: a hostile
        // manifest cannot supply or override the expected production identity.
        var props = typeof(ReleaseManifest.ManifestArtifact).GetProperties();
        Assert.DoesNotContain(props, p =>
            p.Name.Contains("Publisher", StringComparison.OrdinalIgnoreCase));
    }
}
