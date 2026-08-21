using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PathVeer.Core.Cloud;
using PathVeer.Service.Cloud;
using Xunit;

namespace PathVeer.Service.Tests.Cloud;

/// <summary>
/// Proves existing D2 Cloud registrations are upgraded safely to the hardened
/// storage model (dedicated ACL-denied directory + CurrentUser DPAPI) without
/// losing the enrollment, and that migration is idempotent and fail-safe.
/// </summary>
public sealed class CloudRegistrationMigratorTests
{
    private static readonly byte[] s_legacyEntropy =
        Encoding.UTF8.GetBytes("PathVeer.Cloud.DeviceCredential.v1");

    private static string LegacyBlob(string secret)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] blob = ProtectedData.Protect(
            plaintext, s_legacyEntropy, DataProtectionScope.LocalMachine);
        Array.Clear(plaintext, 0, plaintext.Length);
        return Convert.ToBase64String(blob);
    }

    [Fact]
    public async Task MigrateAsync_LegacyLocalMachineBlob_ReProtectedToCurrentUser()
    {
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newDir = Path.Combine(root, "cloud");
            string newPath = Path.Combine(newDir, "cloud-registration.json");

            var legacyRecord = new CloudRegistrationRecord
            {
                State = CloudConnectionState.Connected,
                DeviceId = "dev-1",
                OrganizationId = "org-1",
                DeviceLabel = "Box",
                EnrolledAtUtc = DateTimeOffset.UtcNow,
                CredentialProtectedBase64 = LegacyBlob("old-cred")
            };
            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(legacyRecord,
                    JsonOptions()));

            var migrator = new CloudRegistrationMigrator(
                legacyPath,
                newPath,
                new WindowsDpapiCloudSecretProtector(),
                new LegacyCloudCredentialDecoder());

            bool migrated = await migrator.MigrateAsync();

            Assert.True(migrated);
            Assert.False(File.Exists(legacyPath),
                "legacy file must be removed after successful migration");

            var store = new CloudRegistrationStore(
                newPath, new WindowsDpapiCloudSecretProtector());
            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal("dev-1", view.DeviceId);
            Assert.Equal(CloudConnectionState.Connected, view.State);
            Assert.True(view.HasCredential);

            // The credential is recoverable via the NEW (CurrentUser) protector.
            string? cred = await store.GetCredentialAsync();
            Assert.Equal("old-cred", cred);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_IsIdempotent()
    {
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            var legacyRecord = new CloudRegistrationRecord
            {
                State = CloudConnectionState.Connected,
                DeviceId = "dev-1",
                OrganizationId = "org-1",
                CredentialProtectedBase64 = LegacyBlob("old-cred")
            };
            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(legacyRecord, JsonOptions()));

            var migrator = new CloudRegistrationMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector(),
                new LegacyCloudCredentialDecoder());

            Assert.True(await migrator.MigrateAsync());

            // Second run: new location already enrolled -> no-op.
            Assert.False(await migrator.MigrateAsync());
            Assert.False(File.Exists(legacyPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_RevokedLegacy_MigratedWithoutCredential()
    {
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            var legacyRecord = new CloudRegistrationRecord
            {
                State = CloudConnectionState.Revoked,
                DeviceId = "dev-1",
                OrganizationId = "org-1",
                CredentialRevoked = true,
                CredentialProtectedBase64 = LegacyBlob("old-cred")
            };
            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(legacyRecord, JsonOptions()));

            var migrator = new CloudRegistrationMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector(),
                new LegacyCloudCredentialDecoder());

            await migrator.MigrateAsync();

            var store = new CloudRegistrationStore(
                newPath, new WindowsDpapiCloudSecretProtector());
            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.Revoked, view.State);
            Assert.False(view.HasCredential);
            Assert.Null(await store.GetCredentialAsync());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_CorruptLegacy_NoLossNoThrow()
    {
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            // Garbage legacy file.
            await File.WriteAllTextAsync(legacyPath, "{ not json");

            var migrator = new CloudRegistrationMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector(),
                new LegacyCloudCredentialDecoder());

            // Must not throw; legacy file is preserved (operator can recover).
            await migrator.MigrateAsync();

            Assert.True(File.Exists(legacyPath),
                "corrupt legacy file must NOT be deleted");
            Assert.False(File.Exists(newPath),
                "no new enrollment must be fabricated");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_NoLegacy_NoOp()
    {
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            var migrator = new CloudRegistrationMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector(),
                new LegacyCloudCredentialDecoder());

            Assert.False(await migrator.MigrateAsync());
            Assert.False(File.Exists(newPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_LegacyNotDeletedBeforeNewPersistenceSucceeds()
    {
        // A fault-injecting protector that fails to PROTECT simulates a write
        // failure after the legacy was read. The failure must escape so the
        // startup wrapper can log a clear diagnostic; the legacy file must
        // survive so enrollment is not lost (it is never deleted before a
        // successful new persistence).
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            var legacyRecord = new CloudRegistrationRecord
            {
                State = CloudConnectionState.Connected,
                DeviceId = "dev-1",
                OrganizationId = "org-1",
                CredentialProtectedBase64 = LegacyBlob("old-cred")
            };
            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(legacyRecord, JsonOptions()));

            // Protector that throws on Protect -> migration cannot complete.
            var failingProtector = new FailingProtector();
            var migrator = new CloudRegistrationMigrator(
                legacyPath, newPath,
                failingProtector,
                new LegacyCloudCredentialDecoder());

            // The failure escapes so the caller can surface a clear diagnostic;
            // it must NOT silently claim success.
            await Assert.ThrowsAsync<CryptographicException>(
                () => migrator.MigrateAsync());

            Assert.True(File.Exists(legacyPath),
                "legacy file must survive a failed migration (no enrollment loss)");
            Assert.False(File.Exists(newPath),
                "no fabricated new registration on failure");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_UnusableNewState_RollsBackAndKeepsLegacy()
    {
        // A protector that Protects but whose Unprotect throws simulates a
        // verify-after-write failure. The new file must be removed and the
        // legacy preserved.
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            var legacyRecord = new CloudRegistrationRecord
            {
                State = CloudConnectionState.Connected,
                DeviceId = "dev-1",
                OrganizationId = "org-1",
                CredentialProtectedBase64 = LegacyBlob("old-cred")
            };
            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(legacyRecord, JsonOptions()));

            var migrator = new CloudRegistrationMigrator(
                legacyPath, newPath,
                new ProtectOnlyProtector(),
                new LegacyCloudCredentialDecoder());

            await migrator.MigrateAsync();

            Assert.True(File.Exists(legacyPath),
                "legacy must be kept when the new state is unusable");
            Assert.False(File.Exists(newPath),
                "unusable new file must be rolled back");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private sealed class FailingProtector : ICloudSecretProtector
    {
        public string Protect(string secret) =>
            throw new CryptographicException("simulated protect failure");

        public string Unprotect(string protectedSecret) =>
            throw new CryptographicException("unexpected");
    }

    private sealed class ProtectOnlyProtector : ICloudSecretProtector
    {
        // Protects fine, but Unprotect always fails -> verify-after-write
        // (which calls Unprotect) aborts the migration.
        public string Protect(string secret) =>
            "protected-but-unusable:" + secret;

        public string Unprotect(string protectedSecret) =>
            throw new CryptographicException("simulated unusable");
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true
    };

    private static string NewTempDir()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "pvcloud-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

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
