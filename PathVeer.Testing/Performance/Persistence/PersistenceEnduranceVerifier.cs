using PathVeer.Core.Persistence;

namespace PathVeer.Testing.Performance.Persistence;

public static class PersistenceEnduranceVerifier
{
    public static async Task<T> VerifyRoundTripAsync<T>(
        JsonStore<T> store,
        T expected,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(expected);

        T loaded = await store.LoadAsync(cancellationToken);

        if (!EqualityComparer<T>.Default.Equals(expected, loaded))
        {
            throw new InvalidOperationException(
                "Loaded document did not match the committed document exactly.");
        }

        return loaded;
    }

    public static void VerifyNoOrphanTempFiles(
        FileSystemSnapshot snapshot)
    {
        List<string> orphans = snapshot.OrphanTempFiles.ToList();

        if (orphans.Count > 0)
        {
            throw new InvalidOperationException(
                $"Orphaned temporary files remain: " +
                $"{string.Join(", ", orphans)}.");
        }
    }

    public static void VerifyOpenableWithNoSharing(
        string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException(
                $"Expected file to exist: {filePath}.");
        }

        try
        {
            using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"File {filePath} could not be opened with " +
                $"FileShare.None after the scenario: {ex.Message}.");
        }
    }

    public static void VerifyFileIsCompleteJson(
        string filePath)
    {
        string json = File.ReadAllText(filePath);

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                $"File {filePath} is empty; expected complete JSON.");
        }

        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(json);

        if (document.RootElement.ValueKind
            != System.Text.Json.JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"File {filePath} does not contain a JSON object.");
        }
    }
}
