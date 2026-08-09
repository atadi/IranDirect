namespace PathVeer.Core.Update;

using System.Security.Cryptography;

/// <summary>
/// Safe local staging for downloaded installers.
///
/// Layout (machine-owned, per version, unique):
///   %ProgramData%\PathVeer\Updates\&lt;version&gt;\
///       PathVeerSetup-&lt;version&gt;-win-x64.exe        (final, only after verify)
///       PathVeerSetup-&lt;version&gt;-win-x64.exe.partial (in-flight)
///
/// .partial files are NEVER treated as valid artifacts. The final file is
/// written only after hash + size + signature verification pass.
/// </summary>
public sealed class UpdateStagingPaths
{
    public const string UpdatesRootDefault = @"C:\ProgramData\PathVeer\Updates";

    private readonly string _updatesRoot;

    public UpdateStagingPaths(string? updatesRoot = null)
    {
        _updatesRoot = string.IsNullOrWhiteSpace(updatesRoot) ? UpdatesRootDefault : updatesRoot;
    }

    public string VersionDirectory(string version) =>
        Path.Combine(_updatesRoot, Sanitize(version));

    public string FinalPath(string version, string fileName) =>
        Path.Combine(VersionDirectory(version), fileName);

    public string PartialPath(string version, string fileName) =>
        Path.Combine(VersionDirectory(version), fileName + ".partial");

    public void EnsureVersionDirectory(string version)
    {
        Directory.CreateDirectory(VersionDirectory(version));
    }

    /// <summary>Removes only .partial and stale version dirs, never ProgramData state.</summary>
    public void CleanPartial(string version, string fileName)
    {
        var p = PartialPath(version, fileName);
        if (File.Exists(p)) File.Delete(p);
    }

    public void CleanAllExcept(string keepVersion)
    {
        if (!Directory.Exists(_updatesRoot)) return;
        foreach (var dir in Directory.GetDirectories(_updatesRoot))
        {
            var name = Path.GetFileName(dir);
            if (!string.Equals(name, Sanitize(keepVersion), StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
    }

    private static string Sanitize(string version)
    {
        var cleaned = new string(version.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        return cleaned.Trim();
    }
}
