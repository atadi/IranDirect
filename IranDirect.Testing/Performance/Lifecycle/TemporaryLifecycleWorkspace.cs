namespace IranDirect.Testing.Performance.Lifecycle;

/// <summary>
/// A self-cleaning temporary workspace for a lifecycle simulation.
/// All state, inventory, prefix, DNS, and report files live under a
/// uniquely named temp directory that is removed on dispose.
/// </summary>
public sealed class TemporaryLifecycleWorkspace : IDisposable
{
    private readonly string _path;

    public TemporaryLifecycleWorkspace(string name)
    {
        string prefix = name.Replace(' ', '_');

        _path = Path.Combine(
            Path.GetTempPath(),
            $"IranDirect.Lifecycle",
            $"{prefix}_{Guid.NewGuid():N}");

        Directory.CreateDirectory(_path);
    }

    public string PathValue => _path;

    public string Combine(string relative) =>
        Path.Combine(_path, relative);

    public string PerfDirectory => Path.Combine(_path, "perf");

    public string SupportDirectory => Path.Combine(_path, "support");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(PerfDirectory);
        Directory.CreateDirectory(SupportDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup must never fail a test.
        }
    }
}
