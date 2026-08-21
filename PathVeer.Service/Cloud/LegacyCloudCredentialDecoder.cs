using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace PathVeer.Service.Cloud;

/// <summary>
/// One-time decoder for Cloud credentials written by the original D2 build,
/// which protected the blob with <see cref="DataProtectionScope.LocalMachine"/>
/// and a fixed (non-secret) entropy value.
///
/// The upgrade to user-scoped DPAPI (<see cref="DataProtectionScope.CurrentUser"/>
/// under the LocalSystem service identity) makes those legacy blobs unreadable
/// by the new protector. This decoder lets the migration re-protect existing
/// enrollments without forcing a re-enrollment.
///
/// LocalMachine DPAPI blobs ARE decryptable by the LocalSystem service account,
/// which is exactly the context that runs this migration, so the round-trip is
/// possible exactly once, at upgrade time.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LegacyCloudCredentialDecoder
{
    // Mirrors the original D2 protector's non-secret application-binding
    // entropy. NOT a secret; included only so legacy blobs remain decodable.
    private static readonly byte[] s_legacyEntropy =
        Encoding.UTF8.GetBytes("PathVeer.Cloud.DeviceCredential.v1");

    /// <summary>
    /// Recovers the raw credential from a legacy <c>LocalMachine</c> DPAPI blob.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public string Decode(string protectedBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedBase64);

        byte[] protectedBytes = Convert.FromBase64String(protectedBase64);
        byte[] plaintext = ProtectedData.Unprotect(
            protectedBytes,
            s_legacyEntropy,
            DataProtectionScope.LocalMachine);

        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            Array.Clear(plaintext, 0, plaintext.Length);
        }
    }
}
