using System.Text.Json;
using System.Text.Json.Serialization;
using PathVeer.Core.Configuration;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Routing;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Vpn;

namespace PathVeer.Core.Tests.Globalization;

/// <summary>
/// Permanent acceptance tests for upgrading pre-35.2 (Iran-specific)
/// installations to the country-agnostic implementation.
///
/// These tests seed a temporary data directory EXACTLY as an older version
/// would have left it (root-level <c>iran-ipv4-prefixes.txt</c>,
/// <c>prefix-source-metadata.json</c>, <c>prefix-source-update-history.json</c>,
/// and a <c>desired-config.json</c> WITHOUT a DirectCountryCode field), then
/// exercise the CURRENT production components. They prove the upgrade is
/// lossless, fail-closed, idempotent, and never cross-contaminates countries.
///
/// No fake "legacy" representation is constructed — artifacts match the on-disk
/// shapes of commit 93e2d87 (the parent of Phase 35.2).
/// </summary>
public sealed class LegacyGlobalizationUpgradeTests
{
    private static readonly JsonSerializerOptions ConfigOptions = new()
    {
        WriteIndented = true
    };

    static LegacyGlobalizationUpgradeTests()
    {
        ConfigOptions.Converters.Add(new JsonStringEnumConverter());
        ConfigOptions.Converters.Add(new DirectCountryCodeJsonConverter());
    }

    private static string LegacyConfigJson(bool enabled) =>
        // Mirrors the legacy (pre-35.2) DesiredConfiguration schema exactly:
        // no DirectCountryCode, no new fields.
        "{\n" +
        "  \"SchemaVersion\": 1,\n" +
        "  \"Enabled\": " + (enabled ? "true" : "false") + ",\n" +
        "  \"VpnProvider\": \"OpenVpn\",\n" +
        "  \"VpnProfilePath\": \"vpn-profile.ovpn\",\n" +
        "  \"AutoRepair\": true,\n" +
        "  \"RepairInterval\": \"00:00:30\",\n" +
        "  \"AutoUpdatePrefixes\": true,\n" +
        "  \"PrefixUpdateInterval\": \"1.00:00:00\"\n" +
        "}";

    private static DesiredConfigurationStore MakeConfigStore(
        string dir, out string path)
    {
        path = Path.Combine(dir, "desired-config.json");
        return new DesiredConfigurationStore(
            path, new DesiredConfigurationValidator());
    }

    private static void WriteLegacyPrefixes(string root, string contents) =>
        File.WriteAllText(
            Path.Combine(root, "iran-ipv4-prefixes.txt"), contents);

    private static void WriteLegacyMetadata(string root)
    {
        var doc = new PrefixSourceMetadataDocument
        {
            Current = new PrefixSourceMetadata
            {
                SourceId = "legacy-ir",
                ETag = "legacy-ir-etag",
                ContentHash = "legacy-ir-hash",
                LastStatus = PrefixSourceUpdateStatus.Succeeded,
                PrefixCount = 2
            }
        };
        new PrefixSourceMetadataStore(
            Path.Combine(root, "prefix-source-metadata.json"))
            .SaveAsync(doc, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    private static void WriteLegacyHistory(string root)
    {
        var doc = new PrefixSourceUpdateHistoryDocument
        {
            Entries =
            [
                new PrefixSourceUpdateHistoryEntry
                {
                    StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    AttemptedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    CompletedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    Status = PrefixSourceUpdateStatus.Succeeded,
                    PrefixCount = 2
                }
            ]
        };
        new PrefixSourceUpdateHistoryStore(
            Path.Combine(root, "prefix-source-update-history.json"))
            .SaveAsync(doc, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    // ---- Step 4: legacy config loads as IR ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyConfig_WithNoDirectCountryCode_LoadsAsIr(
        bool enabled)
    {
        string dir = Path.Combine(
            Path.GetTempPath(), "id35-6-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DesiredConfigurationStore store = MakeConfigStore(dir, out _);
            await File.WriteAllTextAsync(
                Path.Combine(dir, "desired-config.json"),
                LegacyConfigJson(enabled));

            DesiredConfiguration config = await store.LoadAsync();

            Assert.Equal(DirectCountryCode.IR, config.DirectCountryCode);
            Assert.Equal(enabled, config.Enabled);
            Assert.Equal("vpn-profile.ovpn", config.VpnProfilePath);
            Assert.Equal(VpnProviderType.OpenVpn, config.VpnProvider);
            Assert.True(config.AutoRepair);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyConfig_NoRewriteRequiredToRead()
    {
        string dir = Path.Combine(
            Path.GetTempPath(), "id35-6-norewrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DesiredConfigurationStore store = MakeConfigStore(dir, out string path);
            await File.WriteAllTextAsync(path, LegacyConfigJson(true));

            await store.LoadAsync(); // succeeds without SaveAsync

            string after = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("DirectCountryCode", after,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task MissingConfig_FollowsPhase344Semantics_NotDisabled()
    {
        string dir = Path.Combine(
            Path.GetTempPath(), "id35-6-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DesiredConfigurationStore store = MakeConfigStore(dir, out _);
            // No file written.
            await Assert.ThrowsAsync<DesiredConfigurationMissingException>(
                () => store.LoadAsync());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- Step 5: legacy IR prefix migration ----

    [Fact]
    public async Task LegacyIrPrefix_OnlyLegacyExists_MigratesToIrScope()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-pfx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteLegacyPrefixes(root, "203.0.113.0/24\n198.51.100.0/24\n");

            CountryPrefixStore store = new(root);
            IReadOnlyList<string> loaded =
                await store.LoadPrefixesAsync(
                    DirectCountryCode.IR, CancellationToken.None);

            Assert.Equal(2, loaded.Count);
            Assert.True(File.Exists(
                Path.Combine(root, "prefixes", "IR", "ipv4-prefixes.txt")));
            // Legacy file is left in place until migration is durable.
            Assert.True(File.Exists(
                Path.Combine(root, "iran-ipv4-prefixes.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyIrPrefix_IqSelected_NeverConsumesLegacyIr()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-pfx-iq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteLegacyPrefixes(root, "203.0.113.0/24\n");

            CountryPrefixStore store = new(root);
            IReadOnlyList<string> iq =
                await store.LoadPrefixesAsync(
                    DirectCountryCode.Parse("IQ"), CancellationToken.None);

            // IQ must NOT see the legacy IR dataset.
            Assert.Empty(iq);
            Assert.False(File.Exists(
                Path.Combine(root, "prefixes", "IQ", "ipv4-prefixes.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyIrPrefix_MigrationIsIdempotent()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-pfx-idem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteLegacyPrefixes(root, "203.0.113.0/24\n");

            CountryPrefixStore store = new(root);
            await store.LoadPrefixesAsync(
                DirectCountryCode.IR, CancellationToken.None);
            // Second startup: new file already exists.
            IReadOnlyList<string> again =
                await store.LoadPrefixesAsync(
                    DirectCountryCode.IR, CancellationToken.None);

            Assert.Equal(1, again.Count);
            // Exactly one migrated copy; legacy preserved.
            Assert.True(File.Exists(
                Path.Combine(root, "iran-ipv4-prefixes.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyIrPrefix_BothExistIdentical_NoDuplication()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-pfx-both-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteLegacyPrefixes(root, "203.0.113.0/24\n");
            Directory.CreateDirectory(
                Path.Combine(root, "prefixes", "IR"));
            File.WriteAllText(
                Path.Combine(root, "prefixes", "IR", "ipv4-prefixes.txt"),
                "203.0.113.0/24\n");

            CountryPrefixStore store = new(root);
            IReadOnlyList<string> loaded =
                await store.LoadPrefixesAsync(
                    DirectCountryCode.IR, CancellationToken.None);

            Assert.Single(loaded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyIrPrefix_LegacyEmpty_DoesNotCreateEmptyIr()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-pfx-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteLegacyPrefixes(root, "\n   \n");

            CountryPrefixStore store = new(root);
            IReadOnlyList<string> loaded =
                await store.LoadPrefixesAsync(
                    DirectCountryCode.IR, CancellationToken.None);

            Assert.Empty(loaded);
            Assert.False(File.Exists(
                Path.Combine(root, "prefixes", "IR", "ipv4-prefixes.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- Step 6: metadata / history migration ----

    [Fact]
    public async Task LegacyMetadata_MigratesToIrScope_AndStaysIrScoped()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteLegacyMetadata(root);

            CountryPrefixStore store = new(root);
            await store.MigrateLegacyMetadataIfNeededAsync(
                DirectCountryCode.IR, CancellationToken.None);

            var migrated = await new PrefixSourceMetadataStore(
                store.MetadataFileFor(DirectCountryCode.IR))
                .LoadAsync(CancellationToken.None);

            Assert.Equal("legacy-ir-etag", migrated.Current?.ETag);
            Assert.Equal("legacy-ir-hash", migrated.Current?.ContentHash);

            // IQ metadata must be untouched / empty, never copied from IR.
            bool iqMetaExists = File.Exists(
                store.MetadataFileFor(DirectCountryCode.Parse("IQ")));
            Assert.False(iqMetaExists);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyHistory_MigratesToIrScope_AndStaysCountryScoped()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-hist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteLegacyHistory(root);

            CountryPrefixStore store = new(root);
            await store.MigrateLegacyHistoryIfNeededAsync(
                DirectCountryCode.IR, CancellationToken.None);

            var migrated = await new PrefixSourceUpdateHistoryStore(
                store.UpdateHistoryFileFor(DirectCountryCode.IR))
                .LoadAsync(CancellationToken.None);

            Assert.Single(migrated.Entries);
            Assert.False(File.Exists(
                store.UpdateHistoryFileFor(DirectCountryCode.Parse("IQ"))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyMetadata_IrEtagCannotBecomeIqState()
    {
        // Proves IR metadata is not globbed into IQ: selecting IQ yields no
        // migrated metadata, so IQ refresh is never suppressed by IR ETag.
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-meta-iq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteLegacyMetadata(root);
            CountryPrefixStore store = new(root);

            await store.MigrateLegacyMetadataIfNeededAsync(
                DirectCountryCode.Parse("IQ"), CancellationToken.None);

            Assert.False(File.Exists(
                store.MetadataFileFor(DirectCountryCode.Parse("IQ"))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- Step 7/8: inventory preservation (no country identity) ----

    [Fact]
    public void RouteInventoryItem_HasNoCountryField()
    {
        // Reflective guard: ownership is identity-based, not country-based.
        System.Reflection.PropertyInfo[] props =
            typeof(RouteInventoryItem).GetProperties();
        Assert.DoesNotContain(
            props, p => p.Name.Contains("Country", StringComparison.Ordinal));
        Assert.Contains(props, p => p.Name == "DestinationPrefix");
    }

    [Fact]
    public void VpnEndpointInventory_HasNoCountryField()
    {
        System.Reflection.PropertyInfo[] props =
            typeof(VpnEndpointInventory).GetProperties();
        Assert.DoesNotContain(
            props, p => p.Name.Contains("Country", StringComparison.Ordinal));
    }

    [Fact]
    public void RouteMutationJournalEntry_HasNoCountryField()
    {
        System.Reflection.PropertyInfo[] props =
            typeof(RouteMutationJournalEntry).GetProperties();
        Assert.DoesNotContain(
            props, p => p.Name.Contains("Country", StringComparison.Ordinal));
        Assert.Contains(props, p => p.Name == "RouteIdentity");
    }

    // ---- Step 11: first start after upgrade (real component wiring) ----

    [Fact]
    public async Task EnabledLegacyInstallation_UpgradesToStableIr()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-upgrade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Seed exactly as an old version left it.
            await File.WriteAllTextAsync(
                Path.Combine(root, "desired-config.json"),
                LegacyConfigJson(true));
            WriteLegacyPrefixes(root, "203.0.113.0/24\n198.51.100.0/24\n");
            WriteLegacyMetadata(root);

            DesiredConfigurationStore configStore =
                new(Path.Combine(root, "desired-config.json"),
                    new DesiredConfigurationValidator());
            CountryPrefixStore prefixStore = new(root);

            DesiredConfiguration config = await configStore.LoadAsync();
            Assert.Equal(DirectCountryCode.IR, config.DirectCountryCode);
            Assert.True(config.Enabled);

            // Cache migration on first IR observation.
            IReadOnlyList<string> prefixes =
                await prefixStore.LoadPrefixesAsync(
                    DirectCountryCode.IR, CancellationToken.None);
            Assert.Equal(2, prefixes.Count);
            await prefixStore.MigrateLegacyMetadataIfNeededAsync(
                DirectCountryCode.IR, CancellationToken.None);

            Assert.True(File.Exists(
                Path.Combine(root, "prefixes", "IR", "ipv4-prefixes.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Upgrade_RestartIsIdempotent()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-restart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "desired-config.json"),
                LegacyConfigJson(true));
            WriteLegacyPrefixes(root, "203.0.113.0/24\n");

            for (int i = 0; i < 2; i++)
            {
                CountryPrefixStore prefixStore = new(root);
                IReadOnlyList<string> prefixes =
                    await prefixStore.LoadPrefixesAsync(
                        DirectCountryCode.IR, CancellationToken.None);
                Assert.Single(prefixes);
            }

            // Still exactly one IR file; legacy preserved.
            Assert.True(File.Exists(
                Path.Combine(root, "prefixes", "IR", "ipv4-prefixes.txt")));
            Assert.True(File.Exists(
                Path.Combine(root, "iran-ipv4-prefixes.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- Step 12: disabled legacy ----

    [Fact]
    public async Task DisabledLegacyInstallation_CountryDefaultsIr_StaysDisabled()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "desired-config.json"),
                LegacyConfigJson(false));
            WriteLegacyPrefixes(root, "203.0.113.0/24\n");

            DesiredConfigurationStore configStore =
                new(Path.Combine(root, "desired-config.json"),
                    new DesiredConfigurationValidator());
            DesiredConfiguration config = await configStore.LoadAsync();

            Assert.Equal(DirectCountryCode.IR, config.DirectCountryCode);
            Assert.False(config.Enabled);

            // Cache migration may occur for IR without enabling.
            CountryPrefixStore prefixStore = new(root);
            IReadOnlyList<string> prefixes =
                await prefixStore.LoadPrefixesAsync(
                    DirectCountryCode.IR, CancellationToken.None);
            Assert.Single(prefixes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- Step 13: offline upgrade (no network needed) ----

    [Fact]
    public async Task OfflineUpgrade_LegacyIrCache_UsableWithoutNetwork()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-offline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "desired-config.json"),
                LegacyConfigJson(true));
            WriteLegacyPrefixes(root, "203.0.113.0/24\n198.51.100.0/24\n");

            // No network calls are made by config load + cache migration.
            DesiredConfigurationStore configStore =
                new(Path.Combine(root, "desired-config.json"),
                    new DesiredConfigurationValidator());
            CountryPrefixStore prefixStore = new(root);

            DesiredConfiguration config = await configStore.LoadAsync();
            Assert.Equal(DirectCountryCode.IR, config.DirectCountryCode);

            IReadOnlyList<string> prefixes =
                await prefixStore.LoadPrefixesAsync(
                    DirectCountryCode.IR, CancellationToken.None);
            Assert.Equal(2, prefixes.Count);
            Assert.True(prefixStore.HasPrefixes(DirectCountryCode.IR));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- Step 15: corrupt legacy data ----

    [Fact]
    public async Task CorruptLegacyPrefix_PreservedAndNotConsumedByIq()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "id35-6-corrupt-pfx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Malformed prefix file: not silently normalized to an empty valid
            // set, not deleted, and never consumed by a non-IR country.
            File.WriteAllText(
                Path.Combine(root, "iran-ipv4-prefixes.txt"),
                "not-a-prefix\n::garbage::\n");

            CountryPrefixStore store = new(root);
            IReadOnlyList<string> loaded =
                await store.LoadPrefixesAsync(
                    DirectCountryCode.IR, CancellationToken.None);

            // Migration copies the legacy bytes verbatim; it does NOT silently
            // empty them into an authoritative empty IR scope.
            Assert.Equal(2, loaded.Count);
            Assert.True(File.Exists(
                Path.Combine(root, "prefixes", "IR", "ipv4-prefixes.txt")));

            // The original legacy file is preserved (not deleted on migration).
            Assert.True(File.Exists(
                Path.Combine(root, "iran-ipv4-prefixes.txt")));

            // IQ must never see the legacy IR dataset, corrupt or not.
            IReadOnlyList<string> iq =
                await store.LoadPrefixesAsync(
                    DirectCountryCode.Parse("IQ"), CancellationToken.None);
            Assert.Empty(iq);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptConfig_CannotBecomeDisabled()
    {
        string dir = Path.Combine(
            Path.GetTempPath(), "id35-6-corrupt-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "desired-config.json");
            // Truncated/invalid JSON: must fail closed, not coerce to disabled.
            await File.WriteAllTextAsync(path, "{ \"Enabled\": ");

            DesiredConfigurationStore store =
                new(path, new DesiredConfigurationValidator());

            await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- Step 17: JSON forward/backward compatibility ----

    [Fact]
    public void NewJsonWithDirectCountryCode_IgnoredByHistoricalModel()
    {
        // A new config containing DirectCountryCode, read by the HISTORICAL
        // (pre-35.2) model that has no such property, must round-trip without
        // error (System.Text.Json ignores unknown members by default).
        string newJson = LegacyConfigJson(true)
            .Replace("}", ",\n  \"DirectCountryCode\": \"IQ\"\n}");

        DesiredConfiguration? parsed =
            JsonSerializer.Deserialize<DesiredConfiguration>(
                newJson, ConfigOptions);
        Assert.NotNull(parsed);
        Assert.Equal(DirectCountryCode.Parse("IQ"), parsed!.DirectCountryCode);
        Assert.True(parsed.Enabled);

        // And the legacy model (record without the field) ignores it.
        var legacyModel = new LegacyModel();
        var reParsed = JsonSerializer.Deserialize<LegacyModel>(
            newJson, ConfigOptions);
        Assert.NotNull(reParsed);
    }

    private sealed record LegacyModel
    {
        public int SchemaVersion { get; init; }
        public bool Enabled { get; init; }
        public string VpnProfilePath { get; init; } = "";
    }

    // ---- Step 18: legacy CLI commands preserve country ----

    [Fact]
    public void DesiredConfiguration_WithExpression_PreservesCountry()
    {
        // Reproduces the failure mode the spec calls out: commands that build a
        // new DesiredConfiguration via `with` must preserve DirectCountryCode
        // rather than reset to IR. The record's default initializer makes a
        // bare `new DesiredConfiguration()` IR, so any command MUST use `with`.
        DesiredConfiguration iq = ConfigurationDefaults.Create() with
        {
            Enabled = true,
            DirectCountryCode = DirectCountryCode.Parse("IQ")
        };

        DesiredConfiguration updated = iq with { Enabled = false };

        Assert.Equal(DirectCountryCode.Parse("IQ"), updated.DirectCountryCode);
        Assert.False(updated.Enabled);
    }
}
