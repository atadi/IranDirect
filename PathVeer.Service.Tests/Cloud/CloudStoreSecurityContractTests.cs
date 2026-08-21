using System.Collections.Concurrent;
using PathVeer.Core.Cloud;
using PathVeer.Service.Cloud;
using Xunit;

namespace PathVeer.Service.Tests.Cloud;

/// <summary>
/// Tests for the corrected JsonStore security callback contract and for
/// treating an attacker-planted "Connected" registration as unusable.
/// </summary>
public sealed class CloudStoreSecurityContractTests
{
    [Fact]
    public void JsonStore_InvokesDistinctDirectoryAndFileCallbacks()
    {
        // Proves the directory-prepared callback (before .tmp) and the
        // file-persisted callback (after atomic move) are distinct and
        // receive the correct paths. The Cloud store wires the directory
        // callback to canonical directory hardening and the file callback to
        // canonical file hardening.
        var dirCalls = new ConcurrentQueue<string>();
        var fileCalls = new ConcurrentQueue<string>();

        string dir = NewTempDir();
        string finalPath = Path.Combine(dir, "cloud-registration.json");
        try
        {
            var store = new CloudRegistrationStore(
                finalPath,
                new InMemoryCloudSecretProtector(),
                onDirectoryPrepared: dirCalls.Enqueue,
                onFilePersisted: fileCalls.Enqueue);

            store.SaveAsync(
                new CloudRegistrationRecord
                {
                    State = CloudConnectionState.Connected,
                    DeviceId = "d",
                    OrganizationId = "o"
                }).GetAwaiter().GetResult();

            // The directory callback must have fired with the directory, and
            // the file callback with the final path — not the .tmp path.
            Assert.Contains(dir, dirCalls);
            Assert.Contains(finalPath, fileCalls);
            Assert.DoesNotContain(
                finalPath + ".tmp", fileCalls,
                StringComparer.Ordinal);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task GetViewAsync_DoesNotTrustPlantedConnectedDocument()
    {
        // An ordinary user pre-creates a "Connected" JSON document with a
        // garbage protected blob. The Service runs as LocalSystem and cannot
        // decrypt it, so HasUsableCredential must be false — the registration
        // is NOT trusted merely because IsEnrolled/State flags say so.
        string path = TempFile();
        try
        {
            File.WriteAllText(path,
                "{\"State\":1,\"DeviceId\":\"x\"," +
                "\"OrganizationId\":\"y\"," +
                "\"CredentialProtectedBase64\":\"!!!not-a-real-blob!!!\"," +
                "\"CredentialRevoked\":false}");

            var store = new CloudRegistrationStore(
                path, new InMemoryCloudSecretProtector());

            CloudRegistrationView view =
                await store.GetViewAsync();

            // Attacker JSON says "enrolled", but the Service cannot use it.
            Assert.True(view.State == CloudConnectionState.Connected);
            Assert.True(view.HasCredential);
            Assert.False(view.HasUsableCredential,
                "a planted document with an undecryptable blob must not be " +
                "reported as a usable registration");

            // Heartbeat path must therefore see no usable credential.
            Assert.Null(await store.GetCredentialAsync());
            Assert.False(
                await store.CanUseStoredCredentialAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetViewAsync_RealCurrentUserBlob_IsUsable()
    {
        // A genuine CurrentUser-protected registration IS reported usable by
        // the same Service that created it (LocalSystem context).
        string path = TempFile();
        try
        {
            var store = new CloudRegistrationStore(
                path, new WindowsDpapiCloudSecretProtector());

            await store.SaveEnrolledAsync(
                "device-1", "org-1", "super-secret-cred", null,
                DateTimeOffset.UtcNow);

            CloudRegistrationView view =
                await store.GetViewAsync();

            Assert.True(view.HasUsableCredential);
            Assert.Equal("super-secret-cred",
                await store.GetCredentialAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string NewTempDir() =>
        Path.Combine(
            Path.GetTempPath(),
            "pvcloud-ctr-" + Guid.NewGuid().ToString("N"));

    private static string TempFile() =>
        Path.Combine(
            Path.GetTempPath(),
            "pvcloud-ctr-" + Guid.NewGuid().ToString("N") + ".json");

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
