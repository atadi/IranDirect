namespace IranDirect.Testing.Performance.Persistence;

public sealed class TemporaryPersistenceWorkspace : IDisposable
{
    public TemporaryPersistenceWorkspace(string? name = null)
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.Endurance",
            $"{name ?? "workspace"}_{Guid.NewGuid():N}");

        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string CreatePath(string relativePath) =>
        Path.Combine(RootPath, relativePath);

    public FileSystemSnapshot Snapshot() =>
        FileSystemSnapshot.Capture(RootPath);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
