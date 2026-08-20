namespace PathVeer.Core.Update;

/// <summary>
/// Single authoritative source for the production Authenticode publisher policy.
///
/// This is the ONLY place the expected production publisher identity lives. It
/// MUST NOT be derived from:
///   - the release manifest (an attacker controlling metadata could then also
///     control the expected identity, defeating Authenticode);
///   - R2 / any remote SaaS API;
///   - a user-controllable environment variable on production clients.
///
/// The value is a non-secret, committed policy constant. Until a real
/// production Authenticode certificate is provisioned, it is
/// <see cref="CodeSignatureVerifier.UnprovisionedPublisher"/>, which makes every
/// signed installer fail closed (no production trust yet).
///
/// Provisioning seam: when the real certificate is issued, change ONLY
/// <see cref="CurrentPublisher"/> to the exact legal publisher identity the
/// certificate asserts (e.g. the certificate Subject CN/legal name). No other
/// code path may supply the expected publisher. <see
/// cref="InstallerDownloadVerifier.ForProduction(string)"/> consumes this value
/// exclusively.
/// </summary>
public static class ProductionSigningPolicy
{
    /// <summary>
    /// The expected production publisher identity for installer Authenticode
    /// verification. Defaults to <see cref="CodeSignatureVerifier.UnprovisionedPublisher"/>
    /// because no production certificate has been provisioned yet.
    ///
    /// DO NOT set this to a guessed or historical publisher. Set it to the exact
    /// Subject identity asserted by the issued production certificate once that
    /// certificate exists and the operator has confirmed the legal identity.
    /// </summary>
    public const string CurrentPublisher = CodeSignatureVerifier.UnprovisionedPublisher;

    /// <summary>
    /// True when a real production publisher identity has been provisioned. While
    /// <see langword="false"/>, every production installer signature check fails
    /// closed regardless of whether the file is signed.
    /// </summary>
    public static bool IsProvisioned =>
        !string.Equals(CurrentPublisher, CodeSignatureVerifier.UnprovisionedPublisher, StringComparison.Ordinal);

    /// <summary>
    /// Builds the production installer verifier wired to the authoritative
    /// publisher policy. This is the only sanctioned factory for production
    /// update-apply verification.
    /// </summary>
    public static InstallerDownloadVerifier CreateInstallerVerifier() =>
        InstallerDownloadVerifier.ForProduction(CurrentPublisher);
}
