namespace PathVeer.Core.Installer;

/// <summary>
/// Normalizes product version strings for display vs. provenance.
///
/// The assembly's informational version carries full provenance, e.g.
/// <c>1.0.0-devsign.5+&lt;commit&gt;</c>. Normal consumer UI (titles, subtitles,
/// result verdicts) must never expose the Git commit SHA; it should show the
/// friendly version <c>1.0.0-devsign.5</c>. The full provenance string is
/// preserved for diagnostics/logging and is never deleted from the assembly.
/// </summary>
public static class SetupVersion
{
    /// <summary>
    /// Returns the friendly display version: the version up to (but not
    /// including) the <c>+</c> commit separator. Prerelease tags are kept intact
    /// so <c>1.0.0-beta.1</c> and <c>1.0.0-devsign.6</c> are not corrupted.
    /// A string without a <c>+</c> is returned unchanged.
    /// </summary>
    public static string Friendly(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "0.0.0";
        int plus = version.IndexOf('+');
        return (plus >= 0 ? version[..plus] : version).Trim();
    }

    /// <summary>
    /// Full provenance version as it appears in the assembly informational
    /// version (includes the <c>+</c> commit suffix when present). Pass-through
    /// for explicit diagnostic use.
    /// </summary>
    public static string Full(string? version) => version ?? "0.0.0";
}
