namespace PathVeer.Core.State;

/// <summary>
/// Single authoritative resolver for PathVeer application persistent-state roots.
///
/// Phase 36.3 migrates the active state root from the legacy
/// <c>%ProgramData%\IranDirect</c> directory to <c>%ProgramData%\PathVeer</c>.
/// This type makes the three root concepts explicit and keeps the
/// <c>IranDirect</c> / <c>PathVeer</c> root strings in exactly one place instead
/// of scattered through production code.
///
/// The legacy root name is intentionally frozen: it identifies pre-PathVeer
/// installations that must be migrated, and it must not be reinterpreted as
/// live runtime storage after migration completes.
/// </summary>
public sealed class StateRootResolver
{
    /// <summary>Legacy (pre-PathVeer) state directory name. Frozen identity.</summary>
    public const string LegacyStateDirectoryName = "IranDirect";

    /// <summary>Current (PathVeer) authoritative state directory name.</summary>
    public const string CurrentStateDirectoryName = "PathVeer";

    private readonly string _commonApplicationData;

    /// <summary>
    /// Uses the real <see cref="Environment.SpecialFolder.CommonApplicationData"/>
    /// location (<c>%ProgramData%</c>).
    /// </summary>
    public StateRootResolver()
        : this(Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData))
    {
    }

    /// <summary>
    /// Uses an explicit common-data base (test seam for isolated roots).
    /// </summary>
    public StateRootResolver(string commonApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commonApplicationData);
        _commonApplicationData = commonApplicationData;
    }

    /// <summary>Base directory shared by both roots (e.g. <c>%ProgramData%</c>).</summary>
    public string BaseDirectory => _commonApplicationData;

    /// <summary>Legacy state root, e.g. <c>%ProgramData%\IranDirect</c>.</summary>
    public string LegacyRoot =>
        Path.Combine(_commonApplicationData, LegacyStateDirectoryName);

    /// <summary>Current authoritative state root, e.g. <c>%ProgramData%\PathVeer</c>.</summary>
    public string CurrentRoot =>
        Path.Combine(_commonApplicationData, CurrentStateDirectoryName);

    /// <summary>
    /// Resolves the current authoritative root against the real
    /// <c>%ProgramData%</c> location. Use from static contexts (e.g. a default
    /// store directory) that cannot hold a resolver instance.
    /// </summary>
    public static string ResolveCurrentRoot() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            CurrentStateDirectoryName);

    public bool LegacyExists => Directory.Exists(LegacyRoot);

    public bool CurrentExists => Directory.Exists(CurrentRoot);

    /// <summary>
    /// Builds a unique, non-authoritative temporary migration directory path under
    /// the shared base, e.g. <c>%ProgramData%\PathVeer.migrating-&lt;guid&gt;</c>.
    /// Such a directory must never be treated as the final authoritative root.
    /// </summary>
    public string CreateTempMigrationRoot() =>
        Path.Combine(
            _commonApplicationData,
            $"{CurrentStateDirectoryName}.migrating-{Guid.NewGuid():N}");

    /// <summary>
    /// Enumerates abandoned temporary migration directories. These are never
    /// authoritative and may be discarded.
    /// </summary>
    public IReadOnlyList<string> ListTempMigrationRoots() =>
        Directory.Exists(_commonApplicationData)
            ? Directory
                .EnumerateDirectories(
                    _commonApplicationData,
                    $"{CurrentStateDirectoryName}.migrating-*")
                .ToList()
            : [];
}
