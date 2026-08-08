using System.IO;
using System.Text;
using System.Text.Json;
using PathVeer.Core.Persistence;
using PathVeer.Core.Testing.FaultInjection;

namespace PathVeer.Core.Runtime.Execution;

/// <summary>
/// File-backed implementation of <see cref="IRouteMutationJournal"/>.
///
/// Durability matches <see cref="JsonStore{T}"/> (tmp file + atomic
/// <see cref="File.Move"/> overwrite, retry/backoff on transient IO), but load
/// semantics differ deliberately: a present-but-unparseable journal or one with
/// an unsupported schema version throws <see cref="RouteMutationJournalCorruptException"/>
/// instead of silently resetting to empty. The journal must never be inferred as
/// "no pending mutation" from corrupt bytes, because that would let recovery
/// guess ownership from native route shape.
///
/// All file access is serialized by an instance lock so the parallel prefix-add
/// path can write and clear independent intents safely.
/// </summary>
public sealed class RouteMutationJournalStore : IRouteMutationJournal
{
    private const int MaxFileAccessAttempts = 5;

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

        string json;
        try
        {
            await using FileStream stream = new(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            using StreamReader reader = new(
                stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            json = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return new RouteMutationJournalFile();
        }
        catch (DirectoryNotFoundException)
        {
            return new RouteMutationJournalFile();
        }

        if (string.IsNullOrWhiteSpace(json))
            return new RouteMutationJournalFile();

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
