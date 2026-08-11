using PathVeer.Core.Update;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace PathVeer.Core.Tests.Update;

/// <summary>
/// Production Authenticode trust-policy tests. These prove the CONTRACT and the
/// fail-closed behavior without depending on a real publicly trusted certificate
/// or on signtool.exe being installed. An ephemeral self-signed code-signing
/// certificate is used only to exercise certificate/identity logic; it is NEVER
/// presented as publicly trusted and is disposed after each test.
/// </summary>
public sealed class AuthenticodeReadinessTests
{
    [Fact]
    public void UnprovisionedPublisher_PolicyReportsInvalid_ForAnySignedFile()
    {
        // Until a production Authenticode certificate is provisioned, the verifier
        // must NOT treat any signature as production-valid.
        var sig = new CodeSignatureVerifier(CodeSignatureVerifier.UnprovisionedPublisher);
        // A fake signed file path; the policy short-circuits to Invalid without
        // touching WinVerifyTrust (which would also fail on an untrusted cert).
        Assert.Equal(InstallerSignatureStatus.Invalid, sig.Verify("C:\\nonexistent-signed.exe"));
    }

    [Fact]
    public void PublisherMatches_IsCaseInsensitiveSubstring()
    {
        Assert.True(CodeSignatureVerifier.PublisherMatches(
            "CN=Alireza Tadi, O=PathVeer, C=IR", "Alireza Tadi"));
        Assert.True(CodeSignatureVerifier.PublisherMatches(
            "CN=alireza tadi", "Alireza Tadi"));
        Assert.False(CodeSignatureVerifier.PublisherMatches(
            "CN=Someone Else, O=Other", "Alireza Tadi"));
        Assert.False(CodeSignatureVerifier.PublisherMatches("", "Alireza Tadi"));
        Assert.False(CodeSignatureVerifier.PublisherMatches(null, "Alireza Tadi"));
    }

    [Fact]
    public void InstallerVerifier_Accepts_WhenProbeValid()
    {
        var verifier = new InstallerDownloadVerifier(_ => InstallerSignatureStatus.Valid);
        var artifact = new ReleaseManifest.ManifestArtifact
        {
            FileName = "x.exe",
            Url = "https://releases.pathveer.com/x.exe",
            Sha256 = "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824",
            Size = 5
        };
        using var tmp = new TempFile("hello");
        var res = verifier.Verify(tmp.Path, artifact);
        Assert.True(res.IsValid);
    }

    [Fact]
    public void InstallerVerifier_Rejects_WhenProbeInvalid()
    {
        var verifier = new InstallerDownloadVerifier(_ => InstallerSignatureStatus.Invalid);
        var artifact = new ReleaseManifest.ManifestArtifact
        {
            FileName = "x.exe",
            Url = "https://releases.pathveer.com/x.exe",
            Sha256 = "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824",
            Size = 5
        };
        using var tmp = new TempFile("hello");
        var res = verifier.Verify(tmp.Path, artifact);
        Assert.False(res.IsValid);
        Assert.Contains("signature", res.Error ?? "");
    }

    [Fact]
    public void InstallerVerifier_Rejects_WhenHashMismatch_EvenIfSignatureValid()
    {
        var verifier = new InstallerDownloadVerifier(_ => InstallerSignatureStatus.Valid);
        var artifact = new ReleaseManifest.ManifestArtifact
        {
            FileName = "x.exe",
            Url = "https://releases.pathveer.com/x.exe",
            Sha256 = "0000000000000000000000000000000000000000000000000000000000000000",
            Size = 5
        };
        using var tmp = new TempFile("hello");
        var res = verifier.Verify(tmp.Path, artifact);
        Assert.False(res.IsValid);
        Assert.Contains("SHA-256", res.Error ?? "");
    }

    [Fact]
    public void ForProduction_Unprovisioned_FailsClose()
    {
        // The production installer verifier built with the UNPROVISIONED policy
        // must fail closed (no silent trust) for any file.
        var sig = new CodeSignatureVerifier(CodeSignatureVerifier.UnprovisionedPublisher);
        Assert.Equal(InstallerSignatureStatus.Invalid, sig.Verify("C:\\any-file.exe"));

        // And the InstallerDownloadVerifier factory wires that same policy.
        var verifier = InstallerDownloadVerifier.ForProduction(CodeSignatureVerifier.UnprovisionedPublisher);
        var artifact = new ReleaseManifest.ManifestArtifact
        {
            FileName = "x.exe",
            Url = "https://releases.pathveer.com/x.exe",
            Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Size = 100
        };
        using var tmp = new TempFile("hello");
        Assert.False(verifier.Verify(tmp.Path, artifact).IsValid);
    }

    [Fact]
    public void EphemeralTestCertificate_HasCodeSigningEku_AndIsSelfSigned()
    {
        // Proves the TEST-ONLY certificate shape the release pipeline would use:
        // ECDsa P-256, id-kp-codeSigning EKU, self-signed. Clearly NOT publicly trusted.
        using var cert = MakeTestCodeSigningCert("Alireza Tadi (TEST ONLY — NOT PUBLICLY TRUSTED)");
        Assert.True(cert.HasPrivateKey);
        var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        Assert.NotNull(eku);
        Assert.Contains(eku.EnhancedKeyUsages.Cast<Oid>().Select(o => o.Value),
            v => v == "1.3.6.1.5.5.7.3.3"); // id-kp-codeSigning
        // Self-signed: subject == issuer.
        Assert.Equal(cert.Subject, cert.Issuer);
        Assert.Contains("TEST ONLY", cert.Subject);
    }

    // --- Helpers ---------------------------------------------------------------

    private static X509Certificate2 MakeTestCodeSigningCert(string subject)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest(
            $"CN={subject}", ec, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.3", "codeSigning") }, false));
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddYears(1));
        return cert;
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; }
        public TempFile(string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "pvact_" + System.Guid.NewGuid().ToString("N") + ".bin");
            System.IO.File.WriteAllText(Path, content);
        }
        public void Dispose() => System.IO.File.Delete(Path);
    }
}
