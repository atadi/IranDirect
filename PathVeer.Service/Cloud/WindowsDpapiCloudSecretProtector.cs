using System.Security.Cryptography;
using System.Text;
using PathVeer.Core.Cloud;

namespace PathVeer.Service.Cloud;

/// <summary>
/// Windows DPAPI-backed secret protector (lives in the Windows Service
/// project, which targets net10.0-windows).
///
/// Uses <see cref="DataProtectionScope.LocalMachine"/> so the credential can
/// be decrypted by the PathVeer Service (which runs under the machine account)
/// on any interactive session, while ordinary per-user UI processes cannot
/// read the raw secret. The plaintext is pinned and cleared from memory as
/// soon as the protection round-trip completes.
///
/// A constant, non-secret entropy value binds the blob to the PathVeer
/// application so it is not trivially usable by unrelated processes on the
/// same machine. Entropy is NOT a secret; it is defense-in-depth only.
/// </summary>
public sealed class WindowsDpapiCloudSecretProtector :
    ICloudSecretProtector
{
    // Non-secret application binding entropy. DPAPI already scopes to the
    // local machine; this just namespaces the blob to PathVeer.
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
                DataProtectionScope.LocalMachine);

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
                DataProtectionScope.LocalMachine);

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
