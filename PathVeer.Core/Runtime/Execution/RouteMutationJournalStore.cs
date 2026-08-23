using System.IO;
using System.Text;
using System.Text.Json;
using PathVeer.Core.Persistence;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Runtime.Execution;

/// <summary>
/// File-backed implementation of <see cref="IRouteMutationJournal"/>.
///
/// Crash-consistency contract for the route-mutation authority: a save writes
/// to a "&lt;path&gt;.tmp" file and then atomically <see cref="File.Move"/>-
/// overwrites the target, with bounded retry/backoff on transient IO. Writes and
/// clears are serialized by a per-instance lock. This store deliberately does
/// NOT use the generic JsonStore&lt;T&gt; BackupRollback / cross-instance
/// path-lock machinery: the journal is its own write-ahead authority and its
/// load semantics are strictly fail-closed (see below).
///
/// Load semantics differ deliberately from a normal document store: a
/// present-but-unparseable journal, a present zero-length or whitespace-only or
/// all-NUL file, or one with an unsupported schema version throws
/// <see cref="RouteMutationJournalCorruptException"/> instead of silently
/// resetting to empty. A MISSING journal file is the only case that legitimately
/// means "no pending mutation". The journal must never be inferred as "no pending
/// mutation" from corrupt bytes, because that would let recovery
/// guess ownership from native route shape.
///
/// All file access is serialized by an instance lock so the parallel prefix-add
/// path can write and clear independent intents safely.
/// </summary>
public sealed class RouteMutationJournalStore : IRouteMutationJournal
{
    private const int MaxFileAccessAttempts = 5;

    // Strict UTF-8 decoder: does NOT substitute U+FFFD for invalid byte
    // sequences. A malformed byte inside a JSON string must fail closed, not be
    // silently normalized into otherwise-valid JSON (which the default
    // Encoding.UTF8.GetString would do).
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IFaultInjectionPolicy _faultPolicy;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    /// <summary>The on-disk journal path (exposed for quarantine on corruption).</summary>
    public string JournalPath => _path;

    public RouteMutationJournalStore(
        string path,
        JsonSerializerOptions? jsonOptions = null,
        IFaultInjectionPolicy? faultPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            WriteIndented = true
        };
        _faultPolicy = faultPolicy ?? FaultInjectionPolicy.Never;
    }

    public async Task WriteIntentAsync(
        RouteMutationJournalEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            RouteMutationJournalFile file = await LoadFileAsync(cancellationToken);

            Dictionary<string, RouteMutationJournalEntry> entries =
                new(file.Entries, StringComparer.OrdinalIgnoreCase);
            entries[entry.RouteIdentity] = entry;

            await SaveFileAsync(
                file with { Entries = entries },
                cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task ClearIntentAsync(
        string routeIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(routeIdentity);

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
                return;

            RouteMutationJournalFile file = await LoadFileAsync(cancellationToken);

            if (!file.Entries.ContainsKey(routeIdentity))
                return;

            Dictionary<string, RouteMutationJournalEntry> entries =
                new(file.Entries, StringComparer.OrdinalIgnoreCase);
            entries.Remove(routeIdentity);

            await SaveFileAsync(
                file with { Entries = entries },
                cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, RouteMutationJournalEntry>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            RouteMutationJournalFile file = await LoadFileAsync(cancellationToken);
            return file.Entries;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task ClearAsync(
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<RouteMutationJournalFile> LoadFileAsync(
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await LoadFileOnceAsync(cancellationToken);
            }
            catch (RouteMutationJournalCorruptException)
            {
                throw;
            }
            catch (Exception ex)
                when (attempt < MaxFileAccessAttempts - 1
                      && ex is IOException or UnauthorizedAccessException)
            {
                await BackoffAsync(attempt, cancellationToken);
            }
        }
    }

    private async Task<RouteMutationJournalFile> LoadFileOnceAsync(
        CancellationToken cancellationToken)
    {
        if (ShouldFailAt(FaultInjectionPoint.FileRead))
            throw new FaultInjectionException(FaultInjectionPoint.FileRead);

        if (!File.Exists(_path))
            return new RouteMutationJournalFile();

        // A PRESENT journal file that is zero-length, all-NUL, or
        // whitespace-only is persisted corruption/truncation and MUST fail
        // closed. It is NOT equivalent to a missing file, which legitimately
        // means "no pending mutation".

        byte[] rawBytes;
        try
        {
            rawBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // Disappeared between the existence check and the read.
            return new RouteMutationJournalFile();
        }
        catch (DirectoryNotFoundException)
        {
            return new RouteMutationJournalFile();
        }

        if (rawBytes.Length == 0)
        {
            throw new RouteMutationJournalCorruptException(
                "The route mutation journal exists but is zero-length, " +
                "indicating truncation. A present journal must not be " +
                "treated as empty.");
        }

        if (rawBytes.AsSpan().IndexOfAnyExcept((byte)0) < 0)
        {
            throw new RouteMutationJournalCorruptException(
                "The route mutation journal contains only NUL bytes, which is " +
                "not valid journal content.");
        }

        // Decode UTF-8 STRICTLY. A leading BOM (if present) is skipped so a
        // legitimately-written journal parses identically, but any malformed
        // byte AFTER the BOM still throws rather than being normalized to U+FFFD.
        int start = (rawBytes.Length >= 3 &&
                    rawBytes[0] == 0xEF && rawBytes[1] == 0xBB &&
                    rawBytes[2] == 0xBF)
            ? 3
            : 0;

        string json;
        try
        {
            json = StrictUtf8.GetString(
                rawBytes, start, rawBytes.Length - start);
        }
        catch (DecoderFallbackException ex)
        {
            // Malformed raw UTF-8 is corruption of an authority file; fail
            // closed. Never substitute replacement characters.
            throw new RouteMutationJournalCorruptException(
                "The route mutation journal is not valid UTF-8 and cannot be " +
                "safely decoded.", ex);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new RouteMutationJournalCorruptException(
                "The route mutation journal exists but contains only " +
                "whitespace; a present journal must not be treated as empty.");
        }

        RouteMutationJournalFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<RouteMutationJournalFile>(
                json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            throw new RouteMutationJournalCorruptException(
                "The route mutation journal could not be parsed.", ex);
        }

        if (parsed is null)
        {
            throw new RouteMutationJournalCorruptException(
                "The route mutation journal deserialized to null.");
        }

        if (parsed.SchemaVersion != RouteMutationJournal.SchemaVersion)
        {
            throw new RouteMutationJournalCorruptException(
                $"The route mutation journal has unsupported schema version " +
                $"{parsed.SchemaVersion}; expected {RouteMutationJournal.SchemaVersion}.");
        }

        return parsed;
    }

    private async Task SaveFileAsync(
        RouteMutationJournalFile file,
        CancellationToken cancellationToken)
    {
        if (ShouldFailAt(FaultInjectionPoint.JsonSave))
            throw new FaultInjectionException(FaultInjectionPoint.JsonSave);

        string? directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Journal directory is invalid.");

        Directory.CreateDirectory(directory);

        string temporaryPath = _path + ".tmp";
        string json = JsonSerializer.Serialize(file, _jsonOptions);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (ShouldFailAt(FaultInjectionPoint.FileWrite))
                    throw new FaultInjectionException(FaultInjectionPoint.FileWrite);

                await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);

                if (ShouldFailAt(FaultInjectionPoint.FileMove))
                    throw new FaultInjectionException(FaultInjectionPoint.FileMove);

                File.Move(temporaryPath, _path, overwrite: true);
                return;
            }
            catch (RouteMutationJournalCorruptException)
            {
                throw;
            }
            catch (FaultInjectionException)
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
                throw;
            }
            catch (Exception ex)
                when (attempt < MaxFileAccessAttempts - 1
                      && ex is IOException or UnauthorizedAccessException)
            {
                await BackoffAsync(attempt, cancellationToken);
            }
        }
    }

    private bool ShouldFailAt(FaultInjectionPoint point) =>
        FaultInjectionResolver.ShouldFail(_faultPolicy, point);

    private static async Task BackoffAsync(
        int attempt,
        CancellationToken cancellationToken) =>
        await Task.Delay(
            TimeSpan.FromMilliseconds(25 * (attempt + 1)),
            cancellationToken);
}
