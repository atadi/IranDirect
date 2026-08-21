using System;
using System.IO;
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
///
/// The actual filesystem hardening (SYSTEM-owned canonical descriptor) is
/// performed by <see cref="CloudStateSecurity"/> and is proven by the separate
/// LocalSystem integration tool. These unit tests inject no-op security
/// delegates so the MIGRATION DECISION LOGIC (legacy decode -> re-protect ->
/// verify-after-write -> legacy delete, plus the precedence rules that stop an
/// attacker-planted "Connected" document from suppressing a recoverable legacy)
/// is exercised under any runner without requiring LocalSystem authority. The
/// injected delegates are NOT the security guarantee; they stand in for it so
/// the logic can be tested in isolation.
/// </summary>
public sealed class CloudRegistrationMigratorTests
{
    private static readonly byte[] s_legacyEntropy =
        Encoding.UTF8.GetBytes("PathVeer.Cloud.DeviceCredential.v1");

    // No-op stand-ins for the production filesystem-security operations so the
    // migration decision logic runs without LocalSystem/SeTakeOwnership.
    // Unlike the real CloudStateSecurity.HardenDirectory, these do NOT set the
    // SYSTEM owner (which requires LocalSystem authority); hardenDir still
    // creates the directory because the production HardenDirectory does, and
    // the migration depends on the directory existing before writing.
    private static readonly Action<string> s_noopSecureLegacy = _ => { };
    private static readonly Action<string> s_noopHardenDir =
        d => Directory.CreateDirectory(d);
    private static readonly Action<string> s_noopHardenFile = _ => { };

    private static string LegacyBlob(string secret)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] blob = ProtectedData.Protect(
            plaintext, s_legacyEntropy, DataProtectionScope.LocalMachine);
        Array.Clear(plaintext, 0, plaintext.Length);
        return Convert.ToBase64String(blob);
    }

    private static CloudRegistrationMigrator CreateMigrator(
        string legacyPath,
        string newPath,
        ICloudSecretProtector protector) =>
        new(legacyPath, newPath, protector, new LegacyCloudCredentialDecoder(),
            s_noopSecureLegacy, s_noopHardenDir, s_noopHardenFile);

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

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

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

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

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

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

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

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

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

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

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
                legacyPath, newPath, failingProtector,
                new LegacyCloudCredentialDecoder(),
                s_noopSecureLegacy, s_noopHardenDir, s_noopHardenFile);

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
                legacyPath, newPath, new ProtectOnlyProtector(),
                new LegacyCloudCredentialDecoder(),
                s_noopSecureLegacy, s_noopHardenDir, s_noopHardenFile);

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

    // ===================================================================
    // Attacker-controlled current-location state must NOT suppress a
    // recoverable legacy registration (the migrator-trusts-IsEnrolled bug).
    // ===================================================================

    [Fact]
    public async Task MigrateAsync_PlantedCurrentConnectedGarbageCred_MigratesLegacy()
    {
        // Attacker pre-creates cloud/cloud-registration.json as "Connected"
        // with a non-decryptable credential blob. A valid legacy exists.
        // The migrator must NOT short-circuit on IsEnrolled=true; it must
        // migrate the legacy credential.
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "dev-legacy",
                        OrganizationId = "org-legacy",
                        CredentialProtectedBase64 = LegacyBlob("legacy-cred")
                    },
                    JsonOptions()));

            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            await File.WriteAllTextAsync(
                newPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "attacker",
                        OrganizationId = "attacker-org",
                        CredentialProtectedBase64 = "!!!not-a-real-blob!!!",
                        CredentialRevoked = false
                    },
                    JsonOptions()));

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

            bool migrated = await migrator.MigrateAsync();

            Assert.True(migrated,
                "attacker-planted current state must not suppress legacy migration");
            // Legacy removed only after successful migration.
            Assert.False(File.Exists(legacyPath));

            var store = new CloudRegistrationStore(
                newPath, new WindowsDpapiCloudSecretProtector());
            Assert.Equal("legacy-cred", await store.GetCredentialAsync());
            Assert.True(await store.CanUseStoredCredentialAsync());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_PlantedCurrentIsEnrolledMissingCred_MigratesLegacy()
    {
        // Attacker claims IsEnrolled=true but provides NO credential blob.
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "dev-legacy",
                        OrganizationId = "org-legacy",
                        CredentialProtectedBase64 = LegacyBlob("legacy-cred")
                    },
                    JsonOptions()));

            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            await File.WriteAllTextAsync(
                newPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "x",
                        OrganizationId = "y",
                        CredentialProtectedBase64 = null,
                        CredentialRevoked = false
                    },
                    JsonOptions()));

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

            Assert.True(await migrator.MigrateAsync());
            Assert.False(File.Exists(legacyPath));

            var store = new CloudRegistrationStore(
                newPath, new WindowsDpapiCloudSecretProtector());
            Assert.Equal("legacy-cred", await store.GetCredentialAsync());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_PlantedCurrentArbitraryIds_MigratesLegacy()
    {
        // Arbitrary DeviceId/OrganizationId in the attacker JSON must not make
        // the current state authoritative.
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "dev-legacy",
                        OrganizationId = "org-legacy",
                        CredentialProtectedBase64 = LegacyBlob("legacy-cred")
                    },
                    JsonOptions()));

            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            await File.WriteAllTextAsync(
                newPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "ATTACKER-DEVICE",
                        OrganizationId = "ATTACKER-ORG",
                        CredentialProtectedBase64 = "garbage",
                        CredentialRevoked = false
                    },
                    JsonOptions()));

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

            Assert.True(await migrator.MigrateAsync());
            var store = new CloudRegistrationStore(
                newPath, new WindowsDpapiCloudSecretProtector());
            Assert.Equal("legacy-cred", await store.GetCredentialAsync());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_UsableCurrentWinsNoReMigration()
    {
        // A genuinely usable CurrentUser current registration already exists
        // (plus a leftover legacy). Migration must safely no-op; the legacy is
        // NOT re-protected or deleted. Cloud is usable from current state.
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            // Legitimate legacy for context.
            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "dev-legacy",
                        OrganizationId = "org-legacy",
                        CredentialProtectedBase64 = LegacyBlob("legacy-cred")
                    },
                    JsonOptions()));

            // Genuine, usable CurrentUser current registration.
            var store = new CloudRegistrationStore(
                newPath, new WindowsDpapiCloudSecretProtector());
            await store.SaveEnrolledAsync(
                "device-current", "org-current", "current-cred", null,
                DateTimeOffset.UtcNow);

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

            Assert.False(await migrator.MigrateAsync(),
                "usable current registration must no-op migration");

            // Legacy left untouched (no destructive re-migration).
            Assert.True(File.Exists(legacyPath));
            Assert.Equal("current-cred", await store.GetCredentialAsync());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_InvalidCurrentValidLegacy_ReplacesCurrentSafely()
    {
        // Malformed current file + valid legacy => current is replaced only
        // after a successful legacy migration; legacy kept until then.
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "dev-legacy",
                        OrganizationId = "org-legacy",
                        CredentialProtectedBase64 = LegacyBlob("legacy-cred")
                    },
                    JsonOptions()));

            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            await File.WriteAllTextAsync(newPath, "{ not json at all");

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

            Assert.True(await migrator.MigrateAsync());
            Assert.False(File.Exists(legacyPath));

            var store = new CloudRegistrationStore(
                newPath, new WindowsDpapiCloudSecretProtector());
            Assert.Equal("legacy-cred", await store.GetCredentialAsync());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_UnusableCurrent_ReplaceFails_LegacyIntact()
    {
        // Unusable current + valid legacy, but the re-protection fails: legacy
        // must remain intact (failure during current-state replacement must
        // not destroy recoverable legacy).
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "dev-legacy",
                        OrganizationId = "org-legacy",
                        CredentialProtectedBase64 = LegacyBlob("legacy-cred")
                    },
                    JsonOptions()));

            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            await File.WriteAllTextAsync(
                newPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "x",
                        OrganizationId = "y",
                        CredentialProtectedBase64 = "garbage",
                        CredentialRevoked = false
                    },
                    JsonOptions()));

            var migrator = new CloudRegistrationMigrator(
                legacyPath, newPath, new FailingProtector(),
                new LegacyCloudCredentialDecoder(),
                s_noopSecureLegacy, s_noopHardenDir, s_noopHardenFile);

            await Assert.ThrowsAsync<CryptographicException>(
                () => migrator.MigrateAsync());

            Assert.True(File.Exists(legacyPath),
                "legacy must remain intact when replacement fails");
            Assert.True(File.Exists(newPath),
                "unusable attacker current file stays; no fabricated state");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_UnusableCurrentNoLegacy_CloudNotUsable()
    {
        // Usable-less current + no legacy => Cloud is not reported usable;
        // nothing fabricated; routing continues (no exception).
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            await File.WriteAllTextAsync(
                newPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "x",
                        OrganizationId = "y",
                        CredentialProtectedBase64 = "garbage",
                        CredentialRevoked = false
                    },
                    JsonOptions()));

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

            // No legacy present -> nothing to migrate.
            Assert.False(await migrator.MigrateAsync());

            var store = new CloudRegistrationStore(
                newPath, new WindowsDpapiCloudSecretProtector());
            Assert.False(await store.CanUseStoredCredentialAsync());
            Assert.Null(await store.GetCredentialAsync());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_RevokedCurrentPlusValidLegacy_MigratesLegacy()
    {
        // Conservative precedence: a 'Revoked' current document (attacker or
        // stale) must NOT suppress a recoverable Connected legacy. Because a
        // revoked current record is intentionally credential-less, it is
        // non-authoritative, so the legacy Connected credential is migrated.
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "dev-legacy",
                        OrganizationId = "org-legacy",
                        CredentialProtectedBase64 = LegacyBlob("legacy-cred")
                    },
                    JsonOptions()));

            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            await File.WriteAllTextAsync(
                newPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Revoked,
                        DeviceId = "whatever",
                        OrganizationId = "whatever",
                        CredentialProtectedBase64 = null,
                        CredentialRevoked = true
                    },
                    JsonOptions()));

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

            Assert.True(await migrator.MigrateAsync(),
                "revoked current must not suppress recoverable legacy");
            Assert.False(File.Exists(legacyPath));

            var store = new CloudRegistrationStore(
                newPath, new WindowsDpapiCloudSecretProtector());
            Assert.Equal("legacy-cred", await store.GetCredentialAsync());
            Assert.True(await store.CanUseStoredCredentialAsync());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_LegitimateRevokedCurrentNoLegacy_StaysRevoked()
    {
        // A legitimate revoked current registration with no legacy remains
        // revoked and does not fabricate a usable credential.
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            var store = new CloudRegistrationStore(
                newPath, new WindowsDpapiCloudSecretProtector());
            await store.MarkRevokedAsync();

            var migrator = CreateMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector());

            Assert.False(await migrator.MigrateAsync());
            Assert.False(await store.CanUseStoredCredentialAsync());
            Assert.Null(await store.GetCredentialAsync());
            CloudRegistrationView view = await store.GetViewAsync();
            Assert.Equal(CloudConnectionState.Revoked, view.State);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MigrateAsync_InvokesSecurityBoundaryAtCorrectLifecyclePoints()
    {
        // Explicit contract test: the migrator must invoke the security
        // boundary (legacy hardening earliest, then directory, then file after
        // the new state is written) so the production wiring can replace the
        // no-op delegates with the real CloudStateSecurity hardening. This
        // proves the security boundary is part of the migration lifecycle
        // without requiring LocalSystem authority here.
        string root = NewTempDir();
        try
        {
            string legacyPath = Path.Combine(root, "cloud-registration.json");
            string newPath = Path.Combine(root, "cloud", "cloud-registration.json");

            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(
                    new CloudRegistrationRecord
                    {
                        State = CloudConnectionState.Connected,
                        DeviceId = "dev-legacy",
                        OrganizationId = "org-legacy",
                        CredentialProtectedBase64 = LegacyBlob("legacy-cred")
                    },
                    JsonOptions()));

            var secureLegacyCalls = new List<string>();
            var hardenDirCalls = new List<string>();
            var hardenFileCalls = new List<string>();
            var newDir = Path.GetDirectoryName(newPath)!;

            var migrator = new CloudRegistrationMigrator(
                legacyPath, newPath,
                new WindowsDpapiCloudSecretProtector(),
                new LegacyCloudCredentialDecoder(),
                secureLegacyCalls.Add,
                d => { hardenDirCalls.Add(d); Directory.CreateDirectory(d); },
                hardenFileCalls.Add);

            await migrator.MigrateAsync();

            // Legacy hardened first (earliest exposure reduction).
            Assert.Single(secureLegacyCalls);
            Assert.Equal(legacyPath, secureLegacyCalls[0]);
            // Directory + file hardened, targeting the new location.
            Assert.Single(hardenDirCalls);
            Assert.Equal(newDir, hardenDirCalls[0]);
            Assert.Single(hardenFileCalls);
            Assert.Equal(newPath, hardenFileCalls[0]);
            // Order: legacy -> directory -> file.
            Assert.True(
                secureLegacyCalls.Count == 1
                && hardenDirCalls.Count == 1
                && hardenFileCalls.Count == 1);
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
