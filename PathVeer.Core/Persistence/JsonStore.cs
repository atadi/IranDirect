using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Persistence;

/// <summary>
/// Recovery policy for a <see cref="JsonStore{T}"/>.
///
/// This is NOT a global "backup everything" switch. Each store is classified
/// by its business authority semantics (see the persistence-hardening change)
/// and only stores whose stale/previous copy is SAFE to restore are allowed
/// automatic backup rollback. Authoritative configuration, mutation journals,
/// and security/credential state must stay fail-closed: a stale copy of those
/// files could cause network mutation or violate revocation/authority.
///
/// The enum is intentionally internal: the only two stores that use it are
/// in-assembly (StateRepository, CustomRouteDnsCacheStore) and the test suite
/// reaches it through InternalsVisibleTo. The PUBLIC JsonStore constructor does
/// NOT accept it, so a caller outside this assembly cannot opt into backup
/// rollback and the public API contract is unchanged.
/// </summary>
internal enum JsonStoreRecoveryMode
{
    /// <summary>
    /// Default. A malformed/corrupt primary throws (preserving any existing
    /// caller-specific corruption semantics). No backup is written or used.
    /// </summary>
    FailClosed = 0,

    /// <summary>
    /// The previous known-good primary is preserved as a ".bak" on every
    /// successful save, and a corrupt primary is recovered from that ".bak"
    /// (with the corrupt primary quarantined) instead of bricking the caller.
    /// Only for stores whose previous copy is safe to restore (runtime state,
    /// derived caches).
    /// </summary>
    BackupRollback = 1
}

/// <summary>
/// Thrown by <see cref="JsonStore{T}"/> when a primary document cannot be
/// parsed/validated. Wraps the original failure so a fail-closed caller keeps
/// seeing its native exception type (e.g. <see cref="JsonException"/>), while a
/// backup-rollback caller can detect "corruption" distinctly from transient
/// I/O.
/// </summary>
internal sealed class JsonStoreCorruptionException : Exception
{
    public JsonStoreCorruptionException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Raised when a store marked for backup recovery has neither a valid primary
/// nor a valid backup. This is a clear, non-silent failure: it does NOT
/// substitute a default object.
/// </summary>
public sealed class PersistenceCorruptException : Exception
{
    public string StorePath { get; }

    public PersistenceCorruptException(string storePath, string message)
        : base(message)
    {
        StorePath = storePath;
    }

    public PersistenceCorruptException(
        string storePath,
        string message,
        Exception inner)
        : base(message, inner)
    {
        StorePath = storePath;
    }
}

/// <summary>
/// Outcome of reading + parsing a candidate document. This separates genuine
/// corruption (bytes were read and content validation failed) from I/O or
/// access failures (which must NOT be treated as corruption).
/// </summary>
internal enum DocumentReadResult
{
    /// <summary>The file does not exist (or vanished between stat and open).</summary>
    Missing,

    /// <summary>Bytes were read but the content is not valid T.</summary>
    Corrupt,

    /// <summary>The file could not be read due to I/O or access failure.</summary>
    TransientIoFailure,

    /// <summary>The file was read and parsed successfully.</summary>
    Valid
}

/// <summary>
/// Diagnostic detail surfaced through <see cref="JsonStoreRecoveryOptions.OnRecovery"/>
/// so Service/runtime logging can observe a recovery event without any document
/// contents, Cloud credentials, or enrollment secrets being exposed. Only the
/// store path, the result, and (when present) the quarantine evidence path are
/// reported.
/// </summary>
public sealed class JsonStoreRecoveryEventArgs : EventArgs
{
    public string StorePath { get; }

    public bool Succeeded { get; }

    public string? FailureReason { get; }

    public string? EvidencePath { get; }

    public bool BackupPreserved { get; }

    public JsonStoreRecoveryEventArgs(
        string storePath,
        bool succeeded,
        string? failureReason,
        string? evidencePath,
        bool backupPreserved)
    {
        StorePath = storePath;
        Succeeded = succeeded;
        FailureReason = failureReason;
        EvidencePath = evidencePath;
        BackupPreserved = backupPreserved;
    }
}

/// <summary>
/// Options controlling recovery diagnostics. Passed through the internal
/// constructor so the two opt-in stores can wire a Service-level logger, while
/// ordinary consumers keep the public constructor contract untouched.
/// </summary>
public sealed class JsonStoreRecoveryOptions
{
    /// <summary>
    /// Invoked exactly once when a backup-rollback recovery attempt finishes
    /// (succeeded or failed). Never carries document contents or secrets.
    /// </summary>
    public Action<JsonStoreRecoveryEventArgs>? OnRecovery { get; init; }
}

/// <summary>
/// Abstraction over the low-level file operations that can fail during
/// promotion. The default implementation is a thin wrapper over
/// <see cref="File"/>; tests substitute an implementation that throws at the
/// actual replacement step so the "promotion failure leaves a trustworthy
/// primary/backup" invariant can be proven deterministically (the prior
/// FaultInjectionPoint.FileMove only fired *before* the replace, which is a
/// different boundary).
///
/// This seam is internal: only Core internals and the persistence test suite
/// (via InternalsVisibleTo) construct it. It is NOT part of the public
/// persistence API.
/// </summary>
internal interface IJsonStoreFileOperations
{
    // Used when the destination/current document already exists: atomic
    // replace, optionally preserving the replaced document as a backup.
    void Replace(string source, string destination, string? backup);

    // Used when there is no current document to replace (first save, or a
    // corrupt primary already quarantined away): a simple rename/move.
    void Move(string source, string destination);
}

internal sealed class DefaultJsonStoreFileOperations : IJsonStoreFileOperations
{
    public void Replace(string source, string destination, string? backup) =>
        File.Replace(source, destination, backup);

    public void Move(string source, string destination) =>
        File.Move(source, destination);
}

/// <summary>
/// Process-wide registry of path-scoped locks so that, within one PathVeer
/// process, every <see cref="JsonStore{T}"/> targeting the same canonical file
/// path serializes Load/Save/Mutate/promotion/recovery against each other.
///
/// Canonicalization uses the fully-qualified path with Windows
/// case-insensitive, slash-normalized semantics, so paths that differ only by
/// casing or '\' vs '/' resolve to the same lock boundary. The registry does
/// NOT track cross-process writers: PathVeer runs a single Service authority
/// per machine, and this lock only coordinates in-process instances.
///
/// The registry holds the lock objects with weak references so entries are
/// collected once every in-process store for a path is gone, preventing
/// unbounded growth without explicit disposal.
/// </summary>
internal static class JsonStoreLockRegistry
{
    private static readonly ConcurrentDictionary<
        string, WeakReference<SemaphoreSlim>> Locks = new(
            StringComparer.OrdinalIgnoreCase);

    public static SemaphoreSlim GetLock(string path)
    {
        string key = Canonicalize(path);

        // Loop until the dictionary holds a live lock for this canonical key.
        // Weak references mean a collected semaphore can leave a dead entry
        // behind; we must overwrite a dead entry rather than spin on it.
        while (true)
        {
            if (Locks.TryGetValue(key, out WeakReference<SemaphoreSlim> existingRef)
                && existingRef is not null
                && existingRef.TryGetTarget(out SemaphoreSlim? live)
                && live is not null)
            {
                return live;
            }

            SemaphoreSlim created = new(1, 1);
            WeakReference<SemaphoreSlim> createdRef = new(created);

            // Keep a live concurrent entry (a race may have just added one);
            // otherwise replace the dead/absent entry with the new lock.
            Locks.AddOrUpdate(
                key,
                createdRef,
                (_, old) =>
                {
                    if (old is null)
                    {
                        return createdRef;
                    }

                    SemaphoreSlim? liveTarget;
                    return old.TryGetTarget(out liveTarget) && liveTarget is not null
                        ? old
                        : createdRef;
                });
        }
    }

    private static string Canonicalize(string path)
    {
        // Always resolve to the fully-qualified absolute form so both rooted
        // and relative inputs (and '.' / '..' segments, and '/' vs '\'
        // separators) collapse to the same canonical key. The dictionary's
        // OrdinalIgnoreCase comparer handles Windows casing. We do NOT resolve
        // symlinks (out of scope for the single-authority model).
        string full = Path.GetFullPath(path);
        return full.Replace('/', '\\');
    }
}

public class JsonStore<T>
    where T : class, new()
{
    private const int MaxFileAccessAttempts = 5;

    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IFaultInjectionPolicy _faultPolicy;
    private readonly Action<string>? _onDirectoryPrepared;
    private readonly Action<string>? _onFilePersisted;
    private readonly JsonStoreRecoveryMode _recoveryMode;
    private readonly IJsonStoreFileOperations _fileOps;
    private readonly Action<JsonStoreRecoveryEventArgs>? _onRecovery;

    // Per-store instance lock. Serializes file access for this instance. The
    // cross-instance boundary is the path-scoped lock from JsonStoreLockRegistry.
    private readonly SemaphoreSlim _instanceLock = new(1, 1);

    // Path-scoped lock shared by every in-process store targeting the same
    // canonical file path. Acquired before _instanceLock on every public
    // operation so two independent instances for the same path never overlap
    // on Load/Save/Mutate/promotion/recovery.
    private readonly SemaphoreSlim _pathLock;

    // Public contract is unchanged.
    public JsonStore(
        string path,
        JsonSerializerOptions? jsonOptions = null,
        IFaultInjectionPolicy? faultPolicy = null,
        Action<string>? onDirectoryPrepared = null,
        Action<string>? onFilePersisted = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _jsonOptions =
            jsonOptions
            ?? new JsonSerializerOptions
            {
                WriteIndented = true
            };
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
        _onDirectoryPrepared = onDirectoryPrepared;
        _onFilePersisted = onFilePersisted;
        _recoveryMode = JsonStoreRecoveryMode.FailClosed;
        _fileOps = new DefaultJsonStoreFileOperations();
        _onRecovery = null;
        _pathLock = JsonStoreLockRegistry.GetLock(_path);
    }

    // Internal: used only by the two stores whose stale copy is safe to restore.
    internal JsonStore(
        string path,
        JsonStoreRecoveryMode recoveryMode,
        JsonStoreRecoveryOptions? recoveryOptions = null,
        JsonSerializerOptions? jsonOptions = null,
        IFaultInjectionPolicy? faultPolicy = null,
        Action<string>? onDirectoryPrepared = null,
        Action<string>? onFilePersisted = null,
        IJsonStoreFileOperations? fileOperations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _jsonOptions =
            jsonOptions
            ?? new JsonSerializerOptions
            {
                WriteIndented = true
            };
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
        _onDirectoryPrepared = onDirectoryPrepared;
        _onFilePersisted = onFilePersisted;
        _recoveryMode = recoveryMode;
        _fileOps = fileOperations ?? new DefaultJsonStoreFileOperations();
        _onRecovery = recoveryOptions?.OnRecovery;
        _pathLock = JsonStoreLockRegistry.GetLock(_path);
    }

    public virtual async Task<T> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _pathLock.WaitAsync(cancellationToken);

        try
        {
            await _instanceLock.WaitAsync(cancellationToken);

            try
            {
                return await LoadCoreAsync(cancellationToken);
            }
            finally
            {
                _instanceLock.Release();
            }
        }
        finally
        {
            _pathLock.Release();
        }
    }

    public virtual async Task SaveAsync(
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        await _pathLock.WaitAsync(cancellationToken);

        try
        {
            await _instanceLock.WaitAsync(cancellationToken);

            try
            {
                await SaveCoreAsync(value, cancellationToken);
            }
            finally
            {
                _instanceLock.Release();
            }
        }
        finally
        {
            _pathLock.Release();
        }
    }

    // Performs a read-modify-write atomically under both the path lock and the
    // instance lock so that the mutation never observes or produces an
    // intermediate state of the underlying document, and never races another
    // in-process store for the same path.
    public async Task<T> MutateAsync(
        Func<T, Task<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        await _pathLock.WaitAsync(cancellationToken);

        try
        {
            await _instanceLock.WaitAsync(cancellationToken);

            try
            {
                T value = await LoadCoreAsync(cancellationToken);

                value = await mutation(value);

                await SaveCoreAsync(value, cancellationToken);

                return value;
            }
            finally
            {
                _instanceLock.Release();
            }
        }
        finally
        {
            _pathLock.Release();
        }
    }

    private async Task<T> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            (DocumentReadResult result, T? value) =
                await ReadDocumentAsync(_path);

            switch (result)
            {
                case DocumentReadResult.Missing:
                    return new T();

                case DocumentReadResult.Valid when value is not null:
                    return value;

                case DocumentReadResult.Corrupt:
                    if (_recoveryMode == JsonStoreRecoveryMode.BackupRollback)
                    {
                        // Corruption is not transient. Attempt recovery from the
                        // known-good ".bak" (which preserves the backup).
                        return await RecoverFromBackupOrThrow(
                            new JsonStoreCorruptionException(
                                $"Primary document at '{_path}' is corrupt.",
                                new JsonException(
                                    $"Primary document at '{_path}' is corrupt.")),
                            cancellationToken);
                    }

                    // Fail-closed: preserve the native JsonException contract so
                    // existing callers (DesiredConfigurationStore,
                    // CloudRegistrationStore, ...) keep seeing the exact
                    // exception type they already rely on. No backup is used.
                    throw new JsonException(
                        $"Primary document at '{_path}' is corrupt.");

                case DocumentReadResult.TransientIoFailure:
                    // Genuine I/O/access failure: retry with backoff (bounded).
                    if (attempt < MaxFileAccessAttempts - 1)
                    {
                        await BackoffAsync(attempt, cancellationToken);
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Primary document at '{_path}' could not be read " +
                        $"after {MaxFileAccessAttempts} attempts.");

                default:
                    // Should not happen, but treat as missing rather than
                    // silently defaulting a corrupt document.
                    return new T();
            }
        }
    }

    // Recovery invariant (fixes prior defect): the validated .bak is NEVER
    // consumed/destroyed. We build a SEPARATE recovery temp from the backup,
    // validate it, then atomically promote it to primary via File.Replace.
    // The original .bak stays on disk and remains valid for the next incident.
    // Recovery transaction order (audit finding 1):
    //   1. validate the .bak FIRST, without touching the corrupt primary
    //   2. if backup is missing/invalid/unreadable: leave the corrupt primary
    //      IN PLACE, emit a failure diagnostic, throw. The next load is STILL a
    //      corrupt primary -> consistent corruption/recovery path, NEVER Missing.
    //   3. if backup is valid: build a unique recovery temp, re-validate it,
    //      preserve the corrupt primary as COPY evidence (never move it away),
    //      then atomically Replace the corrupt primary with the validated temp.
    //      The .bak is never consumed and remains valid.
    //
    // Invariant after ANY stage failure:
    //   - the .bak is valid and untouched
    //   - the primary is either the original corrupt primary or a valid
    //     recovered primary (it is never absent/renamed such that the next
    //     LoadAsync returns new T())
    //   - a failed recovery fails clearly, not silently.
    private async Task<T> RecoverFromBackupOrThrow(
        JsonStoreCorruptionException corruption,
        CancellationToken cancellationToken = default)
    {
        string backupPath = _path + ".bak";

        // 1. Validate the backup WITHOUT consuming it. A transient read/access
        // failure on the backup must NOT be reported as a corrupt backup
        // (finding 4): retry with backoff before concluding.
        (DocumentReadResult bakResult, T? backupValue) =
            (DocumentReadResult.TransientIoFailure, (T?)null);
        for (int attempt = 0; attempt < MaxFileAccessAttempts; attempt++)
        {
            (bakResult, backupValue) = await ReadDocumentAsync(backupPath);
            if (bakResult != DocumentReadResult.TransientIoFailure)
            {
                break;
            }

            await BackoffAsync(attempt, cancellationToken);
        }

        if (bakResult != DocumentReadResult.Valid || backupValue is null)
        {
            // 2. No usable backup. Leave the corrupt primary exactly where it
            // is so the next load re-enters the corruption/recovery path
            // (never Missing/default). Emit a failure diagnostic and fail.
            _onRecovery?.Invoke(new JsonStoreRecoveryEventArgs(
                _path,
                succeeded: false,
                failureReason: "No valid backup available for recovery.",
                evidencePath: null,
                backupPreserved: File.Exists(backupPath)));

            throw new PersistenceCorruptException(
                _path,
                $"Primary document at '{_path}' is corrupt and no valid " +
                $"backup ('.bak') is available for recovery.",
                corruption);
        }

        // 3. Backup is valid. Build a unique recovery temp, re-validate it, then
        // atomically replace the corrupt primary with it.
        string recoveryTemp = _path + ".recovery.tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await WriteDocumentAsync(
                recoveryTemp, backupValue, cancellationToken: default);

            if (ShouldFailAt(FaultInjectionPoint.RecoveryTempWrite))
            {
                throw new FaultInjectionException(
                    FaultInjectionPoint.RecoveryTempWrite);
            }

            (DocumentReadResult tempResult, _) =
                await ReadDocumentAsync(recoveryTemp);

            if (tempResult != DocumentReadResult.Valid)
            {
                // The recovery temp could not be re-validated (should not
                // happen since the source was valid, but be defensive). The
                // corrupt primary and the .bak are both left intact.
                _onRecovery?.Invoke(new JsonStoreRecoveryEventArgs(
                    _path,
                    succeeded: false,
                    failureReason:
                        "Recovered backup could not be re-validated " +
                        "into a temp document.",
                    evidencePath: null,
                    backupPreserved: true));

                throw new PersistenceCorruptException(
                    _path,
                    "Primary is corrupt and the recovered backup could " +
                    "not be re-validated.",
                    corruption);
            }

            // Copy the corrupt primary to collision-safe evidence BEFORE
            // replacing it, so a failed promotion still leaves the original
            // corrupt primary on disk (it is never moved away first, so the
            // next load cannot observe a Missing file).
            string? evidencePath = CopyCorruptPrimaryEvidence();

            // Inject a promotion failure BEFORE the atomic replace so the test
            // can verify the corrupt primary stays put (never turned Missing).
            if (ShouldFailAt(FaultInjectionPoint.RecoveryPromotion))
            {
                throw new FaultInjectionException(
                    FaultInjectionPoint.RecoveryPromotion);
            }

            // Atomically replace the corrupt primary with the validated
            // recovery temp. backup is null on purpose: the valid .bak stays
            // in place. On failure, the corrupt primary is unchanged.
            try
            {
                _fileOps.Replace(recoveryTemp, _path, backup: null);
            }
            catch (Exception)
            {
                // Promotion failed: the corrupt primary is still present and
                // the .bak is untouched. Leave evidence (if any) for diagnosis
                // and throw so the next load retries/recovers rather than
                // defaulting.
                _onRecovery?.Invoke(new JsonStoreRecoveryEventArgs(
                    _path,
                    succeeded: false,
                    failureReason: "Recovery promotion (replace) failed.",
                    evidencePath: evidencePath,
                    backupPreserved: true));

                throw;
            }

            // A leftover ordinary .tmp from an interrupted save is never
            // authoritative; now that primary is established it is safe to drop.
            TryDeleteStaleTemp();

            _onRecovery?.Invoke(new JsonStoreRecoveryEventArgs(
                _path,
                succeeded: true,
                failureReason: null,
                evidencePath: evidencePath,
                backupPreserved: true));

            return backupValue;
        }
        finally
        {
            // Always clean up the recovery temp, never the .bak.
            if (File.Exists(recoveryTemp))
            {
                File.Delete(recoveryTemp);
            }
        }
    }

    // Reads + classifies a candidate document. A returned Corrupt result means
    // the bytes were successfully read and content validation failed. An
    // IOException / UnauthorizedAccessException is reported as TransientIoFailure
    // and must NOT be treated as corruption.
    private async Task<(DocumentReadResult result, T? value)> ReadDocumentAsync(
        string path,
        FaultInjectionPoint faultPoint = FaultInjectionPoint.FileRead)
    {
        if (ShouldFailAt(faultPoint))
        {
            // Disk/access read failure: genuine I/O, NOT corruption.
            return (DocumentReadResult.TransientIoFailure, null);
        }

        if (ShouldFailAt(FaultInjectionPoint.JsonLoad))
        {
            // A content/parse failure is corruption: the bytes were read but
            // cannot be validated as T. (This is the boundary that audit
            // finding 4 requires us NOT to conflate with transient I/O.)
            return (DocumentReadResult.Corrupt, null);
        }

        if (!File.Exists(path))
        {
            return (DocumentReadResult.Missing, null);
        }

        byte[] bytes;

        try
        {
            // Share ReadWrite|Delete so concurrent readers of other store
            // instances and external scanners are tolerated.
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous
                | FileOptions.SequentialScan);

            bytes = new byte[stream.Length];
            if (bytes.Length > 0)
            {
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = await stream.ReadAsync(bytes.AsMemory(read));
                    if (n == 0)
                    {
                        break;
                    }

                    read += n;
                }

                if (read != bytes.Length)
                {
                    // Truncated read of an otherwise-present file is treated
                    // as corruption (do not silently default).
                    Array.Resize(ref bytes, read);
                }
            }
        }
        catch (FileNotFoundException)
        {
            // The file was replaced between File.Exists and FileMode.Open.
            return (DocumentReadResult.Missing, null);
        }
        catch (DirectoryNotFoundException)
        {
            return (DocumentReadResult.Missing, null);
        }
        catch (IOException)
        {
            // Genuine read/access failure: NOT corruption.
            return (DocumentReadResult.TransientIoFailure, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (DocumentReadResult.TransientIoFailure, null);
        }

        // An all-NUL document is corruption, not valid JSON. Distinguish raw
        // NUL bytes (file corruption) from a NUL character that legitimately
        // appears inside a JSON string value: here we inspect the RAW ON-DISK
        // bytes, not a deserialized string, so a data value containing '\0'
        // cannot be mistaken for a corrupted file.
        if (bytes.Length > 0 && bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0)
        {
            return (DocumentReadResult.Corrupt, null);
        }

        // Strict UTF-8 boundary: System.Text.Json deserializes directly from the
        // UTF-8 bytes, which rejects malformed sequences (it does not apply the
        // lenient replacement fallback that Encoding.UTF8.GetString would). A
        // legitimate escaped '\uFFFD' inside a JSON STRING is valid JSON and is
        // not the same as malformed raw UTF-8; STJ handles that correctly.
        try
        {
            T? value = await JsonSerializer.DeserializeAsync<T>(
                new MemoryStream(bytes),
                _jsonOptions);

            if (value is null)
            {
                return (DocumentReadResult.Corrupt, null);
            }

            return (DocumentReadResult.Valid, value);
        }
        catch (JsonException)
        {
            return (DocumentReadResult.Corrupt, null);
        }
        catch (InvalidOperationException)
        {
            return (DocumentReadResult.Corrupt, null);
        }
    }

    private async Task SaveCoreAsync(
        T value,
        CancellationToken cancellationToken)
    {
        if (ShouldFailAt(FaultInjectionPoint.JsonSave))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.JsonSave);
        }

        string? directory =
            Path.GetDirectoryName(_path);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "JSON store directory is invalid.");
        }

        Directory.CreateDirectory(directory);

        // Proactively remove any stale temp files from a PREVIOUS crashed
        // writer for this path (matching our unique ".tmp-<guid>" pattern).
        // Safe under the path lock: no other in-process writer for this path
        // can be mid-flight, so any such file is genuinely orphaned.
        TryDeleteStaleTemp();

        // Prepare/harden the directory BEFORE the temporary (pre-move) file is
        // created, so the .tmp file inherits only the approved security
        // boundary and there is no readable tmp-file window for an ordinary
        // user (required by the Cloud credential storage contract).
        _onDirectoryPrepared?.Invoke(directory);

        // Unique per-operation temp name. This prevents one writer instance
        // from touching another writer instance's temp file, but it does NOT
        // by itself make MutateAsync atomic across instances — the path-scoped
        // lock provides that. The unique name is defense-in-depth so a stale
        // temp from a different worker is never mistaken for ours.
        string temporaryPath =
            $"{_path}.tmp-{Guid.NewGuid():N}";

        string json = JsonSerializer.Serialize(
            value,
            _jsonOptions);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (ShouldFailAt(FaultInjectionPoint.FileWrite))
                {
                    throw new FaultInjectionException(
                        FaultInjectionPoint.FileWrite);
                }

                // Write the temp document with WriteThrough so its bytes are
                // pushed toward stable storage before we promote it. NOTE: this
                // REDUCES the durability window; it does NOT promise immunity
                // to every filesystem controller, hardware, or power-loss
                // failure. The atomic replace below is the real safety net.
                await WriteTempValidatedAsync(
                    temporaryPath, json, cancellationToken);

                if (ShouldFailAt(FaultInjectionPoint.FileMove))
                {
                    throw new FaultInjectionException(
                        FaultInjectionPoint.FileMove);
                }

                await PromoteTempToPrimary(temporaryPath, cancellationToken);

                // Best-effort post-persistence hook (e.g. Windows ACL hardening
                // of the final file). Runs after the atomic move so the live
                // file's security descriptor is enforced before any reader sees
                // it as "enrolled".
                _onFilePersisted?.Invoke(_path);

                return;
            }
            catch (Exception ex)
                when (attempt < MaxFileAccessAttempts - 1
                      && IsRetryableAccess(ex))
            {
                await BackoffAsync(attempt, cancellationToken);
            }
            catch (FaultInjectionException)
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                throw;
            }
        }
    }

    // Writes the temp file and then READS IT BACK and deserializes it as T
    // BEFORE promotion. A completed SaveAsync must never replace a known-good
    // primary with an unvalidated temp document: if the round-trip fails we
    // delete the temp and throw, leaving the existing primary fully intact.
    private async Task WriteTempValidatedAsync(
        string temporaryPath,
        string json,
        CancellationToken cancellationToken)
    {
        await using (FileStream writeStream = new(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous
            | FileOptions.WriteThrough))
        {
            await writeStream.WriteAsync(
                Encoding.UTF8.GetBytes(json),
                cancellationToken);
            await writeStream.FlushAsync(cancellationToken);
        }

        // Validate by deserializing the just-written temp document. This is a
        // genuine read-back, not a re-serialize of the in-memory value, so a
        // serializer/encoding mismatch is caught here rather than after the
        // live file has been replaced.
        (DocumentReadResult result, _) = await ReadDocumentAsync(temporaryPath);
        if (result != DocumentReadResult.Valid)
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw new InvalidOperationException(
                "Persisted temp document failed validation and was not " +
                "promoted; the existing primary is unchanged.");
        }
    }

    // Writes a document to an arbitrary temp path (used for recovery temps).
    private async Task WriteDocumentAsync(
        string temporaryPath,
        T value,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(
            value,
            _jsonOptions);

        await using FileStream writeStream = new(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous
            | FileOptions.WriteThrough);

        await writeStream.WriteAsync(
            Encoding.UTF8.GetBytes(json),
            cancellationToken);
        await writeStream.FlushAsync(cancellationToken);
    }

    private async Task PromoteTempToPrimary(
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            // No existing primary: nothing to back up. Promote the temp
            // document (the destination does not exist, so Move not Replace).
            // This establishes a NEW generation.
            MoveFileWithRetry(temporaryPath, _path);

            // New-generation establishment completed successfully. A ".bak"
            // from a PREVIOUS generation (whose primary vanished) must not stay
            // eligible for automatic recovery of this new generation. Quarantine
            // it into a separate collision-safe evidence file (never deleted
            // outright), but only AFTER the new primary is safely committed.
            // NOTE: this runs only here, on the no-prior-primary branch — a
            // normal save-with-existing-primary keeps its fresh ".bak" intact.
            QuarantineStaleBackupIfPresent();
            return;
        }

        // Existing primary: classify it explicitly (audit finding 2). We must
        // NOT collapse TransientIoFailure into "not valid" and therefore skip
        // the .bak, because that would let us overwrite a known-good primary
        // without preserving the prior generation merely because a transient
        // read flaked.
        DocumentReadResult result = DocumentReadResult.TransientIoFailure;
        for (int attempt = 0; attempt < MaxFileAccessAttempts; attempt++)
        {
            // The primary-classification read uses a dedicated fault point so a
            // regression test can deterministically target THIS boundary (the
            // temp is already written and validated before we get here).
            result = (await ReadDocumentAsync(
                _path, FaultInjectionPoint.PrimaryValidation)).result;
            if (result != DocumentReadResult.TransientIoFailure)
            {
                break;
            }

            await BackoffAsync(attempt, cancellationToken);
        }

        if (result == DocumentReadResult.TransientIoFailure)
        {
            // Even after bounded retries the primary could not be read. We MUST
            // NOT promote the temp and we MUST NOT touch the .bak. Fail the
            // save loudly and leave both the primary and the .bak intact.
            throw new InvalidOperationException(
                "Primary could not be read for backup classification after " +
                "retries; the save was aborted to avoid an unbacked " +
                "overwrite of the existing primary.");
        }

        if (result == DocumentReadResult.Corrupt)
        {
            // The current primary is itself corrupt. Do NOT overwrite a good
            // .bak with bad bytes; promote without a backup so the valid .bak
            // (from a prior good generation) is preserved.
            ReplaceFileWithRetry(temporaryPath, _path, backupPath: null);
            return;
        }

        // result == Valid: preserve the current known-good primary as the
        // ".bak" DURING replacement (File.Replace moves the old primary to the
        // backup path atomically, never consuming it).
        string? backupPath = _recoveryMode == JsonStoreRecoveryMode.BackupRollback
            ? _path + ".bak"
            : null;

        ReplaceFileWithRetry(temporaryPath, _path, backupPath);
    }

    // File.Move can transiently fail under load for the same reasons as
    // Replace. The temp is preserved on a failed Move, so retrying is safe.
    private void MoveFileWithRetry(string temporaryPath, string destinationPath)
    {
        for (int attempt = 0; attempt < MaxFileAccessAttempts; attempt++)
        {
            try
            {
                _fileOps.Move(temporaryPath, destinationPath);
                return;
            }
            catch (Exception ex) when (IsRetryableAccess(ex))
            {
                if (attempt == MaxFileAccessAttempts - 1)
                {
                    throw;
                }

                Task.Delay(
                    TimeSpan.FromMilliseconds(25 * (attempt + 1)))
                    .GetAwaiter().GetResult();
            }
        }
    }

    // File.Replace can transiently fail under load (e.g. "Unable to remove the
    // file to be replaced" when the destination is briefly locked by an
    // external reader / AV). The temp file is preserved across a failed
    // Replace, so retrying is safe and is the correct handling for transient
    // I/O — it must NOT collapse into a corrupt/abort classification.
    private void ReplaceFileWithRetry(
        string temporaryPath,
        string destinationPath,
        string? backupPath)
    {
        for (int attempt = 0; attempt < MaxFileAccessAttempts; attempt++)
        {
            try
            {
                _fileOps.Replace(temporaryPath, destinationPath, backupPath);
                return;
            }
            catch (Exception ex) when (IsRetryableAccess(ex))
            {
                if (attempt == MaxFileAccessAttempts - 1)
                {
                    throw;
                }

                // The temp remains valid; await a short backoff and retry.
                Task.Delay(
                    TimeSpan.FromMilliseconds(25 * (attempt + 1)))
                    .GetAwaiter().GetResult();
            }
        }
    }

    // Removes a stale ".bak" that belongs to a previous generation (its primary
    // was absent when this generation was established). It is moved to a
    // collision-safe evidence file for forensics; never deleted outright, and
    // only called AFTER the new primary is committed.
    private void QuarantineStaleBackupIfPresent()
    {
        try
        {
            string backupPath = _path + ".bak";

            if (File.Exists(backupPath))
            {
                string evidence = MakeEvidencePath(_path + ".bak.stale");
                if (File.Exists(evidence))
                {
                    File.Delete(evidence);
                }

                File.Move(backupPath, evidence, overwrite: false);
            }
        }
        catch
        {
            // Best-effort; never let stale-backup cleanup mask the committed
            // save.
        }
    }

    // Copies (NOT moves) the corrupt primary to a collision-safe evidence file
    // so the original corrupt primary remains in place for the next load to
    // keep failing consistently (audit finding 1). Returns the evidence path,
    // or null if the primary is already gone or the copy fails.
    private string? CopyCorruptPrimaryEvidence()
    {
        try
        {
            if (File.Exists(_path))
            {
                string evidencePath = MakeEvidencePath(_path + ".corrupt");
                // Copy, never Move: the corrupt primary must stay where it is so
                // a subsequent LoadAsync does not silently see "Missing".
                File.Copy(_path, evidencePath, overwrite: false);
                return evidencePath;
            }
        }
        catch
        {
            // Best-effort; do not let evidence capture mask the real error.
        }

        return null;
    }

    private static string MakeEvidencePath(string basePath)
    {
        // Collision-safe: append UTC timestamp + short random id. Contains no
        // document contents or secrets (only the store path stem + a suffix).
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        string id = Guid.NewGuid().ToString("N")[..8];
        return $"{basePath}.{stamp}.{id}";
    }

    private void TryDeleteStaleTemp()
    {
        try
        {
            // Only delete OUR temp prefix pattern; unique temp files from other
            // workers are not our responsibility and we never promote them.
            string tempPattern = _path + ".tmp-";
            foreach (string file in Directory.EnumerateFiles(
                         Path.GetDirectoryName(_path)!,
                         Path.GetFileName(tempPattern) + "*"))
            {
                if (file.StartsWith(tempPattern, StringComparison.Ordinal))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    private static bool IsRetryableAccess(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    private bool ShouldFailAt(FaultInjectionPoint point) =>
        FaultInjectionResolver.ShouldFail(_faultPolicy, point);

    private static async Task BackoffAsync(
        int attempt,
        CancellationToken cancellationToken) =>
        await Task.Delay(
            TimeSpan.FromMilliseconds(25 * (attempt + 1)),
            cancellationToken);
}
