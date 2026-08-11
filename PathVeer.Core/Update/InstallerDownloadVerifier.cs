namespace PathVeer.Core.Update;

using System.IO;
using System.Security.Cryptography;
/// <summary>
/// Verifies a downloaded installer BEFORE it may be executed. Hash is
/// authoritative; size is an additional sanity check only. The signature seam
/// (Authenticode) is exercised through a pluggable verifier so production trust
/// can be enforced without pretending a test cert is trusted. Executing the
/// installer is only permitted after Verify returns success.
/// </summary>
public sealed class InstallerDownloadVerifier
{
    private readonly Func<string, InstallerSignatureStatus>? _signatureProbe;

    public InstallerDownloadVerifier(Func<string, InstallerSignatureStatus>? signatureProbe = null)
    {
        _signatureProbe = signatureProbe;
    }

    public InstallerVerificationResult Verify(string filePath, ReleaseManifest.ManifestArtifact expected)
    {
        if (!File.Exists(filePath))
            return InstallerVerificationResult.Failed("Downloaded installer not found.");

        // Size sanity check (not authoritative, but cheap early rejection).
        try
        {
            var info = new FileInfo(filePath);
            if (expected.Size > 0 && info.Length != expected.Size)
                return InstallerVerificationResult.Failed(
                    $"Installer size mismatch: expected {expected.Size}, got {info.Length}.");
        }
        catch (Exception ex)
        {
            return InstallerVerificationResult.Failed($"Cannot stat installer: {ex.Message}");
        }

        // SHA-256 (authoritative).
        string actualHash;
        try
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            actualHash = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            return InstallerVerificationResult.Failed($"Hash computation failed: {ex.Message}");
        }

        if (!string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            return InstallerVerificationResult.Failed(
                $"Installer SHA-256 mismatch: expected {expected.Sha256}, got {actualHash}.");

        // Signature seam (Authenticode) — production fails closed if untrusted.
        if (_signatureProbe is not null)
        {
            var status = _signatureProbe(filePath);
            if (status != InstallerSignatureStatus.Valid)
                return InstallerVerificationResult.Failed($"Installer signature invalid: {status}.");
        }

        return InstallerVerificationResult.Success(actualHash);
    }

    /// <summary>
    /// Builds a production installer verifier that enforces Authenticode trust via
    /// <see cref="CodeSignatureVerifier"/>. Until a production Authenticode
    /// certificate is provisioned (publisher policy = <see
    /// cref="CodeSignatureVerifier.UnprovisionedPublisher"/>), every signed file
    /// is reported Invalid so the release stays ES256 + hash protected only and is
    /// never presented as Authenticode-trusted. A <paramref name="expectedPublisher"/>
    /// value is required; pass <see cref="CodeSignatureVerifier.UnprovisionedPublisher"/>
    /// when no certificate exists.
    /// </summary>
    public static InstallerDownloadVerifier ForProduction(string expectedPublisher)
    {
        var sig = new CodeSignatureVerifier(expectedPublisher);
        return new InstallerDownloadVerifier(path => sig.Verify(path));
    }
}

public enum InstallerSignatureStatus
{
    Valid,
    Invalid,
    Unsigned,
    NotApplicable, // self-contained on a runtime-less box / test where signature is not enforced
    Error
}

public sealed class InstallerVerificationResult
{
    public bool IsValid { get; }
    public string? Hash { get; }
    public string? Error { get; }
    private InstallerVerificationResult(bool valid, string? hash, string? error)
    {
        IsValid = valid; Hash = hash; Error = error;
    }
    public static InstallerVerificationResult Success(string hash) => new(true, hash, null);
    public static InstallerVerificationResult Failed(string error) => new(false, null, error);
}
