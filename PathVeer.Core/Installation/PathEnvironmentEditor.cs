namespace PathVeer.Core.Installation;

/// <summary>
/// Pure PATH-string manipulation for the installer.
///
/// The installer adds the PathVeer CLI directory to the machine PATH so
/// <c>pathveer</c> resolves as a command, and removes exactly that entry on
/// uninstall. Both operations must be safe to run repeatedly:
///
/// <list type="bullet">
/// <item><description>adding twice must not duplicate the entry</description></item>
/// <item><description>removing must delete only PathVeer's own entry</description></item>
/// <item><description>unrelated entries must survive byte-for-byte, in order</description></item>
/// <item><description>empty segments from historical edits must not be resurrected</description></item>
/// </list>
///
/// Keeping this as a pure function over strings is what makes those guarantees
/// permanently testable without mutating the developer's real environment.
/// </summary>
public static class PathEnvironmentEditor
{
    private const char Separator = ';';

    /// <summary>
    /// Returns <paramref name="currentPath"/> with <paramref name="entry"/>
    /// appended, or unchanged if an equivalent entry is already present.
    /// </summary>
    public static string AddEntry(string? currentPath, string entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);

        string normalizedEntry = NormalizeEntry(entry);

        List<string> segments = Split(currentPath);

        bool alreadyPresent = segments.Any(
            segment => string.Equals(
                NormalizeEntry(segment),
                normalizedEntry,
                StringComparison.OrdinalIgnoreCase));

        if (alreadyPresent)
        {
            return Join(segments);
        }

        segments.Add(entry.Trim());

        return Join(segments);
    }

    /// <summary>
    /// Returns <paramref name="currentPath"/> with every occurrence of
    /// <paramref name="entry"/> removed. Unrelated entries keep their order.
    /// </summary>
    public static string RemoveEntry(string? currentPath, string entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);

        string normalizedEntry = NormalizeEntry(entry);

        List<string> segments = Split(currentPath)
            .Where(segment => !string.Equals(
                NormalizeEntry(segment),
                normalizedEntry,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Join(segments);
    }

    /// <summary>True when an equivalent entry is already on the PATH.</summary>
    public static bool ContainsEntry(string? currentPath, string entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);

        string normalizedEntry = NormalizeEntry(entry);

        return Split(currentPath).Any(
            segment => string.Equals(
                NormalizeEntry(segment),
                normalizedEntry,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Splits a PATH value, discarding empty segments so repeated edits cannot
    /// accumulate <c>;;</c> runs.
    /// </summary>
    private static List<string> Split(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        return path
            .Split(Separator)
            .Select(segment => segment.Trim())
            .Where(segment => segment.Length > 0)
            .ToList();
    }

    private static string Join(IEnumerable<string> segments) =>
        string.Join(Separator, segments);

    /// <summary>
    /// Normalizes a segment for comparison only: trims quotes/whitespace and a
    /// single trailing separator, so <c>C:\X</c> and <c>C:\X\</c> are the same
    /// entry. The stored value keeps the caller's original spelling.
    /// </summary>
    private static string NormalizeEntry(string segment)
    {
        string trimmed = segment.Trim().Trim('"');

        if (trimmed.Length > 3
            && (trimmed.EndsWith(Path.DirectorySeparatorChar)
                || trimmed.EndsWith(Path.AltDirectorySeparatorChar)))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed;
    }
}
