namespace PathVeer.Core.State;

/// <summary>
/// Crash-safe, idempotent, single-authority migrator for the PathVeer
/// persistent-state root.
///
/// Migration model (Phase 36.3): <c>COPY -&gt; VERIFY -&gt; PUBLISH</c>.
///
///   1. Detect a legacy <c>%ProgramData%\IranDirect</c> root and the ABSENCE of a
///      current <c>%ProgramData%\PathVeer</c> root.
///   2. Copy legacy state into a unique temporary directory
///      (<c>%ProgramData%\PathVeer.migrating-&lt;guid&gt;</c>).
///   3. Verify the copy (structure + lengths; optional content hash for files).
///   4. Publish by atomically renaming the temporary directory to the final
///      <c>%ProgramData%\PathVeer</c> root.
///
/// Hard requirements enforced here:
/// <list type="bullet">
///   <item>An existing current root always wins; legacy is never copied over it.</item>
///   <item>A partial / temporary migration directory is never authoritative.</item>
///   <item>On any failure the legacy root is preserved and partial state is not
///         published.</item>
///   <item>No permanent dual-read or dual-write of runtime state.</item>
///   <item>The legacy root is never deleted by this phase.</item>
/// </list>
/// </summary>
public sealed class StateRootMigrator
{
    private static readonly string[] s_expectedTopLevelFiles =
    [
        "desired-configuration.json",
        "route-inventory.json",
        "route-mutation-journal.json",
        "state.json",
        "custom-routes.json",
        "custom-route-dns-cache.json",
        "vpn-profile.ovpn",
    ];

    private readonly StateRootResolver _resolver;
    private readonly IStateRootMigrationProbe? _probe;

    public StateRootMigrator(StateRootResolver resolver)
        : this(resolver, null)
    {
    }

    public StateRootMigrator(
        StateRootResolver resolver,
        IStateRootMigrationProbe? probe)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
        _probe = probe;
    }

    /// <summary>
    /// Ensures the current PathVeer state root is the authoritative root.
    ///
    /// Decision table:
    /// <list type="table">
    ///   <item>Current exists (valid)          -> current wins, no migration.</item>
    ///   <item>Legacy exists, current absent    -> migrate legacy, publish current.</item>
    ///   <item>Neither exists                   -> fresh install, no migration.</item>
    ///   <item>Migration fails                  -> legacy preserved, not published.</item>
    /// </list>
    /// </summary>
    /// <returns>
    /// <see cref="MigrationOutcome"/> describing what happened. Throws only for
    /// programming/IO errors that are not part of the modelled migration failure.
    /// </returns>
    public MigrationOutcome EnsureCurrentRoot()
    {
        if (_resolver.CurrentExists)
        {
            // Existing PathVeer state wins. Never re-migrate or overwrite.
            return new MigrationOutcome(
                MigrationStatus.CurrentAlreadyAuthoritative,
                _resolver.CurrentRoot,
                null);
        }

        if (!_resolver.LegacyExists)
        {
            // Fresh install. The application remains unconfigured until legitimate
            // configuration is created through the supported flow (Phase 34.4).
            return new MigrationOutcome(
                MigrationStatus.FreshInstallNoLegacy,
                null,
                null);
        }

        // Legacy candidate + no current root: migrate.
        return Migrate();
    }

    private MigrationOutcome Migrate()
    {
        _probe?.OnDetected(_resolver.LegacyRoot);

        // Discard any abandoned temporary migration directories from a prior
        // crashed run. They are never authoritative, so removing them before
        // starting keeps the base directory clean and avoids confusion.
        foreach (string stale in _resolver.ListTempMigrationRoots())
        {
            TryDeleteTemp(stale);
        }

        string tempRoot = _resolver.CreateTempMigrationRoot();
        string finalRoot = _resolver.CurrentRoot;

        _probe?.OnTempCreated(tempRoot);

        try
        {
            CopyDirectory(_resolver.LegacyRoot, tempRoot, _probe);
            _probe?.OnCopied(tempRoot);

            VerifyCopy(_resolver.LegacyRoot, tempRoot, _probe);
            _probe?.OnVerified(tempRoot);

            // Publish: atomic rename of the completed temp dir onto the final root.
            // If a crash happens before this, the temp dir is non-authoritative and
            // the legacy root remains; a restart re-runs from detection.
            _probe?.OnBeforePublish(finalRoot);
            Directory.Move(tempRoot, finalRoot);
            _probe?.OnPublished(finalRoot);

            return new MigrationOutcome(
                MigrationStatus.Migrated,
                finalRoot,
                tempRoot);
        }
        catch (Exception)
        {
            // Failure path: never publish partial state. Best-effort cleanup of the
            // non-authoritative temp directory; legacy root is untouched.
            TryDeleteTemp(tempRoot);
            _probe?.OnFailed(tempRoot);
            throw;
        }
    }

    private static void CopyDirectory(
        string source,
        string destination,
        IStateRootMigrationProbe? probe)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.EnumerateFiles(source))
        {
            string name = Path.GetFileName(file);
            probe?.OnBeforeCopyFile(file, Path.Combine(destination, name));
            File.Copy(file, Path.Combine(destination, name), overwrite: false);
        }

        foreach (string sub in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(
                sub,
                Path.Combine(destination, Path.GetFileName(sub)),
                probe);
        }
    }

    private static void VerifyCopy(
        string source,
        string destination,
        IStateRootMigrationProbe? probe)
    {
        probe?.OnBeforeVerify(destination);

        IReadOnlyList<string> sourceFiles = EnumerateAllFiles(source);
        IReadOnlyList<string> destFiles = EnumerateAllFiles(destination);

        if (sourceFiles.Count != destFiles.Count)
        {
            throw new InvalidOperationException(
                $"State migration verify failed: file count {sourceFiles.Count} " +
                $"!= {destFiles.Count}.");
        }

        // Length + content-hash verification for integrity (no rewrites of content).
        foreach (string src in sourceFiles)
        {
            string rel = src.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
            string dst = Path.Combine(destination, rel);

            if (!File.Exists(dst))
            {
                throw new InvalidOperationException(
                    $"State migration verify failed: missing '{rel}'.");
            }

            long srcLen = new FileInfo(src).Length;
            long dstLen = new FileInfo(dst).Length;
            if (srcLen != dstLen)
            {
                throw new InvalidOperationException(
                    $"State migration verify failed: length mismatch for '{rel}'.");
            }

            if (!ContentHashesEqual(src, dst))
            {
                throw new InvalidOperationException(
                    $"State migration verify failed: content mismatch for '{rel}'.");
            }
        }

        // Confirm the expected authoritative files are present when the legacy
        // root carried them; an empty legacy root is still a valid (fresh-like)
        // migration target.
        foreach (string expected in s_expectedTopLevelFiles)
        {
            string legacyPath = Path.Combine(source, expected);
            if (File.Exists(legacyPath))
            {
                string copiedPath = Path.Combine(destination, expected);
                if (!File.Exists(copiedPath))
                {
                    throw new InvalidOperationException(
                        $"State migration verify failed: expected '{expected}' absent.");
                }
            }
        }
    }

    private static IReadOnlyList<string> EnumerateAllFiles(string root)
    {
        var list = new List<string>();
        if (!Directory.Exists(root))
        {
            return list;
        }

        foreach (string file in Directory.EnumerateFiles(root))
        {
            list.Add(file);
        }

        foreach (string sub in Directory.EnumerateDirectories(root))
        {
            list.AddRange(EnumerateAllFiles(sub));
        }

        return list;
    }

    private static bool ContentHashesEqual(string a, string b)
    {
        using var ha = System.Security.Cryptography.SHA256.Create();
        using var hb = System.Security.Cryptography.SHA256.Create();
        using var sa = File.OpenRead(a);
        using var sb = File.OpenRead(b);
        byte[] da = ha.ComputeHash(sa);
        byte[] db = hb.ComputeHash(sb);
        return da.AsSpan().SequenceEqual(db);
    }

    private static void TryDeleteTemp(string tempRoot)
    {
        try
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Leave for later cleanup; never authoritative.
        }
        catch (UnauthorizedAccessException)
        {
            // Leave for later cleanup; never authoritative.
        }
    }
}

/// <summary>Outcome of <see cref="StateRootMigrator.EnsureCurrentRoot"/>.</summary>
public readonly record struct MigrationOutcome(
    MigrationStatus Status,
    string? CurrentRoot,
    string? TempRoot);

/// <summary>Discrete migration results.</summary>
public enum MigrationStatus
{
    FreshInstallNoLegacy,
    CurrentAlreadyAuthoritative,
    Migrated,
}

/// <summary>
/// Deterministic seam for failure-injection and observability during migration.
/// Implementations may throw from any hook to simulate a crash at that boundary.
/// </summary>
public interface IStateRootMigrationProbe
{
    void OnDetected(string legacyRoot) { }
    void OnTempCreated(string tempRoot) { }
    void OnBeforeCopyFile(string sourcePath, string destinationPath) { }
    void OnCopied(string tempRoot) { }
    void OnBeforeVerify(string tempRoot) { }
    void OnVerified(string tempRoot) { }
    void OnBeforePublish(string finalRoot) { }
    void OnPublished(string finalRoot) { }
    void OnFailed(string tempRoot) { }
}
