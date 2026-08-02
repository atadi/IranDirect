using System.Text;
using System.Text.Json;

namespace IranDirect.Core.Persistence;

public class JsonStore<T>
    where T : class, new()
{
    private const int MaxFileAccessAttempts = 5;

    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions;

    // Serializes all file access for this store instance. The atomic
    // write (tmp + File.Move overwrite) cannot replace the live file
    // while any handle holds it open, so reads and writes must never
    // overlap on the same store.
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public JsonStore(
        string path,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _jsonOptions =
            jsonOptions
            ?? new JsonSerializerOptions
            {
                WriteIndented = true
            };
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
            catch (Exception ex)
                when (attempt < MaxFileAccessAttempts - 1
                      && IsRetryableAccess(ex))
            {
                await BackoffAsync(attempt, cancellationToken);
            }
        }
    }

    private async Task<T> LoadOnceAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new T();
        }

        string json;

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

            using StreamReader reader = new(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

            json = await reader.ReadToEndAsync(
                cancellationToken);
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

        if (string.IsNullOrWhiteSpace(json))
        {
            return new T();
        }

        return JsonSerializer.Deserialize<T>(
                   json,
                   _jsonOptions)
               ?? new T();
    }

    private async Task SaveCoreAsync(
        T value,
        CancellationToken cancellationToken)
    {
        string? directory =
            Path.GetDirectoryName(_path);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "JSON store directory is invalid.");
        }

        Directory.CreateDirectory(directory);

        string temporaryPath =
            _path + ".tmp";

        string json = JsonSerializer.Serialize(
            value,
            _jsonOptions);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    json,
                    cancellationToken);

                File.Move(
                    temporaryPath,
                    _path,
                    overwrite: true);

                return;
            }
            catch (Exception ex)
                when (attempt < MaxFileAccessAttempts - 1
                      && IsRetryableAccess(ex))
            {
                await BackoffAsync(attempt, cancellationToken);
            }
        }
    }

    private static bool IsRetryableAccess(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    private static async Task BackoffAsync(
        int attempt,
        CancellationToken cancellationToken) =>
        await Task.Delay(
            TimeSpan.FromMilliseconds(25 * (attempt + 1)),
            cancellationToken);
}
