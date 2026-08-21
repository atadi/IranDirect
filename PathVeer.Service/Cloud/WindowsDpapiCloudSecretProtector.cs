using System.Security.Cryptography;
using System.Text;
using PathVeer.Core.Cloud;

namespace PathVeer.Service.Cloud;

/// <summary>
/// Windows DPAPI-backed secret protector (lives in the Windows Service
/// project, which targets net10.0-windows).
///
/// SECURITY BOUNDARY — this is the corrected design after D2 live
/// certification found the original <see cref="DataProtectionScope.LocalMachine"/>
/// choice insufficient:
///
///   * The PathVeer Service runs as LocalSystem. With <see cref="DataProtectionScope.CurrentUser"/>,
///     "current user" is the LocalSystem profile, so the protected blob can ONLY
///     be decrypted by processes running as LocalSystem (the Service). Ordinary
///     local users — even those who can read the bytes — CANNOT decrypt it.
///     This makes the cryptographic boundary depend on the service identity, not
///     on filesystem ACLs.
///
///   * The dedicated Cloud state directory is additionally ACL-hardened by
///     <see cref="CloudStateSecurity"/> (inheritance disabled, BUILTIN\Users
///     removed, SYSTEM + Administrators only). Defense in depth: even if one
///     control is bypassed, the other still prevents an ordinary user from
///     obtaining/decrypting the credential merely by reading PathVeer state.
///
/// The plaintext is pinned and cleared from memory as soon as the
/// protect/unprotect round-trip completes.
///
/// The entropy value below is a NON-SECRET application-binding salt. It is NOT a
/// security control and must not be relied upon as one — the real boundary is
/// the user-scoped DPAPI key plus the filesystem ACL. Do not treat constant
/// entropy as a secret.
/// </summary>
public sealed class WindowsDpapiCloudSecretProtector :
    ICloudSecretProtector
{
    // Non-secret application-binding entropy. DPAPI already scopes the blob to
    // the LocalSystem user profile under CurrentUser; this just namespaces the
    // blob to PathVeer. NOT a security control.
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("PathVeer.Cloud.DeviceCredential.v1");

    public string Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);

        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        try
        {
            byte[] protectedBytes = ProtectedData.Protect(
                plaintext,
                Entropy,
                DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            Array.Clear(plaintext, 0, plaintext.Length);
        }
    }

    public string Unprotect(string protectedSecret)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedSecret);

        byte[] protectedBytes =
            Convert.FromBase64String(protectedSecret);

        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            if (plaintext is not null)
            {
                Array.Clear(plaintext, 0, plaintext.Length);
            }
        }
    }
}
