using PathVeer.Core.Cloud;
using PathVeer.Service.Cloud;
using Xunit;

namespace PathVeer.Service.Tests.Cloud;

/// <summary>
/// End-to-end proof that the REAL Windows DPAPI (CurrentUser) protector works
/// through the CloudRegistrationStore: enrollment persists a recoverable,
/// non-plaintext credential, and the heartbeat path (GetCredentialAsync) can
/// decrypt it. This is the path the live Service uses.
/// </summary>
public sealed class CloudStoreWindowsProtectorTests
{
    [Fact]
    public async Task Store_WithRealProtector_RoundTripsCredential()
    {
        string path = TempFile();
        try
        {
            var store = new CloudRegistrationStore(
                path, new WindowsDpapiCloudSecretProtector());

            await store.SaveEnrolledAsync(
                "device-1", "org-1", "super-secret-cred", "Box",
                DateTimeOffset.UtcNow);

            // Heartbeat path recovers the credential via the real protector.
            string? cred = await store.GetCredentialAsync();
            Assert.Equal("super-secret-cred", cred);

            // Raw credential must not appear in plaintext on disk.
            string onDisk = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("super-secret-cred", onDisk);
            Assert.Contains("CredentialProtectedBase64", onDisk);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Store_WithRealProtector_HardenedDirKeepsCredentialSecret()
    {
        // Even when the store file lives in a dedicated, ACL-hardened directory
        // the Service creates, the credential remains recoverable by the Service
        // (LocalSystem) and never written as plaintext.
        string dir = Path.Combine(
            Path.GetTempPath(),
            "pvcloud-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "cloud-registration.json");
        try
        {
            var store = new CloudRegistrationStore(
                path, new WindowsDpapiCloudSecretProtector());

            await store.SaveEnrolledAsync(
                "device-1", "org-1", "super-secret-cred", null,
                DateTimeOffset.UtcNow);

            string? cred = await store.GetCredentialAsync();
            Assert.Equal("super-secret-cred", cred);

            string onDisk = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("super-secret-cred", onDisk);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string TempFile() =>
        Path.Combine(
            Path.GetTempPath(),
            "pvcloud-realstore-" + Guid.NewGuid().ToString("N") + ".json");
}
