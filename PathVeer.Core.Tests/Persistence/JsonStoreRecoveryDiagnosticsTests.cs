using System.Text;
using PathVeer.Core.Persistence;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Tests.Persistence;

/// <summary>
/// Audit findings 2, 4, 5, 7, 8, 9: recovery preserves the validated .bak,
/// corruption is distinguished from transient I/O, UTF-8 validation is strict,
/// successful recovery is diagnosable, evidence naming is collision-safe, and
/// actual promotion failure preserves recoverability.
/// </summary>
public sealed class JsonStoreRecoveryDiagnosticsTests
{
    private sealed record Doc
    {
        public string Name { get; init; } = "";

        public int Count { get; init; }
    }

    private static JsonStore<Doc> Create(
        string path,
        JsonStoreRecoveryMode mode = JsonStoreRecoveryMode.FailClosed,
        JsonStoreRecoveryOptions? options = null,
        IFaultInjectionPolicy? faultPolicy = null,
        IJsonStoreFileOperations? fileOps = null) =>
        new(path, mode, options, faultPolicy: faultPolicy, fileOperations: fileOps);

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

    private static bool ExistsWithPrefix(string basePath)
    {
        string? dir = Path.GetDirectoryName(basePath);
        string fileName = Path.GetFileName(basePath);
        return dir is not null
            && Directory.Exists(dir)
            && Directory.EnumerateFiles(dir, fileName + ".*").Any();
    }

    // Finding 2: backup-preserving recovery. The validated .bak must remain
    // on disk and unchanged after recovery.
    [Fact]
    public async Task Recovery_PreservesValidatedBackup()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await store.SaveAsync(new Doc { Name = "v2", Count = 2 });
        // primary=v2, bak=v1.

        byte[] bakBytes = await File.ReadAllBytesAsync(path + ".bak");
        await File.WriteAllBytesAsync(path, new byte[271]); // all-NUL primary

        Doc loaded = await Create(path, JsonStoreRecoveryMode.BackupRollback)
            .LoadAsync();

        Assert.Equal("v1", loaded.Name);
        Assert.Equal(1, loaded.Count);
        // .bak still present and byte-identical (never consumed).
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(bakBytes, await File.ReadAllBytesAsync(path + ".bak"));
    }

    // Finding 2: failure at each recovery stage does not destroy the .bak.
    [Fact]
    public async Task Recovery_ReplacementFailure_KeepsValidBackup()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await store.SaveAsync(new Doc { Name = "v2", Count = 2 });

        byte[] bakBytes = await File.ReadAllBytesAsync(path + ".bak");
        await File.WriteAllBytesAsync(path, new byte[271]);

        IJsonStoreFileOperations failingOps = new FailingReplaceOps();
        JsonStore<Doc> faulted = Create(
            path, JsonStoreRecoveryMode.BackupRollback, fileOps: failingOps);

        await Assert.ThrowsAsync<IOException>(() => faulted.LoadAsync());

        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(bakBytes, await File.ReadAllBytesAsync(path + ".bak"));
    }

    private sealed class FailingReplaceOps : IJsonStoreFileOperations
    {
        public void Replace(string source, string destination, string? backup) =>
            throw new IOException("injected replace failure");

        public void Move(string source, string destination) =>
            throw new IOException("injected move failure");
    }

    // Finding 4: an inaccessible/transient primary read must NOT be classified
    // as corruption, and must NOT suppress the backup path.
    [Fact]
    public async Task TransientPrimaryIoFailure_NotCorruption_LeavesBackupUsable()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await store.SaveAsync(new Doc { Name = "v2", Count = 2 });
        // primary=v2, bak=v1.

        // Simulate a TRANSIENT (one-shot) read failure on the primary via
        // fault injection. The load must retry and recover the valid primary,
        // never classifying a transient access failure as corruption.
        JsonStore<Doc> faulted = Create(
            path,
            JsonStoreRecoveryMode.BackupRollback,
            faultPolicy: new OneShotFaultInjectionPolicy(FaultInjectionPoint.FileRead));

        // The load must not throw a corruption failure and must still be able
        // to read the valid primary on a retry (FileRead is a one-shot fault in
        // the policy, so the next call succeeds). The .bak is preserved.
        Doc loaded = await faulted.LoadAsync();
        Assert.Equal("v2", loaded.Name);
        Assert.True(File.Exists(path + ".bak"));
    }

    // Finding 4: a transient backup read must NOT be reported as a corrupt
    // backup (which would suppress recovery and fail the load).
    [Fact]
    public async Task TransientBackupIoFailure_NotCorruptBackup_PreventsRollback()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await store.SaveAsync(new Doc { Name = "v2", Count = 2 });

        await File.WriteAllBytesAsync(path, new byte[271]); // corrupt primary

        // The backup is transiently unreadable on first validation attempt.
        JsonStore<Doc> faulted = Create(
            path,
            JsonStoreRecoveryMode.BackupRollback,
            faultPolicy: new OneShotFaultInjectionPolicy(FaultInjectionPoint.FileRead));

        // One-shot FileRead fault: backup validation retries and succeeds, so
        // recovery proceeds instead of being reported as corrupt backup.
        Doc loaded = await faulted.LoadAsync();
        Assert.Equal("v1", loaded.Name);
        Assert.True(File.Exists(path + ".bak"));
    }

    // Finding 5: strict UTF-8. Malformed raw UTF-8 bytes must be rejected
    // (Corrupt), not silently replaced with U+FFFD via lenient GetString.
    [Fact]
    public async Task MalformedUtf8Primary_IsCorruptNotReplaced()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await store.SaveAsync(new Doc { Name = "v2", Count = 2 });

        // Invalid UTF-8 sequence (0xFF is never valid).
        await File.WriteAllBytesAsync(path, new byte[] { 0x7B, 0xFF, 0x7D });

        Doc loaded = await Create(path, JsonStoreRecoveryMode.BackupRollback)
            .LoadAsync();
        Assert.Equal("v1", loaded.Name); // recovered from .bak
    }

    // Finding 5: a legitimate escaped NUL / replacement char inside valid JSON
    // is NOT malformed raw UTF-8 and must still parse.
    [Fact]
    public async Task ValidJsonWithEscapedReplacementChar_Parses()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "normal", Count = 5 });

        // JSON containing an escaped replacement character is valid UTF-8 JSON.
        string json = "{\"Name\":\"a\\uFFFDb\",\"Count\":7}";
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(json));

        Doc loaded = await Create(path, JsonStoreRecoveryMode.BackupRollback)
            .LoadAsync();
        Assert.Equal("a\ufffdb", loaded.Name);
        Assert.Equal(7, loaded.Count);
    }

    // Finding 7: successful recovery produces a diagnostic event (path only,
    // no contents), with backup preserved and the evidence path reported.
    [Fact]
    public async Task SuccessfulRecovery_RaisesDiagnosticWithPathAndEvidence()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await store.SaveAsync(new Doc { Name = "v2", Count = 2 });

        await File.WriteAllBytesAsync(path, new byte[271]);

        JsonStoreRecoveryEventArgs? raised = null;
        JsonStore<Doc> reader = Create(
            path,
            JsonStoreRecoveryMode.BackupRollback,
            options: new JsonStoreRecoveryOptions
            {
                OnRecovery = e => raised = e
            });

        await reader.LoadAsync();

        Assert.NotNull(raised);
        Assert.True(raised!.Succeeded);
        Assert.Equal(path, raised.StorePath);
        Assert.True(raised.BackupPreserved);
        Assert.True(raised.EvidencePath is not null);
        // No document contents/secrets are surfaced.
        Assert.DoesNotContain("v1", raised.EvidencePath!);
    }

    // Finding 7: recovery failure is also reported.
    [Fact]
    public async Task RecoveryFailure_RaisesDiagnosticFailure()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await store.SaveAsync(new Doc { Name = "v2", Count = 2 });

        await File.WriteAllBytesAsync(path, new byte[271]);
        await File.WriteAllBytesAsync(path + ".bak", new byte[40]); // corrupt bak

        JsonStoreRecoveryEventArgs? raised = null;
        JsonStore<Doc> reader = Create(
            path,
            JsonStoreRecoveryMode.BackupRollback,
            options: new JsonStoreRecoveryOptions
            {
                OnRecovery = e => raised = e
            });

        await Assert.ThrowsAsync<PersistenceCorruptException>(
            () => reader.LoadAsync());

        Assert.NotNull(raised);
        Assert.False(raised!.Succeeded);
        Assert.Equal(path, raised.StorePath);
    }

    // Finding 8: two separate corruption/recovery incidents must not overwrite
    // the first incident's evidence artifact.
    [Fact]
    public async Task TwoIncidents_DistinctEvidenceArtifacts()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await store.SaveAsync(new Doc { Name = "v2", Count = 2 });

        await File.WriteAllBytesAsync(path, new byte[271]);
        await Create(path, JsonStoreRecoveryMode.BackupRollback).LoadAsync();
        // First incident evidence exists.
        Assert.True(ExistsWithPrefix(path + ".corrupt"));

        // Establish a new good primary, then a second incident.
        await store.SaveAsync(new Doc { Name = "v3", Count = 3 });
        await store.SaveAsync(new Doc { Name = "v4", Count = 4 });
        await File.WriteAllBytesAsync(path, new byte[271]);
        await Create(path, JsonStoreRecoveryMode.BackupRollback).LoadAsync();

        // Two distinct .corrupt evidence files (collision-safe names).
        int corruptCount = Directory.EnumerateFiles(
            Path.GetDirectoryName(path)!,
            Path.GetFileName(path + ".corrupt") + ".*").Count();
        Assert.Equal(2, corruptCount);
    }

    // Finding 9: actual promotion (replace) failure on a SAVE leaves the prior
    // primary and backup intact.
    [Fact]
    public async Task Save_ActualReplaceFailure_KeepsPrimaryAndBackup()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await store.SaveAsync(new Doc { Name = "v2", Count = 2 });
        string primaryBefore = await File.ReadAllTextAsync(path);
        string backupBefore = await File.ReadAllTextAsync(path + ".bak");

        IJsonStoreFileOperations failingOps = new FailingReplaceOps();
        JsonStore<Doc> faulted = Create(
            path, JsonStoreRecoveryMode.BackupRollback, fileOps: failingOps);

        await Assert.ThrowsAsync<IOException>(
            () => faulted.SaveAsync(new Doc { Name = "v3", Count = 3 }));

        Assert.Equal(primaryBefore, await File.ReadAllTextAsync(path));
        Assert.Equal(backupBefore, await File.ReadAllTextAsync(path + ".bak"));
    }

    // Audit finding 1, regression A/B: a corrupt primary with no valid backup
    // fails to load, and a SECOND load still fails (never silently defaults to
    // new T()). The corrupt primary is preserved in place between loads.
    [Fact]
    public async Task CorruptPrimary_NoBackup_FirstAndSecondLoadBothFail()
    {
        string path = CreateTemporaryPath();
        // Seed a valid primary, then corrupt it and remove any backup.
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await File.WriteAllBytesAsync(path, new byte[271]); // all-NUL => corrupt
        if (File.Exists(path + ".bak"))
        {
            File.Delete(path + ".bak");
        }

        JsonStore<Doc> reader = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await Assert.ThrowsAsync<PersistenceCorruptException>(
            () => reader.LoadAsync());

        // Crucially the corrupt primary is STILL present (not renamed away).
        Assert.True(File.Exists(path));
        byte[] bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0,
            "corrupt primary must remain in place");

        // Second load must STILL fail the same way, never default.
        await Assert.ThrowsAsync<PersistenceCorruptException>(
            () => reader.LoadAsync());
    }

    // Audit finding 1, regression C: corrupt primary + valid backup + injected
    // recovery temp write failure => backup preserved, primary remains
    // corruption-visible, subsequent load does not default.
    [Fact]
    public async Task CorruptPrimary_ValidBackup_RecoveryTempWriteFails_NoDefault()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await store.SaveAsync(new Doc { Name = "v2", Count = 2 });

        byte[] bakBytes = await File.ReadAllBytesAsync(path + ".bak");
        await File.WriteAllBytesAsync(path, new byte[271]); // corrupt primary

        JsonStore<Doc> faulted = Create(
            path,
            JsonStoreRecoveryMode.BackupRollback,
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.RecoveryTempWrite]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => faulted.LoadAsync());

        // Backup preserved and untouched.
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(bakBytes, await File.ReadAllBytesAsync(path + ".bak"));

        // Primary remains corrupt (not turned into Missing).
        Assert.True(File.Exists(path));
        byte[] primaryNow = await File.ReadAllBytesAsync(path);
        Assert.True(primaryNow.AsSpan().IndexOfAnyExcept((byte)0) < 0);

        // A subsequent load (without the fault) recovers from the valid backup
        // (it does NOT silently default to a new T()). The corrupt primary is
        // still present but recovery correctly falls back to the good .bak.
        Doc recovered = await Create(
            path, JsonStoreRecoveryMode.BackupRollback).LoadAsync();
        Assert.Equal("v1", recovered.Name);
    }

    // Audit finding 1, regression D: corrupt primary + valid backup + injected
    // actual recovery promotion (replace) failure => backup preserved, primary
    // remains corrupt, subsequent load retries recovery (not a default).
    [Fact]
    public async Task CorruptPrimary_ValidBackup_RecoveryPromotionFails_NoDefault()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await store.SaveAsync(new Doc { Name = "v2", Count = 2 });

        byte[] bakBytes = await File.ReadAllBytesAsync(path + ".bak");
        await File.WriteAllBytesAsync(path, new byte[271]); // corrupt primary

        JsonStore<Doc> faulted = Create(
            path,
            JsonStoreRecoveryMode.BackupRollback,
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.RecoveryPromotion]));

        await Assert.ThrowsAsync<FaultInjectionException>(
            () => faulted.LoadAsync());

        // Backup preserved and untouched.
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(bakBytes, await File.ReadAllBytesAsync(path + ".bak"));

        // Primary remains corrupt (never absented).
        Assert.True(File.Exists(path));
        byte[] primaryNow = await File.ReadAllBytesAsync(path);
        Assert.True(primaryNow.AsSpan().IndexOfAnyExcept((byte)0) < 0);

        // Subsequent load (no fault) recovers from the backup, NOT defaults.
        Doc recovered = await Create(
            path, JsonStoreRecoveryMode.BackupRollback).LoadAsync();
        Assert.Equal("v1", recovered.Name);
    }

    // Audit finding 2, regression: a transient primary read during Save must
    // NOT promote the temp without preserving the prior generation. With the
    // path-lock serializing, inject FileRead on the primary-validation step so
    // the save fails and the existing valid primary + backup survive.
    [Fact]
    public async Task Save_TransientPrimaryValidation_DoesNotPromoteWithoutBackup()
    {
        string path = CreateTemporaryPath();
        JsonStore<Doc> store = Create(path, JsonStoreRecoveryMode.BackupRollback);
        await store.SaveAsync(new Doc { Name = "v1", Count = 1 });
        await store.SaveAsync(new Doc { Name = "v2", Count = 2 });
        byte[] primaryBefore = await File.ReadAllBytesAsync(path);
        byte[] backupBefore = await File.ReadAllBytesAsync(path + ".bak");

        // Persistent (always) FileRead fault: the primary-validation read
        // during promotion keeps failing across retries, so the save must abort
        // and must NOT overwrite the primary without preserving the prior
        // generation. (The temp write-back read also fails transiently, which
        // is the same "transient I/O aborts the save" invariant.)
        JsonStore<Doc> faulted = Create(
            path,
            JsonStoreRecoveryMode.BackupRollback,
            faultPolicy: FaultInjectionPolicy.For(
                [FaultInjectionPoint.FileRead]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => faulted.SaveAsync(new Doc { Name = "v3", Count = 3 }));

        // Primary and backup are byte-preserved; v3 was NOT promoted.
        Assert.Equal(primaryBefore, await File.ReadAllBytesAsync(path));
        Assert.Equal(backupBefore, await File.ReadAllBytesAsync(path + ".bak"));
    }
}
