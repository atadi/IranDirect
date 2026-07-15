using System.Text.Json;

namespace IranDirect.Core.Routing;

public sealed class RouteInventoryStore
{
    private readonly string _inventoryPath;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    public RouteInventoryStore(string inventoryPath)
    {
        _inventoryPath = inventoryPath;
    }

    public async Task<RouteInventory> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_inventoryPath))
        {
            return new RouteInventory();
        }

        string json = await File.ReadAllTextAsync(
            _inventoryPath,
            cancellationToken);

        return JsonSerializer.Deserialize<RouteInventory>(
                   json,
                   JsonOptions)
               ?? new RouteInventory();
    }

    public async Task SaveAsync(
        RouteInventory inventory,
        CancellationToken cancellationToken = default)
    {
        string? directory =
            Path.GetDirectoryName(_inventoryPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "Route inventory directory is invalid.");
        }

        Directory.CreateDirectory(directory);

        string temporaryPath =
            _inventoryPath + ".tmp";

        string json = JsonSerializer.Serialize(
            inventory,
            JsonOptions);

        await File.WriteAllTextAsync(
            temporaryPath,
            json,
            cancellationToken);

        File.Move(
            temporaryPath,
            _inventoryPath,
            overwrite: true);
    }

    public Task ClearAsync(
        CancellationToken cancellationToken = default)
    {
        return SaveAsync(
            new RouteInventory(),
            cancellationToken);
    }
}