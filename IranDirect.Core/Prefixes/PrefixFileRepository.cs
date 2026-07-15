namespace IranDirect.Core.Prefixes;

public sealed class PrefixFileRepository
{
    private readonly string _filePath;

    public PrefixFileRepository(string filePath)
    {
        _filePath = filePath;
    }

    public async Task SaveAsync(
        IEnumerable<string> prefixes,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(_filePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "The prefix file directory is invalid.");
        }

        Directory.CreateDirectory(directory);

        string temporaryFile = _filePath + ".tmp";

        try
        {
            await File.WriteAllLinesAsync(
                temporaryFile,
                prefixes,
                cancellationToken);

            File.Move(
                temporaryFile,
                _filePath,
                overwrite: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UnauthorizedAccessException(
                $"IranDirect cannot write to '{_filePath}'. " +
                "Run the application as Administrator or use a " +
                "user-writable data directory such as LocalApplicationData.",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }

    public async Task<IReadOnlyList<string>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<string>();
        }

        string[] lines = await File.ReadAllLinesAsync(
            _filePath,
            cancellationToken);

        return lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public DateTimeOffset? GetLastModified()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        return File.GetLastWriteTimeUtc(_filePath);
    }
}