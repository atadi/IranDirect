namespace PathVeer.Core.Update;

/// <summary>
/// Production Authenticode verification contract.
///
/// PathVeer treats Authenticode (Windows code-signing trust chain) as a SEPARATE
/// trust layer from the ES256 release-metadata signature (ReleaseSignatureVerifier).
/// A valid Authenticode certificate does NOT guarantee the absence of SmartScreen
/// prompts — SmartScreen is a separate Microsoft reputation system.
///
/// The verification here is conservative: it requires the file to be signed by a
/// certificate whose chain builds to a trusted root AND whose publisher identity
/// matches the configured production publisher policy. A self-signed or otherwise
/// untrusted certificate yields Invalid (never Valid).
///
/// This type is the home of the publisher-identity policy and the WinVerifyTrust
/// probe. On non-Windows platforms WinVerifyTrust is unavailable, so the probe
/// reports NotApplicable (the file is validated by hash alone in that context).
/// </summary>
public sealed class CodeSignatureVerifier
{
    public const string UnprovisionedPublisher = "UNPROVISIONED";

    private readonly string _expectedPublisherPolicy;

    /// <summary>
    /// Creates a verifier. <paramref name="expectedPublisherPolicy"/> is the exact
    /// publisher identity the production certificate must assert (e.g. "Alireza Tadi").
    /// Until a real certificate is provisioned, pass <see cref="UnprovisionedPublisher"/>,
    /// which makes Verify report Invalid for any signed file (no production trust yet).
    /// </summary>
    public CodeSignatureVerifier(string expectedPublisherPolicy)
    {
        _expectedPublisherPolicy = expectedPublisherPolicy ?? UnprovisionedPublisher;
    }

    /// <summary>
    /// Verifies an installer's Authenticode signature and publisher identity.
    /// </summary>
    public InstallerSignatureStatus Verify(string filePath)
    {
        if (_expectedPublisherPolicy == UnprovisionedPublisher)
        {
            // No production certificate has been provisioned. We do not accept any
            // signature as production-valid; the release stays ES256 + hash protected
            // only. Callers must not treat this as a passing production signature.
            return InstallerSignatureStatus.Invalid;
        }

        var probe = WinTrustProbe();
        return probe(filePath, _expectedPublisherPolicy);
    }

    // --- Real WinVerifyTrust probe (Windows only) ----------------------------
    private static Func<string, string, InstallerSignatureStatus>? _winTrust;
    private static Func<string, string, InstallerSignatureStatus> WinTrustProbe()
    {
        if (_winTrust is not null) return _winTrust;
        _winTrust = OperatingSystem.IsWindows()
            ? new Func<string, string, InstallerSignatureStatus>(WinVerify)
            : new Func<string, string, InstallerSignatureStatus>((_, _) => InstallerSignatureStatus.NotApplicable);
        return _winTrust;
    }

    private static InstallerSignatureStatus WinVerify(string filePath, string expectedPublisher)
    {
        // WinVerifyTrust with the Authenticode policy GUID. We P/Invoke the
        // unmanaged API; a failed trust chain or publisher mismatch returns Invalid.
        try
        {
            var result = WinTrust.VerifyAuthenticode(filePath);
            if (result != WinTrust.TrustResult.Success)
                return InstallerSignatureStatus.Invalid;

            var subject = WinTrust.GetCertificateSubject(filePath);
            if (subject is null) return InstallerSignatureStatus.Invalid;
            if (!PublisherMatches(subject, expectedPublisher))
                return InstallerSignatureStatus.Invalid;
            return InstallerSignatureStatus.Valid;
        }
        catch
        {
            return InstallerSignatureStatus.Error;
        }
    }

    /// <summary>
    /// Publisher matching strategy. Windows exposes the certificate Subject as
    /// "CN=..., O=..., ...". We match case-insensitively on a configurable token
    /// (e.g. the legal publisher name) so the exact Subject CN formatting does not
    /// need to be assumed in advance. A real certificate's exact identity is
    /// recorded in the production signing policy once provisioned.
    /// </summary>
    public static bool PublisherMatches(string certificateSubject, string expectedPublisher)
    {
        if (string.IsNullOrWhiteSpace(certificateSubject) || string.IsNullOrWhiteSpace(expectedPublisher))
            return false;
        return certificateSubject.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase);
    }
}
