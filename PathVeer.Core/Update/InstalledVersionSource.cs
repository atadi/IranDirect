namespace PathVeer.Core.Update;

using System.Text.Json;

/// <summary>
/// Authoritative installed-version source = install-manifest.json written by
/// Install-PathVeer.ps1 (productVersion field). Folder names are NEVER used.
/// Missing/corrupt manifest yields null (caller decides unsupported state).
/// </summary>
public sealed class InstalledVersionSource
{
    public const string DefaultManifestPath =
        @"C:\ProgramData\PathVeer\install-manifest.json";

    private readonly Func<string> _pathResolver;

    public InstalledVersionSource() : this(DefaultManifestPath) { }
    public InstalledVersionSource(string path) => _pathResolver = () => path;
    public InstalledVersionSource(Func<string> pathResolver) => _pathResolver = pathResolver;

    public string? ReadVersion()
    {
        var path = _pathResolver();
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("productVersion", out var v))
                return v.GetString();
        }
        catch
        {
            return null;
        }
        return null;
    }
}
