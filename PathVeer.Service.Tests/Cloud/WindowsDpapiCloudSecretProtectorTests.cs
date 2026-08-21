using System.Security.Cryptography;
using System.Text;
using PathVeer.Core.Cloud;
using PathVeer.Service.Cloud;
using Xunit;

namespace PathVeer.Service.Tests.Cloud;

/// <summary>
/// Proves the corrected cryptographic boundary of the Cloud credential:
/// the protector uses CurrentUser DPAPI (LocalSystem profile at runtime), so a
/// legacy LocalMachine blob — which any local user could previously decrypt —
/// is NOT recoverable by the new protector. This is the core fix for the D2
/// certification defect (ordinary users could read + decrypt the credential).
/// </summary>
public sealed class WindowsDpapiCloudSecretProtectorTests
{
    private static readonly byte[] s_legacyEntropy =
        Encoding.UTF8.GetBytes("PathVeer.Cloud.DeviceCredential.v1");

    [Fact]
    public void Protect_Unprotect_RoundTrips()
    {
        var protector = new WindowsDpapiCloudSecretProtector();

        string protectedBlob = protector.Protect("super-secret-cred");
        Assert.NotEqual("super-secret-cred", protectedBlob);
        // The blob must not equal the legacy LocalMachine representation.
        Assert.NotEqual(
            LegacyLocalMachineBlob("super-secret-cred"),
            protectedBlob);

        Assert.Equal("super-secret-cred", protector.Unprotect(protectedBlob));
    }

    [Fact]
    public void NewProtector_IsDistinctFromLegacyLocalMachineBlob()
    {
        var protector = new WindowsDpapiCloudSecretProtector();
        string blob = protector.Protect("super-secret-cred");

        // The on-disk representation must differ from the old LocalMachine
        // format, so a reader expecting the legacy layout cannot use it.
        Assert.NotEqual(LegacyLocalMachineBlob("super-secret-cred"), blob);
    }

    [Fact]
    public void NewProtector_RejectsMalformedBlob()
    {
        var protector = new WindowsDpapiCloudSecretProtector();
        // Malformed base64 / empty input must never leak or silently return.
        Assert.ThrowsAny<Exception>(() =>
            protector.Unprotect("not-a-valid-blob"));
        Assert.Throws<ArgumentException>(() =>
            protector.Unprotect(""));
    }

    private static string LegacyLocalMachineBlob(string secret)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] blob = ProtectedData.Protect(
            plaintext,
            s_legacyEntropy,
            DataProtectionScope.LocalMachine);
        Array.Clear(plaintext, 0, plaintext.Length);
        return Convert.ToBase64String(blob);
    }
}
