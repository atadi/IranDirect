namespace PathVeer.Core.Cloud;

/// <summary>
/// Protects/unprotects the device credential at rest.
///
/// Implementations MUST NOT store or return the secret in plaintext outside
/// the protect/unprotect round-trip. The Windows implementation uses DPAPI
/// with <see cref="System.Security.Cryptography.DataProtectionScope.CurrentUser"/>
/// scope while the Service runs as LocalSystem, so the blob can only be
/// decrypted by the LocalSystem service identity — ordinary local users
/// (including the Tray/CLI running per-user) cannot obtain the raw secret even
/// if they can read the on-disk bytes. The dedicated Cloud state directory is
/// additionally ACL-hardened (see PathVeer.Service.Cloud.CloudStateSecurity).
/// </summary>
public interface ICloudSecretProtector
{
    /// <summary>Returns an opaque, protected representation of the secret.</summary>
    string Protect(string secret);

    /// <summary>Recovers the secret from a protected representation.</summary>
    string Unprotect(string protectedSecret);
}
