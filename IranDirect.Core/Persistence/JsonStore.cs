using System.Text.Json;

namespace IranDirect.Core.Persistence;

public class JsonStore<T>
    where T : class, new()
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions;

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
        if (!File.Exists(_path))
        {
            return new T();
        }

        string json = await File.ReadAllTextAsync(
            _path,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            return new T();
        }

        return JsonSerializer.Deserialize<T>(
                   json,
                   _jsonOptions)
               ?? new T();
    }

    public virtual async Task SaveAsync(
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

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

        await File.WriteAllTextAsync(
            temporaryPath,
            json,
            cancellationToken);

        File.Move(
            temporaryPath,
            _path,
            overwrite: true);
    }
}