namespace PathVeer.Core.Installation;

/// <summary>
/// The canonical machine-level installation layout for PathVeer.
///
/// Three roots are deliberately kept distinct and must never be conflated:
///
/// <list type="bullet">
/// <item><description>
/// DEVELOPMENT REPOSITORY — e.g. <c>C:\codespace\PathVeer</c>. Source only.
/// Nothing installed may ever resolve back to it.
/// </description></item>
/// <item><description>
/// INSTALLED BINARIES — <c>%ProgramFiles%\PathVeer</c>. Written by the
/// installer, read-only to unprivileged users (Program Files ACL inheritance),
/// which is what keeps the elevated service binary non-writable by standard
/// users.
/// </description></item>
/// <item><description>
/// PERSISTENT DATA — <c>%ProgramData%\PathVeer</c>, owned by
/// <c>StateRootResolver</c> (Phase 36.3). The installer never writes state here
/// and uninstall never deletes it unless an explicit purge is requested.
/// </description></item>
/// </list>
///
/// This type is pure path composition so it is fully unit-testable against a
/// synthetic root; nothing here touches the filesystem.
/// </summary>
public sealed class InstallLayout
{
    /// <summary>Directory name created under <c>%ProgramFiles%</c>.</summary>
    public const string ProductDirectoryName = "PathVeer";

    public const string ServiceDirectoryName = "Service";
    public const string CliDirectoryName = "Cli";
    public const string TrayDirectoryName = "Tray";

    public const string ServiceExecutableName = "PathVeer.Service.exe";
    public const string CliExecutableName = "PathVeer.Cli.exe";
    public const string TrayExecutableName = "PathVeer.Tray.exe";

    /// <summary>
    /// Install metadata file. Bounded installer metadata only — never runtime
    /// configuration and never secrets.
    /// </summary>
    public const string ManifestFileName = "install-manifest.json";

    /// <summary>
    /// Staging directory used for the stop → stage → verify → swap → start
    /// replacement pattern. Kept as a sibling of the live directories so the
    /// swap is a same-volume directory move.
    /// </summary>
    public const string StagingDirectoryName = ".staging";

    public InstallLayout(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        InstallRoot = installRoot;
    }

    /// <summary>Resolves the layout under the real <c>%ProgramFiles%</c>.</summary>
    public static InstallLayout CreateDefault() =>
        new(Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            ProductDirectoryName));

    /// <summary>e.g. <c>%ProgramFiles%\PathVeer</c>.</summary>
    public string InstallRoot { get; }

    public string ServiceDirectory =>
        Path.Combine(InstallRoot, ServiceDirectoryName);

    public string CliDirectory =>
        Path.Combine(InstallRoot, CliDirectoryName);

    public string TrayDirectory =>
        Path.Combine(InstallRoot, TrayDirectoryName);

    public string StagingDirectory =>
        Path.Combine(InstallRoot, StagingDirectoryName);

    public string ServiceExecutablePath =>
        Path.Combine(ServiceDirectory, ServiceExecutableName);

    public string CliExecutablePath =>
        Path.Combine(CliDirectory, CliExecutableName);

    public string TrayExecutablePath =>
        Path.Combine(TrayDirectory, TrayExecutableName);

    public string ManifestPath =>
        Path.Combine(InstallRoot, ManifestFileName);

    /// <summary>
    /// The directory added to the machine PATH so <c>pathveer</c> resolves as a
    /// command. Only the CLI directory is exposed — the service and tray
    /// directories stay off PATH.
    /// </summary>
    public string PathEnvironmentEntry => CliDirectory;

    /// <summary>
    /// True when <paramref name="candidate"/> sits inside this install root.
    /// Used by the upgrader to reject an SCM binary path that still points at a
    /// developer checkout.
    /// </summary>
    public bool ContainsPath(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string normalizedRoot = NormalizeDirectory(InstallRoot);
        string normalizedCandidate = Path.GetFullPath(candidate.Trim('"'));

        return normalizedCandidate.StartsWith(
            normalizedRoot,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string path)
    {
        string full = Path.GetFullPath(path);

        return full.EndsWith(Path.DirectorySeparatorChar)
            ? full
            : full + Path.DirectorySeparatorChar;
    }
}
