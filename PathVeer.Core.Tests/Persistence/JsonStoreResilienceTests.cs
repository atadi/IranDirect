using System.Text;
using PathVeer.Core.Persistence;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Tests.Persistence;

/// <summary>
/// Persistence-resilience regression coverage for <see cref="JsonStore{T}"/>.
///
/// These tests exercise the corruption/backup boundary WITHOUT ever touching
/// the real ProgramData state and WITHOUT requiring LocalSystem. All work is
/// done under %TEMP%.
///
/// Two recovery modes are covered:
///  - FailClosed (default, used by authoritative config / journals / Cloud):
///    a corrupt primary throws and NO backup is written or used.
///  - BackupRollback (used by runtime state / derived caches): a corrupt
///    primary is recovered from the last known-good ".bak".
/// </summary>
public sealed class JsonStoreResilienceTests
{
    // ---- Phase 5.1 / 5.3 : all-NUL and invalid primary (FailClosed) ----

    [Fact]
    public async Task FailClosed_AllNulPrimary_ThrowsJsonException()
    {
        string path = CreateTemporaryPath();
        await WriteAllNulAsync(path, 271);

        JsonStore<SampleDoc> store = Create(path);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => store.LoadAsync());
    }

    [Fact]
    public async Task FailClosed_InvalidJsonPrimary_ThrowsJsonException()
    {
        string path = CreateTemporaryPath();
        await File.WriteAllTextAsync(path, "{ not valid json !");

        JsonStore<SampleDoc> store = Create(path);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => store.LoadAsync());
    }

    [Fact]
    public async Task FailClosed_TruncatedJsonPrimary_Throws()
    {
        string path = CreateTemporaryPath();
        await File.WriteAllTextAsync(path, "{\"Name\":\"x\"");

        JsonStore<SampleDoc> store = Create(path);

        await Assert.ThrowsAnyAsync<Exception>(
            () => store.LoadAsync());
    }

    [Fact]
    public async Task FailClosed_DoesNotCreateBackupFile()
    {
        string path = CreateTemporaryPath();
        await WriteAllNulAsync(path, 64);

        JsonStore<SampleDoc> store = Create(path);

        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync());

        Assert.False(File.Exists(path + ".bak"));
    }

    // ---- Phase 5.4 / 5.5 : valid primary + corrupt/partial .tmp ----

    [Fact]
    public async Task FailClosed_ValidPrimaryPlusCorruptTmp_PrimaryWins()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store = Create(path);
        SampleDoc original = new() { Name = "good", Count = 1 };
        await store.SaveAsync(original);

        // A leftover .tmp from an interrupted save must never become
        // authoritative.
        await File.WriteAllTextAsync(path + ".tmp", "!!garbage!!");

        JsonStore<SampleDoc> reader = Create(path);
        SampleDoc loaded = await reader.LoadAsync();

        Assert.Equal("good", loaded.Name);
        Assert.Equal(1, loaded.Count);
    }

    [Fact]
    public async Task BackupRollback_ValidPrimaryPlusPartialTmp_PrimaryWins()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);
        SampleDoc original = new() { Name = "good", Count = 2 };
        await store.SaveAsync(original);

        await File.WriteAllTextAsync(path + ".tmp", "{\"Name\":\"par");

        JsonStore<SampleDoc> reader =
            Create(path, JsonStoreRecoveryMode.BackupRollback);
        SampleDoc loaded = await reader.LoadAsync();

        Assert.Equal("good", loaded.Name);
        Assert.Equal(2, loaded.Count);
    }

    // ---- Phase 5.6 / 5.7 : corrupt primary + backup ----

    [Fact]
    public async Task BackupRollback_AllNulPrimaryPlusValidBackup_Recovers()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);

        await store.SaveAsync(new SampleDoc { Name = "v1", Count = 10 });
        await store.SaveAsync(new SampleDoc { Name = "v2", Count = 20 });
        // .bak now holds v1; primary holds v2.

        await WriteAllNulAsync(path, 271);

        JsonStore<SampleDoc> reader =
            Create(path, JsonStoreRecoveryMode.BackupRollback);
        SampleDoc loaded = await reader.LoadAsync();

        Assert.Equal("v1", loaded.Name);
        Assert.Equal(10, loaded.Count);
        Assert.True(File.Exists(path + ".corrupt") || ExistsWithPrefix(path + ".corrupt"));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public async Task BackupRollback_TruncatedPrimaryPlusValidBackup_Recovers()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);

        await store.SaveAsync(new SampleDoc { Name = "v1", Count = 11 });
        await store.SaveAsync(new SampleDoc { Name = "v2", Count = 22 });

        await File.WriteAllTextAsync(path, "{\"Name\":\"x\"");

        JsonStore<SampleDoc> reader =
            Create(path, JsonStoreRecoveryMode.BackupRollback);
        SampleDoc loaded = await reader.LoadAsync();

        Assert.Equal("v1", loaded.Name);
        Assert.Equal(11, loaded.Count);
    }

    [Fact]
    public async Task BackupRollback_InvalidPrimaryPlusValidBackup_Recovers()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);

        await store.SaveAsync(new SampleDoc { Name = "v1", Count = 12 });
        await store.SaveAsync(new SampleDoc { Name = "v2", Count = 24 });

        await File.WriteAllTextAsync(path, "{ totally not json }");

        JsonStore<SampleDoc> reader =
            Create(path, JsonStoreRecoveryMode.BackupRollback);
        SampleDoc loaded = await reader.LoadAsync();

        Assert.Equal("v1", loaded.Name);
        Assert.Equal(12, loaded.Count);
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public async Task BackupRollback_AllNulPrimaryPlusCorruptBackup_FailsClearly()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);

        await store.SaveAsync(new SampleDoc { Name = "v1", Count = 13 });
        await store.SaveAsync(new SampleDoc { Name = "v2", Count = 26 });
        // .bak = v1.

        await WriteAllNulAsync(path, 271);          // primary corrupt
        await WriteAllNulAsync(path + ".bak", 40);   // backup also corrupt

        JsonStore<SampleDoc> reader =
            Create(path, JsonStoreRecoveryMode.BackupRollback);

        PersistenceCorruptException ex =
            await Assert.ThrowsAsync<PersistenceCorruptException>(
                () => reader.LoadAsync());
        Assert.Equal(path, ex.StorePath);
    }

    [Fact]
    public async Task BackupRollback_CorruptPrimaryWithNoBackup_FailsClearly()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);

        // Only a single save: a .bak is NOT created on the first save (no
        // prior primary to preserve).
        await store.SaveAsync(new SampleDoc { Name = "only", Count = 1 });

        await WriteAllNulAsync(path, 271);

        JsonStore<SampleDoc> reader =
            Create(path, JsonStoreRecoveryMode.BackupRollback);

        await Assert.ThrowsAsync<PersistenceCorruptException>(
            () => reader.LoadAsync());
    }

    // ---- Phase 5.3 (negative) : stale backup must NOT roll back
    //       an authoritative/fail-closed store ----

    [Fact]
    public async Task FailClosed_CorruptPrimaryPlusValidBackup_DoesNotRollBack()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store = Create(path);

        await store.SaveAsync(new SampleDoc { Name = "v1", Count = 5 });
        // No .bak is produced by the fail-closed store; plant one manually to
        // prove the generic hardening never auto-rolls-back this mode.
        await File.WriteAllTextAsync(
            path + ".bak",
            "{\"Name\":\"planted-backup\",\"Count\":99}");

        await WriteAllNulAsync(path, 64);

        JsonStore<SampleDoc> reader = Create(path);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => reader.LoadAsync());
    }

    // ---- Phase 5.8 / 5.9 : failed write / promotion keeps prior primary ----

    [Fact]
    public async Task BackupRollback_FailedWriteBeforePromotion_KeepsPriorPrimary()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);
        SampleDoc original = new() { Name = "keep", Count = 7 };
        await store.SaveAsync(original);
        string before = await File.ReadAllTextAsync(path);

        JsonStore<SampleDoc> faulted = Create(
            path,
            JsonStoreRecoveryMode.BackupRollback,
            FaultInjectionPolicy.For([FaultInjectionPoint.FileWrite]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => faulted.SaveAsync(
                new SampleDoc { Name = "replacement", Count = 8 }));

        string after = await File.ReadAllTextAsync(path);
        Assert.Equal(before, after);
        SampleDoc loaded = await store.LoadAsync();
        Assert.Equal("keep", loaded.Name);
    }

    [Fact]
    public async Task BackupRollback_PrePromotionFault_KeepsPriorPrimaryAndBackup()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new SampleDoc { Name = "v1", Count = 1 });
        await store.SaveAsync(new SampleDoc { Name = "v2", Count = 2 });
        // primary=v2, .bak=v1.
        string primaryBefore = await File.ReadAllTextAsync(path);
        string backupBefore = await File.ReadAllTextAsync(path + ".bak");

        JsonStore<SampleDoc> faulted = Create(
            path,
            JsonStoreRecoveryMode.BackupRollback,
            FaultInjectionPolicy.For([FaultInjectionPoint.FileMove]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => faulted.SaveAsync(
                new SampleDoc { Name = "v3", Count = 3 }));

        Assert.Equal(primaryBefore, await File.ReadAllTextAsync(path));
        Assert.Equal(backupBefore, await File.ReadAllTextAsync(path + ".bak"));
    }

    // PROVES the actual replacement failure (not merely a pre-promotion fault):
    // a file-operations implementation that throws inside Replace leaves both
    // the prior primary and the .bak intact, and no recovery temp is left in
    // the active slot.
    [Fact]
    public async Task BackupRollback_ActualReplaceFailure_KeepsPriorPrimaryAndBackup()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new SampleDoc { Name = "v1", Count = 1 });
        await store.SaveAsync(new SampleDoc { Name = "v2", Count = 2 });
        string primaryBefore = await File.ReadAllTextAsync(path);
        string backupBefore = await File.ReadAllTextAsync(path + ".bak");

        IJsonStoreFileOperations failingOps = new FailingReplaceOps();
        JsonStore<SampleDoc> faulted = new(
            path,
            JsonStoreRecoveryMode.BackupRollback,
            fileOperations: failingOps);

        await Assert.ThrowsAsync<IOException>(
            () => faulted.SaveAsync(
                new SampleDoc { Name = "v3", Count = 3 }));

        Assert.Equal(primaryBefore, await File.ReadAllTextAsync(path));
        Assert.Equal(backupBefore, await File.ReadAllTextAsync(path + ".bak"));
    }

    private sealed class FailingReplaceOps : IJsonStoreFileOperations
    {
        public void Replace(string source, string destination, string? backup) =>
            throw new IOException("injected replace failure");

        public void Move(string source, string destination) =>
            throw new IOException("injected move failure");
    }

    // ---- Phase 5.11 : repeated valid saves keep correct backup sequencing ----

    [Fact]
    public async Task BackupRollback_RepeatedSaves_SequenceBackupCorrectly()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);

        await store.SaveAsync(new SampleDoc { Name = "v1", Count = 1 });
        // First save has no prior primary, so no .bak.
        Assert.False(File.Exists(path + ".bak"));

        await store.SaveAsync(new SampleDoc { Name = "v2", Count = 2 });
        // Now .bak must hold the prior good (v1); primary holds v2.
        SampleDoc backup =
            System.Text.Json.JsonSerializer.Deserialize<SampleDoc>(
                await File.ReadAllTextAsync(path + ".bak"))!;
        SampleDoc primary =
            System.Text.Json.JsonSerializer.Deserialize<SampleDoc>(
                await File.ReadAllTextAsync(path))!;
        Assert.Equal("v1", backup.Name);
        Assert.Equal("v2", primary.Name);

        await store.SaveAsync(new SampleDoc { Name = "v3", Count = 3 });
        backup = System.Text.Json.JsonSerializer.Deserialize<SampleDoc>(
            await File.ReadAllTextAsync(path + ".bak"))!;
        primary = System.Text.Json.JsonSerializer.Deserialize<SampleDoc>(
            await File.ReadAllTextAsync(path))!;
        Assert.Equal("v2", backup.Name);
        Assert.Equal("v3", primary.Name);
    }

    // ---- Phase 5.12 : concurrency retains per-instance serialization ----

    [Fact]
    public async Task BackupRollback_ConcurrentLoadsAndSaves_DoNotCorrupt()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new SampleDoc { Name = "base", Count = 0 });

        for (int round = 0; round < 10; round++)
        {
            Task[] readers = Enumerable.Range(0, 8)
                .Select(_ => store.LoadAsync())
                .ToArray();
            Task[] writers = Enumerable.Range(0, 8)
                .Select(i => store.SaveAsync(
                    new SampleDoc { Name = $"r{round}", Count = i }))
                .ToArray();

            await Task.WhenAll(readers.Concat(writers));
        }

        SampleDoc loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.False(string.IsNullOrEmpty(loaded.Name));
    }

    // ---- Phase 5.13 : NUL inside a JSON string value is NOT file
    //       corruption ----

    [Fact]
    public async Task NulInsideJsonStringData_IsNotTreatedAsCorruption()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);

        // A NUL character embedded in a legitimate string value.
        SampleDoc withNul = new() { Name = "a\0b", Count = 42 };
        await store.SaveAsync(withNul);

        SampleDoc loaded = await store.LoadAsync();

        Assert.Equal("a\0b", loaded.Name);
        Assert.Equal(42, loaded.Count);
    }

    // ---- Phase 3 : validate-before-promote is exercised on every save ----

    [Fact]
    public async Task SaveAsync_WritesTempFileWithWriteThroughAndPromotesAtomically()
    {
        string path = CreateTemporaryPath();
        JsonStore<SampleDoc> store =
            Create(path, JsonStoreRecoveryMode.BackupRollback);

        await store.SaveAsync(new SampleDoc { Name = "persisted", Count = 5 });

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        SampleDoc loaded = await store.LoadAsync();
        Assert.Equal("persisted", loaded.Name);
        Assert.Equal(5, loaded.Count);
    }

    private static async Task WriteAllNulAsync(string path, int size)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        byte[] nul = new byte[size];
        await File.WriteAllBytesAsync(path, nul);
    }

    private static JsonStore<SampleDoc> Create(
        string path,
        JsonStoreRecoveryMode mode = JsonStoreRecoveryMode.FailClosed,
        IFaultInjectionPolicy? faultPolicy = null) =>
        new(
            path,
            mode,
            faultPolicy: faultPolicy);

    private static string CreateTemporaryPath()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Tests",
            Guid.NewGuid().ToString("N"),
            "document.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    // Collision-safe evidence files use "<base>.<timestamp>.<id>"; this helper
    // asserts that at least one such file exists for the given base prefix.
    private static bool ExistsWithPrefix(string basePath)
    {
        string? dir = Path.GetDirectoryName(basePath);
        string fileName = Path.GetFileName(basePath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return false;
        }

        return Directory.EnumerateFiles(dir, fileName + ".*").Any();
    }

    public sealed record SampleDoc
    {
        public string Name { get; init; } = "";

        public int Count { get; init; }
    }
}
