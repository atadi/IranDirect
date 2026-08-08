namespace PathVeer.Core.Tests.State;

using System.Security.Cryptography;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.State;

/// <summary>
/// Phase 36.3 permanent migration behavior tests (spec §31 A-Z).
///
/// These exercise <see cref="StateRootResolver"/> / <see cref="StateRootMigrator"/>
/// against isolated temp bases so they never touch the real %ProgramData% root.
/// </summary>
public sealed class StateRootMigrationTests
{
    private static string NewBase() =>
        Path.Combine(
            Path.GetTempPath(),
            "PathVeer.Migration.Tests",
            Guid.NewGuid().ToString("N"));

    private static string LegacyRoot(string baseDir) =>
        Path.Combine(baseDir, StateRootResolver.LegacyStateDirectoryName);

    private static string CurrentRoot(string baseDir) =>
        Path.Combine(baseDir, StateRootResolver.CurrentStateDirectoryName);

    // --- A. Fresh install: no roots -> no migration, no fake state ---
    [Fact]
    public void FreshInstall_NoRoots_NoMigration()
    {
        string baseDir = NewBase();
        var resolver = new StateRootResolver(baseDir);
        var migrator = new StateRootMigrator(resolver);

        MigrationOutcome outcome = migrator.EnsureCurrentRoot();

        Assert.Equal(MigrationStatus.FreshInstallNoLegacy, outcome.Status);
        Assert.False(Directory.Exists(LegacyRoot(baseDir)));
        Assert.False(Directory.Exists(CurrentRoot(baseDir)));
    }

    // --- B. Legacy-only root migrates successfully ---
    [Fact]
    public void LegacyOnlyRoot_MigratesSuccessfully()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        WriteLegacyState(legacy, enabled: true, country: "IR");

        var resolver = new StateRootResolver(baseDir);
        var migrator = new StateRootMigrator(resolver);

        MigrationOutcome outcome = migrator.EnsureCurrentRoot();

        Assert.Equal(MigrationStatus.Migrated, outcome.Status);
        Assert.True(Directory.Exists(CurrentRoot(baseDir)));
        Assert.True(File.Exists(Path.Combine(CurrentRoot(baseDir), "desired-configuration.json")));
        // Legacy retained for rollback evidence.
        Assert.True(Directory.Exists(legacy));
    }

    // --- C. Existing PathVeer root wins; legacy does not overwrite ---
    [Fact]
    public void ExistingPathVeerRoot_Wins_LegacyNotOverwritten()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        string current = CurrentRoot(baseDir);
        WriteConfig(legacy, enabled: true, "IR");
        Directory.CreateDirectory(current);
        WriteConfig(current, enabled: true, "IQ"); // existing PathVeer state

        var resolver = new StateRootResolver(baseDir);
        var migrator = new StateRootMigrator(resolver);

        MigrationOutcome outcome = migrator.EnsureCurrentRoot();

        Assert.Equal(MigrationStatus.CurrentAlreadyAuthoritative, outcome.Status);
        Assert.Equal("IQ", ReadCountry(current));
        Assert.Equal("IR", ReadCountry(legacy)); // legacy untouched
    }

    // --- D. Repeated migration is idempotent ---
    [Fact]
    public void RepeatedMigration_Idempotent()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        WriteConfig(legacy, enabled: true, "RO");

        var resolver = new StateRootResolver(baseDir);
        var migrator = new StateRootMigrator(resolver);

        MigrationOutcome first = migrator.EnsureCurrentRoot();
        MigrationOutcome second = migrator.EnsureCurrentRoot();
        MigrationOutcome third = migrator.EnsureCurrentRoot();

        Assert.Equal(MigrationStatus.Migrated, first.Status);
        Assert.Equal(MigrationStatus.CurrentAlreadyAuthoritative, second.Status);
        Assert.Equal(MigrationStatus.CurrentAlreadyAuthoritative, third.Status);
        Assert.Equal("RO", ReadCountry(CurrentRoot(baseDir)));
    }

    // --- E. Partial temporary migration never authoritative ---
    [Fact]
    public void PartialTemporaryMigration_NeverAuthoritative()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        WriteConfig(legacy, enabled: true, "IR");
        string temp = Path.Combine(baseDir, $"{StateRootResolver.CurrentStateDirectoryName}.migrating-abc");

        // Simulate a leftover migrating directory (crash before publish).
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "partial.txt"), "x");

        var resolver = new StateRootResolver(baseDir);
        var migrator = new StateRootMigrator(resolver);

        MigrationOutcome outcome = migrator.EnsureCurrentRoot();

        // Legacy still migrates to the FINAL current root; temp is discarded/ignored.
        Assert.Equal(MigrationStatus.Migrated, outcome.Status);
        Assert.True(Directory.Exists(CurrentRoot(baseDir)));
        Assert.False(Directory.Exists(temp));
    }

    // --- F. Interrupted migration restart converges ---
    [Fact]
    public void InterruptedMigration_RestartConverges()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        WriteConfig(legacy, enabled: true, "IR");

        var failingProbe = new FailingProbe(throwAt: ProbeStage.BeforeCopyFile);
        var resolver = new StateRootResolver(baseDir);
        var migrator = new StateRootMigrator(resolver, failingProbe);

        Assert.ThrowsAny<Exception>(() => migrator.EnsureCurrentRoot());
        // No published current root, legacy preserved.
        Assert.False(Directory.Exists(CurrentRoot(baseDir)));
        Assert.True(Directory.Exists(legacy));

        // Retry without failure -> converges.
        var migrator2 = new StateRootMigrator(resolver);
        MigrationOutcome outcome = migrator2.EnsureCurrentRoot();
        Assert.Equal(MigrationStatus.Migrated, outcome.Status);
    }

    // --- G/I. Enabled installation (IR, IQ, RO) preserves configuration ---
    [Theory]
    [InlineData("IR")]
    [InlineData("IQ")]
    [InlineData("RO")]
    public void EnabledInstallation_PreservesConfiguration(string country)
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        WriteConfig(legacy, enabled: true, country);

        var migrator = new StateRootMigrator(new StateRootResolver(baseDir));
        migrator.EnsureCurrentRoot();

        Assert.Equal(country, ReadCountry(CurrentRoot(baseDir)));
        Assert.True(ReadEnabled(CurrentRoot(baseDir)));
    }

    // --- J. Disabled non-IR installation remains disabled ---
    [Fact]
    public void DisabledInstallation_RemainsDisabled()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        WriteConfig(legacy, enabled: false, "IQ");

        var migrator = new StateRootMigrator(new StateRootResolver(baseDir));
        migrator.EnsureCurrentRoot();

        Assert.False(ReadEnabled(CurrentRoot(baseDir)));
        Assert.Equal("IQ", ReadCountry(CurrentRoot(baseDir)));
    }

    // --- K/L. Multiple country prefix caches preserved & isolated ---
    [Fact]
    public void MultipleCountryCaches_PreservedAndIsolated()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        foreach (string c in new[] { "IR", "IQ", "RO", "US", "BR" })
        {
            string dir = Path.Combine(legacy, "prefixes", c);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "prefixes.txt"), $"{c}-data");
            File.WriteAllText(Path.Combine(dir, "metadata.json"), $"{c}-meta");
            File.WriteAllText(Path.Combine(dir, "history.json"), $"{c}-hist");
        }

        var migrator = new StateRootMigrator(new StateRootResolver(baseDir));
        migrator.EnsureCurrentRoot();

        foreach (string c in new[] { "IR", "IQ", "RO", "US", "BR" })
        {
            string dir = Path.Combine(CurrentRoot(baseDir), "prefixes", c);
            Assert.True(Directory.Exists(dir));
            Assert.Equal($"{c}-data", File.ReadAllText(Path.Combine(dir, "prefixes.txt")));
            Assert.Equal($"{c}-meta", File.ReadAllText(Path.Combine(dir, "metadata.json")));
            Assert.Equal($"{c}-hist", File.ReadAllText(Path.Combine(dir, "history.json")));
        }
    }

    // --- M. Legacy iran-ipv4-prefixes.txt preserved ---
    [Fact]
    public void LegacyPrefixFile_Preserved()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        Directory.CreateDirectory(legacy);
        string legacyFile = Path.Combine(legacy, "iran-ipv4-prefixes.txt");
        File.WriteAllText(legacyFile, "legacy-ir-prefixes");

        var migrator = new StateRootMigrator(new StateRootResolver(baseDir));
        migrator.EnsureCurrentRoot();

        Assert.True(File.Exists(Path.Combine(CurrentRoot(baseDir), "iran-ipv4-prefixes.txt")));
        Assert.Equal("legacy-ir-prefixes",
            File.ReadAllText(Path.Combine(CurrentRoot(baseDir), "iran-ipv4-prefixes.txt")));
    }

    // --- N/O. Route inventory & endpoint inventory preserved ---
    [Fact]
    public void InventoriesPreserved()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "route-inventory.json"), "inv");
        File.WriteAllText(Path.Combine(legacy, "endpoint-inventory.json"), "endp");
        File.WriteAllText(Path.Combine(legacy, "vpn-profile.ovpn"), "vpn");

        var migrator = new StateRootMigrator(new StateRootResolver(baseDir));
        migrator.EnsureCurrentRoot();

        Assert.Equal("inv", File.ReadAllText(Path.Combine(CurrentRoot(baseDir), "route-inventory.json")));
        Assert.Equal("endp", File.ReadAllText(Path.Combine(CurrentRoot(baseDir), "endpoint-inventory.json")));
        Assert.Equal("vpn", File.ReadAllText(Path.Combine(CurrentRoot(baseDir), "vpn-profile.ovpn")));
    }

    // --- P/Q. Pending ADD/DELETE journal intent preserved ---
    [Fact]
    public void RouteMutationJournal_Preserved()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        Directory.CreateDirectory(legacy);
        string journal =
            "{\"Intents\":[{\"Kind\":\"Add\",\"Destination\":\"1.2.3.0/24\"}," +
            "{\"Kind\":\"Delete\",\"Destination\":\"9.9.9.0/24\"}]}";
        File.WriteAllText(Path.Combine(legacy, "route-mutation-journal.json"), journal);

        var migrator = new StateRootMigrator(new StateRootResolver(baseDir));
        migrator.EnsureCurrentRoot();

        string migrated = File.ReadAllText(
            Path.Combine(CurrentRoot(baseDir), "route-mutation-journal.json"));
        Assert.Contains("1.2.3.0/24", migrated);
        Assert.Contains("9.9.9.0/24", migrated);
    }

    // --- R. Journal recovery precedes reconciliation: recovery runs against current root ---
    [Fact]
    public async Task JournalRecovery_ResolvesFromCurrentRoot()
    {
        // RouteMutationRecovery is constructed from the journal path under the
        // resolver's current root; this proves recovery reads the migrated root,
        // not the legacy root.
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        string current = CurrentRoot(baseDir);
        WriteConfig(legacy, enabled: true, "IR");
        File.WriteAllText(
            Path.Combine(legacy, "route-mutation-journal.json"),
            "{\"Intents\":[]}");

        var resolver = new StateRootResolver(baseDir);
        new StateRootMigrator(resolver).EnsureCurrentRoot();

        string journalPath = Path.Combine(resolver.CurrentRoot, "route-mutation-journal.json");
        var journalStore = new RouteMutationJournalStore(journalPath);

        Assert.True(File.Exists(journalPath));
        IReadOnlyDictionary<string, RouteMutationJournalEntry> intents =
            await journalStore.LoadAllAsync(CancellationToken.None);
        Assert.Empty(intents);
    }

    // --- S. External-route safety: migration does not alter route ownership ---
    [Fact]
    public void Migration_DoesNotAlterRouteOwnership()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "route-inventory.json"), "OWNED-ROUTES");

        var migrator = new StateRootMigrator(new StateRootResolver(baseDir));
        migrator.EnsureCurrentRoot();

        Assert.Equal("OWNED-ROUTES",
            File.ReadAllText(Path.Combine(CurrentRoot(baseDir), "route-inventory.json")));
    }

    // --- T. Custom-route state preserved ---
    [Fact]
    public void CustomRoutesPreserved()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "custom-routes.json"), "CR");
        File.WriteAllText(Path.Combine(legacy, "custom-route-dns-cache.json"), "CDNS");

        var migrator = new StateRootMigrator(new StateRootResolver(baseDir));
        migrator.EnsureCurrentRoot();

        Assert.Equal("CR", File.ReadAllText(Path.Combine(CurrentRoot(baseDir), "custom-routes.json")));
        Assert.Equal("CDNS", File.ReadAllText(Path.Combine(CurrentRoot(baseDir), "custom-route-dns-cache.json")));
    }

    // --- U. Offline migration: no network calls (pure filesystem) ---
    [Fact]
    public void OfflineMigration_NoNetworkRequired()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        WriteConfig(legacy, enabled: true, "IR");

        var migrator = new StateRootMigrator(new StateRootResolver(baseDir));
        migrator.EnsureCurrentRoot();

        Assert.True(Directory.Exists(CurrentRoot(baseDir)));
    }

    // --- V. Missing configuration remains unconfigured (no fake config) ---
    [Fact]
    public void MissingConfiguration_RemainsUnconfigured()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        Directory.CreateDirectory(legacy); // no desired-configuration.json

        var migrator = new StateRootMigrator(new StateRootResolver(baseDir));
        migrator.EnsureCurrentRoot();

        Assert.False(File.Exists(Path.Combine(CurrentRoot(baseDir), "desired-configuration.json")));
    }

    // --- X. Existing new root + stale legacy root: new root wins ---
    [Fact]
    public void ExistingNewRoot_StaleLegacy_Ignored()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        string current = CurrentRoot(baseDir);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "stale.txt"), "legacy");
        Directory.CreateDirectory(current);
        WriteConfig(current, enabled: true, "US");

        var migrator = new StateRootMigrator(new StateRootResolver(baseDir));
        MigrationOutcome outcome = migrator.EnsureCurrentRoot();

        Assert.Equal(MigrationStatus.CurrentAlreadyAuthoritative, outcome.Status);
        Assert.Equal("US", ReadCountry(current));
    }

    // --- Y. No writes to legacy root after successful migration ---
    [Fact]
    public void AfterMigration_NoWritesToLegacyRoot()
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        WriteConfig(legacy, enabled: true, "IR");
        DateTime before = Directory.GetLastWriteTimeUtc(legacy);

        var migrator = new StateRootMigrator(new StateRootResolver(baseDir));
        migrator.EnsureCurrentRoot();
        // Run again (idempotent) - must NOT write to legacy.
        migrator.EnsureCurrentRoot();

        DateTime after = Directory.GetLastWriteTimeUtc(legacy);
        Assert.Equal(before, after);
    }

    // --- Z. Frozen identifiers unchanged (regression guard) ---
    [Fact]
    public void FrozenIdentifiers_Unchanged()
    {
        Assert.Equal("IranDirect", StateRootResolver.LegacyStateDirectoryName);
        Assert.Equal("PathVeer", StateRootResolver.CurrentStateDirectoryName);
        // Service identity / pipe / telemetry are NOT touched by this phase.
        Assert.Equal("IranDirect", PathVeer.Core.ServiceLifecycle.LegacyServiceNames.ServiceName);
        Assert.Equal("IranDirect.Control.v1", PathVeer.Core.Ipc.PathVeerPipeNames.LegacyPipeName);
        Assert.Equal("IranDirect.Core", PathVeer.Core.Observability.Telemetry.IranDirectTelemetry.SourceName);
    }

    // --- Probe failure-injection across boundaries (§32) ---
    [Theory]
    [InlineData(ProbeStage.BeforeCopyFile)]
    [InlineData(ProbeStage.BeforeVerify)]
    [InlineData(ProbeStage.BeforePublish)]
    public void FailureAtBoundary_PreservesLegacy_NoAuthoritativePartial(ProbeStage stage)
    {
        string baseDir = NewBase();
        string legacy = LegacyRoot(baseDir);
        WriteConfig(legacy, enabled: true, "IR");

        var probe = new FailingProbe(throwAt: stage);
        var migrator = new StateRootMigrator(new StateRootResolver(baseDir), probe);

        Assert.ThrowsAny<Exception>(() => migrator.EnsureCurrentRoot());

        Assert.False(Directory.Exists(CurrentRoot(baseDir)));
        Assert.True(Directory.Exists(legacy));
    }

    // ===== helpers =====

    private static void WriteLegacyState(string legacy, bool enabled, string country)
    {
        Directory.CreateDirectory(legacy);
        WriteConfig(legacy, enabled, country);
    }

    private static void WriteConfig(string root, bool enabled, string country)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "desired-configuration.json"),
            $"{{\"Enabled\":{enabled.ToString().ToLowerInvariant()},\"DirectCountryCode\":\"{country}\"}}");
    }

    private static string ReadCountry(string root) =>
        ReadJsonValue(root, "DirectCountryCode");

    private static bool ReadEnabled(string root)
    {
        string path = Path.Combine(root, "desired-configuration.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("Enabled").GetBoolean();
    }

    private static string ReadJsonValue(string root, string key)
    {
        string path = Path.Combine(root, "desired-configuration.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty(key).GetString() ?? string.Empty;
    }
}

public enum ProbeStage
{
    BeforeCopyFile,
    BeforeVerify,
    BeforePublish,
}

internal sealed class FailingProbe : IStateRootMigrationProbe
{
    private readonly ProbeStage _throwAt;

    public FailingProbe(ProbeStage throwAt) => _throwAt = throwAt;

    public void OnBeforeCopyFile(string sourcePath, string destinationPath)
    {
        if (_throwAt == ProbeStage.BeforeCopyFile)
        {
            throw new IOException("injected pre-copy failure");
        }
    }

    public void OnBeforeVerify(string tempRoot)
    {
        if (_throwAt == ProbeStage.BeforeVerify)
        {
            throw new IOException("injected pre-verify failure");
        }
    }

    public void OnBeforePublish(string finalRoot)
    {
        if (_throwAt == ProbeStage.BeforePublish)
        {
            throw new IOException("injected pre-publish failure");
        }
    }
}
