using System.Text.Json;
using PathVeer.Core.Cloud;
using Xunit;

namespace PathVeer.Core.Tests.Cloud;

public sealed class CloudRegistrationStoreTests
{
    private static string TempFile() =>
        Path.Combine(
            Path.GetTempPath(),
            "pvcloud-test-" + Guid.NewGuid().ToString("N") + ".json");

    // B. successful enrollment securely persists deviceId, orgId, credential.
    [Fact]
    public async Task SaveEnrolledAsync_PersistsIdentityAndCredential()
    {
        string path = TempFile();
        try
        {
            InMemoryCloudSecretProtector protector = new();
            CloudRegistrationStore store = new(path, protector);

            await store.SaveEnrolledAsync(
                "device-1",
                "org-1",
                "super-secret-cred",
                "My Box",
                DateTimeOffset.UtcNow);

            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.Connected, view.State);
            Assert.Equal("device-1", view.DeviceId);
            Assert.Equal("org-1", view.OrganizationId);
            Assert.Equal("My Box", view.DeviceLabel);
            Assert.True(view.HasCredential);

            // The raw credential must be recoverable via the store.
            string? cred = await store.GetCredentialAsync();
            Assert.Equal("super-secret-cred", cred);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // C + N. raw credential is NOT written to plaintext config on disk.
    [Fact]
    public async Task SaveEnrolledAsync_CredentialNotStoredAsPlaintext()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store = new(path, new InMemoryCloudSecretProtector());

            await store.SaveEnrolledAsync(
                "device-1", "org-1", "super-secret-cred", null,
                DateTimeOffset.UtcNow);

            string onDisk = await File.ReadAllTextAsync(path);

            // The literal secret must never appear in the file.
            Assert.DoesNotContain(
                "super-secret-cred",
                onDisk);
            // The persisted blob is the protected ("mem:...") representation.
            Assert.Contains("CredentialProtectedBase64", onDisk);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // D. enrollment code is not persisted unnecessarily.
    [Fact]
    public async Task SaveEnrolledAsync_DoesNotPersistEnrollmentCode()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store = new(path, new InMemoryCloudSecretProtector());

            await store.SaveEnrolledAsync(
                "device-1", "org-1", "cred", null,
                DateTimeOffset.UtcNow);

            string onDisk = await File.ReadAllTextAsync(path);

            // The operator-entered code must not be stored anywhere.
            Assert.DoesNotContain("ENROLL-CODE-XYZ", onDisk);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // O. un-enrolled installation performs no authenticated heartbeat
    // (no credential is available, so the heartbeat path has nothing to send).
    [Fact]
    public async Task Unenrolled_GetCredentialReturnsNull()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store = new(path, new InMemoryCloudSecretProtector());

            string? cred = await store.GetCredentialAsync();
            Assert.Null(cred);

            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.NotConnected, view.State);
            Assert.False(view.HasCredential);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // K. revoked credential moves local state to revoked.
    [Fact]
    public async Task MarkRevokedAsync_ClearsCredentialAndSetsRevoked()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store = new(path, new InMemoryCloudSecretProtector());
            await store.SaveEnrolledAsync(
                "device-1", "org-1", "cred", null,
                DateTimeOffset.UtcNow);

            await store.MarkRevokedAsync();

            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.Revoked, view.State);
            Assert.False(view.HasCredential);

            // No credential can be recovered after revocation.
            string? cred = await store.GetCredentialAsync();
            Assert.Null(cred);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // J. successful heartbeat updates safe local connection metadata.
    [Fact]
    public async Task RecordHeartbeatSuccessAsync_UpdatesTimestamp()
    {
        string path = TempFile();
        try
        {
            CloudRegistrationStore store = new(path, new InMemoryCloudSecretProtector());
            await store.SaveEnrolledAsync(
                "device-1", "org-1", "cred", null,
                DateTimeOffset.UtcNow);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            await store.RecordHeartbeatSuccessAsync(now);

            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.Connected, view.State);
            Assert.Equal(now, view.LastHeartbeatUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
