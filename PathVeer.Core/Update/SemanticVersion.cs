namespace PathVeer.Core.Update;

/// <summary>
/// Phase 37.3 — SemVer-aware version comparison for update selection.
///
/// This follows the SAME prerelease convention established in
/// Directory.Build.props (1.0.0, 1.0.0-beta.1, 1.0.0-rc.1) but, unlike the
/// install-state classifier (PathVeer.Core.Installer.InstallStateClassifier,
/// which is intentionally prerelease-INSENSITIVE for downgrade blocking), it
/// respects prerelease ordering so update channels can rank beta/rc correctly:
///   1.0.0-beta.1 < 1.0.0-rc.1 < 1.0.0
/// This is the same version family, not a second/contradictory parser.
/// </summary>
public readonly struct SemanticVersion : IComparable<SemanticVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? Prerelease { get; }

    public SemanticVersion(int major, int minor, int patch, string? prerelease = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = string.IsNullOrWhiteSpace(prerelease) ? null : prerelease.Trim();
    }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = new SemanticVersion(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value!.Trim();
        string core = v, pre = "";
        int dash = v.IndexOf('-');
        if (dash >= 0) { core = v[..dash]; pre = v[(dash + 1)..]; }
        var parts = core.Split('.');
        if (parts.Length < 3) return false;
        if (!int.TryParse(parts[0], out var maj) || !int.TryParse(parts[1], out var min) || !int.TryParse(parts[2], out var pat))
            return false;
        version = new SemanticVersion(maj, min, pat, pre);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string? a, string? b)
    {
        // A release with no prerelease outranks any prerelease of the same core.
        bool aPre = !string.IsNullOrEmpty(a);
        bool bPre = !string.IsNullOrEmpty(b);
        if (!aPre && !bPre) return 0;
        if (!aPre) return 1;
        if (!bPre) return -1;
        return string.CompareOrdinal(a, b);
    }

    public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;

    public override string ToString() => string.IsNullOrEmpty(Prerelease) ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
