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
/// </summary>
public enum JsonStoreRecoveryMode
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

    // Serializes all file access for this store instance. The atomic
    // write (tmp + replace) cannot replace the live file
    // while any handle holds it open, so reads and writes must never
    // overlap on the same store.
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public JsonStore(
        string path,
        JsonSerializerOptions? jsonOptions = null,
        IFaultInjectionPolicy? faultPolicy = null,
        Action<string>? onDirectoryPrepared = null,
        Action<string>? onFilePersisted = null,
        JsonStoreRecoveryMode recoveryMode = JsonStoreRecoveryMode.FailClosed)
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
    }

    public virtual async Task<T> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            return await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public virtual async Task SaveAsync(
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            await SaveCoreAsync(value, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    // Performs a read-modify-write atomically under the file lock so
    // that the mutation never observes or produces an intermediate
    // state of the underlying document.
    public async Task<T> MutateAsync(
        Func<T, Task<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            T value = await LoadCoreAsync(cancellationToken);

            value = await mutation(value);

            await SaveCoreAsync(value, cancellationToken);

            return value;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<T> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await LoadOnceAsync(cancellationToken);
            }
            catch (JsonStoreCorruptionException ex)
            {
                // Corruption is not transient. Fail-closed callers keep their
                // native exception type; backup-rollback callers attempt
                // recovery from the known-good ".bak".
                if (_recoveryMode == JsonStoreRecoveryMode.FailClosed)
                {
                    if (ex.InnerException is not null)
                    {
                        // Preserve the original exception type so existing
                        // fail-closed callers (DesiredConfigurationStore,
                        // CloudRegistrationStore, ...) see the same
                        // JsonException / InvalidOperationException they
                        // already rely on.
                        System.Runtime.ExceptionServices
                            .ExceptionDispatchInfo
                            .Capture(ex.InnerException)
                            .Throw();
                    }

                    throw;
                }

                return await RecoverFromBackupOrThrow(ex);
            }
            catch (Exception ex)
                when (attempt < MaxFileAccessAttempts - 1
                      && IsRetryableAccess(ex))
            {
                await BackoffAsync(attempt, cancellationToken);
            }
        }
    }

    private async Task<T> RecoverFromBackupOrThrow(
        JsonStoreCorruptionException corruption)
    {
        string backupPath = _path + ".bak";

        // Primary corrupt + backup valid: quarantine the corrupt primary so it
        // is preserved for diagnosis, then promote the backup to primary.
        if (File.Exists(backupPath) &&
            await TryDeserialize(backupPath) is (true, { } backupValue))
        {
            QuarantineCorruptPrimary();

            // Promote the backup to primary. This also removes the .bak; the
            // next successful save will recreate it from the new primary.
            File.Move(backupPath, _path, overwrite: true);

            // A leftover .tmp from an interrupted save is never authoritative;
            // now that the primary is established it is safe to drop.
            TryDeleteStaleTemp();

            return backupValue;
        }

        // Primary corrupt AND backup missing/corrupt: fail clearly. Never
        // silently substitute a default object.
        throw new PersistenceCorruptException(
            _path,
            $"Primary document at '{_path}' is corrupt and no valid backup " +
            $"('.bak') is available for recovery.",
            corruption);
    }

    private async Task<T> LoadOnceAsync(
        CancellationToken cancellationToken)
    {
        if (ShouldFailAt(FaultInjectionPoint.JsonLoad))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.JsonLoad);
        }

        if (ShouldFailAt(FaultInjectionPoint.FileRead))
        {
            throw new FaultInjectionException(
                FaultInjectionPoint.FileRead);
        }

        if (!File.Exists(_path))
        {
            return new T();
        }

        byte[] bytes;

        try
        {
            // Share ReadWrite|Delete so concurrent readers of other
            // store instances and external scanners are tolerated.
            await using FileStream stream = new(
                _path,
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
                    int n = await stream.ReadAsync(
                        bytes.AsMemory(read),
                        cancellationToken);
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
            return new T();
        }
        catch (DirectoryNotFoundException)
        {
            return new T();
        }

        // An all-NUL document is corruption, not valid JSON. Distinguish raw
        // NUL bytes (file corruption) from a NUL character that legitimately
        // appears inside a JSON string value: here we inspect the RAW ON-DISK
        // bytes, not a deserialized string, so a data value containing '\0'
        // cannot be mistaken for a corrupted file.
        if (bytes.Length > 0 && bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0)
        {
            throw new JsonStoreCorruptionException(
                "Document consists entirely of NUL bytes.",
                new JsonException(
                    "Document consists entirely of NUL bytes."));
        }

        string json;
        try
        {
            json = Encoding.UTF8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            // Invalid UTF-8 byte sequence -> corruption.
            throw new JsonStoreCorruptionException(
                "Document is not valid UTF-8.", ex);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return new T();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(
                       json,
                       _jsonOptions)
                   ?? new T();
        }
        catch (Exception ex)
            when (ex is JsonException or InvalidOperationException)
        {
            throw new JsonStoreCorruptionException(
                "Document could not be deserialized as " +
                typeof(T).Name + ".",
                ex);
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

        // Prepare/harden the directory BEFORE the temporary (pre-move) file is
        // created, so the .tmp file inherits only the approved security
        // boundary and there is no readable tmp-file window for an ordinary
        // user (required by the Cloud credential storage contract).
        _onDirectoryPrepared?.Invoke(directory);

        string temporaryPath =
            _path + ".tmp";

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

                await PromoteTempToPrimary(temporaryPath);

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
        (bool valid, _) = await TryDeserialize(temporaryPath);
        if (!valid)
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

    private async Task PromoteTempToPrimary(string temporaryPath)
    {
        if (File.Exists(_path))
        {
            // Preserve the previous known-good primary as the ".bak" DURING
            // replacement (File.Replace moves the old primary to the backup
            // path atomically) — but only when the current primary is itself
            // valid. If the current primary is already corrupt we must NOT
            // overwrite a good .bak with bad bytes; in that case replace
            // without a backup.
            string? backupPath =
                (_recoveryMode == JsonStoreRecoveryMode.BackupRollback &&
                 (await TryDeserialize(_path)).ok)
                    ? _path + ".bak"
                    : null;

            // File.Replace is atomic on NTFS: it replaces _path with the temp
            // document and (when backupPath is set) moves the prior _path to
            // backupPath in a single operation. If it fails, both the temp and
            // the old primary remain, so at least one trustworthy copy exists.
            File.Replace(temporaryPath, _path, backupPath);
        }
        else
        {
            // No existing primary: nothing to back up. A plain move promotes
            // the temp document (File.Replace requires the destination to
            // exist).
            File.Move(temporaryPath, _path, overwrite: false);
        }
    }

    private async Task<(bool ok, T? value)> TryDeserialize(string path)
    {
        try
        {
            // Share Read so a concurrent reader of the live file is tolerated
            // while we validate temp/backup documents.
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous
                | FileOptions.SequentialScan);

            T? value = await JsonSerializer.DeserializeAsync<T>(
                stream, _jsonOptions);
            return (value is not null, value);
        }
        catch
        {
            return (false, null);
        }
    }

    // Quarantines a corrupt primary so it is preserved for diagnosis rather
    // than silently overwritten. Uses a single fixed ".corrupt" slot.
    private void QuarantineCorruptPrimary()
    {
        try
        {
            if (File.Exists(_path))
            {
                string corruptPath = _path + ".corrupt";
                if (File.Exists(corruptPath))
                {
                    File.Delete(corruptPath);
                }

                File.Move(_path, corruptPath, overwrite: false);
            }
        }
        catch
        {
            // Best-effort; do not let quarantine failure mask the real error.
        }
    }

    private void TryDeleteStaleTemp()
    {
        try
        {
            string temp = _path + ".tmp";
            if (File.Exists(temp))
            {
                File.Delete(temp);
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
