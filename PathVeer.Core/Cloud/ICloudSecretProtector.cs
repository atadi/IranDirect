namespace PathVeer.Core.Cloud;

/// <summary>
/// Protects/unprotects the device credential at rest.
///
/// Implementations MUST NOT store or return the secret in plaintext outside
/// the protect/unprotect round-trip. The Windows implementation uses DPAPI
/// (LocalMachine scope) so the PathVeer Service — the machine authority —
/// can decrypt it regardless of which interactive user is logged in, while
/// ordinary UI processes (running per-user) are not given the raw secret.
/// </summary>
public interface ICloudSecretProtector
{
    /// <summary>Returns an opaque, protected representation of the secret.</summary>
    string Protect(string secret);

    /// <summary>Recovers the secret from a protected representation.</summary>
    string Unprotect(string protectedSecret);
}
