namespace PathVeer.Tray;

using PathVeer.Core.Update;
using System.IO;

/// <summary>
/// Phase 37.3 — resolves the release source the Tray uses for a manual
/// "Check for app updates" action.
///
/// The Tray check is MANUAL and NON-FATAL: it must never affect routing or
/// crash the product. Until PathVeer distribution (Phase 37.4) publishes a
/// live release feed, update checking is configured via an explicit local
/// manifest path (env PATHVEER_UPDATE_MANIFEST_PATH) for development/testing.
/// When unset, the factory returns null and the Tray reports that automatic
/// update checking is not yet configured — it does NOT fabricate network
/// traffic or fail closed.
/// </summary>
public static class TrayUpdateSourceFactory
{
    public const string ManifestPathEnv = "PATHVEER_UPDATE_MANIFEST_PATH";

    public static IReleaseSource? Resolve()
    {
        var path = Environment.GetEnvironmentVariable(ManifestPathEnv);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        return new LocalFileReleaseSource(path);
    }

    /// <summary>
    /// Installed version comes from the authoritative install manifest written
    /// by Install-PathVeer.ps1 (%ProgramFiles%\PathVeer\install-manifest.json).
    /// </summary>
    public static string InstallManifestPath()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return Path.Combine(programFiles, "PathVeer", "install-manifest.json");
    }
}
