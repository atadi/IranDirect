using System.Text.Json;

namespace IranDirect.Core.State;

public sealed class StateRepository
{
    private readonly string _statePath;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    public StateRepository(string statePath)
    {
        _statePath = statePath;
    }

    public async Task<IranDirectState> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_statePath))
        {
            return new IranDirectState();
        }

        string json = await File.ReadAllTextAsync(
            _statePath,
            cancellationToken);

        return JsonSerializer.Deserialize<IranDirectState>(
                   json,
                   JsonOptions)
               ?? new IranDirectState();
    }

    public async Task SaveAsync(
        IranDirectState state,
        CancellationToken cancellationToken = default)
    {
        string? directory =
            Path.GetDirectoryName(_statePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "State directory is invalid.");
        }

        Directory.CreateDirectory(directory);

        string temporaryPath =
            _statePath + ".tmp";

        string json = JsonSerializer.Serialize(
            state,
            JsonOptions);

        await File.WriteAllTextAsync(
            temporaryPath,
            json,
            cancellationToken);

        File.Move(
            temporaryPath,
            _statePath,
            overwrite: true);
    }
}