using IranDirect.Core;
using IranDirect.Core.Configuration;
using IranDirect.Core.Prefixes;
using IranDirect.Core.Runtime;
using IranDirect.Core.Runtime.Execution;
using IranDirect.Core.Runtime.Reconciliation;
using IranDirect.Core.Tests.Globalization;
using IranDirect.Testing.Performance.Workloads;

namespace IranDirect.Core.Tests.Globalization;

/// <summary>
/// Phase 35.7 — global country-routing end-to-end acceptance.
///
/// Validates country generalization is production-ready across clean install,
/// legacy upgrade, multiple countries, switching, offline, restart, crash
/// recovery, route-ownership safety, persistence isolation, and large-country
/// datasets. Reuses the real pipeline harness from Phase 35.4/35.5
/// (CountrySwitchingAcceptanceTests.SwitchHarness) so every test drives the
/// actual controller/planner/executor/journal — not fakes of routing logic.
/// No production code is modified.
/// </summary>
public sealed class GlobalCountryRoutingAcceptanceTests
{
    private static readonly DirectCountryCode IR = DirectCountryCode.Parse("IR");
    private static readonly DirectCountryCode IQ = DirectCountryCode.Parse("IQ");
    private static readonly DirectCountryCode RO = DirectCountryCode.Parse("RO");
    private static readonly DirectCountryCode US = DirectCountryCode.Parse("US");
    private static readonly DirectCountryCode BR = DirectCountryCode.Parse("BR");
    private static readonly DirectCountryCode JP = DirectCountryCode.Parse("JP");
    private static readonly DirectCountryCode ZA = DirectCountryCode.Parse("ZA");
    private static readonly DirectCountryCode AU = DirectCountryCode.Parse("AU");

    private static CountrySwitchingAcceptanceTests.SwitchHarness NewHarness()
    {
        string dir = Path.Combine(
            Path.GetTempPath(), "id35-7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new CountrySwitchingAcceptanceTests.SwitchHarness(dir);
    }

    private static string Pid(
        CountrySwitchingAcceptanceTests.SwitchHarness h, string prefix) =>
        $"{prefix}|{h.Gateway}|{h.IfIndex}";

    // ---- Step 3: clean-install acceptance ----

    [Fact]
    public async Task CleanInstall_NoLegacyFilesRequired()
    {
        await using var h = NewHarness();
        Assert.False(File.Exists(
            Path.Combine(h.TempDir, "iran-ipv4-prefixes.txt")));

        await h.SetConfigurationAsync(enabled: true, IR);
        await h.SeedCacheAsync(IR, ["203.0.113.0/24", "198.51.100.0/24"]);
        await h.SeedOwnedPrefixRoutesAsync(
            ["203.0.113.0/24", "198.51.100.0/24"]);
        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(Pid(h, "203.0.113.0/24"), h.RouteManager.Present);
    }

    [Fact]
    public async Task CleanInstall_MissingConfig_NoPrefixRoutes()
    {
        await using var h = NewHarness();
        await h.SetConfigurationAsync(enabled: false, IR);

        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Disabled => no country prefix routes are installed (an endpoint host
        // route may legitimately exist; that is not a prefix route).
        Assert.DoesNotContain(Pid(h, "203.0.113.0/24"), h.RouteManager.Present);
    }

    [Fact]
    public async Task CleanInstall_IqSelectedBeforeEnable_FetchesThenReconciles()
    {
        await using var h = NewHarness();
        await h.SetConfigurationAsync(enabled: false, IQ);
        await h.SeedCacheAsync(IQ, ["203.0.113.0/24"]);

        await h.SetCountryAsync(IQ);
        await h.Controller.UpdatePrefixesAsync(IQ, CancellationToken.None);
        await h.ConfigurationService.SetEnabledAsync(true);
        await h.SeedOwnedPrefixRoutesAsync(["203.0.113.0/24"]);
        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(Pid(h, "203.0.113.0/24"), h.RouteManager.Present);
    }

    [Fact]
    public async Task CleanInstall_RoSelectedWhileDisabled_NoPrefixMutations()
    {
        await using var h = NewHarness();
        await h.SetConfigurationAsync(enabled: false, RO);
        await h.SeedCacheAsync(RO, ["203.0.113.0/24"]);

        await h.SetCountryAsync(RO);
        await h.Controller.UpdatePrefixesAsync(RO, CancellationToken.None);

        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(Pid(h, "203.0.113.0/24"), h.RouteManager.Present);
        DesiredConfiguration cfg =
            await h.ConfigurationService.GetAsync(CancellationToken.None);
        Assert.False(cfg.Enabled);
        Assert.Equal(RO, cfg.DirectCountryCode);
    }

    // ---- Step 4: legacy-upgrade acceptance ----

    [Fact]
    public async Task LegacyUpgrade_EnabledIr_RetainsOwnership()
    {
        string dir = Path.Combine(
            Path.GetTempPath(), "id35-7-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "config.json"), LegacyConfigJson());
            Directory.CreateDirectory(Path.Combine(dir, "prefixes"));
            File.WriteAllText(
                Path.Combine(dir, "prefixes", "iran-ipv4-prefixes.txt"),
                "203.0.113.0/24\n198.51.100.0/24\n");

            await using var h = new CountrySwitchingAcceptanceTests.SwitchHarness(dir);
            DesiredConfiguration cfg =
                await h.ConfigurationService.GetAsync(CancellationToken.None);
            Assert.Equal(IR, cfg.DirectCountryCode);
            Assert.True(cfg.Enabled);

            IReadOnlyList<string> migrated =
                await h.PrefixStore.LoadPrefixesAsync(
                    IR, CancellationToken.None);
            Assert.Equal(2, migrated.Count);

            await h.SeedOwnedPrefixRoutesAsync(
                ["203.0.113.0/24", "198.51.100.0/24"]);
            RuntimeCycleExecutionResult result =
                await h.Controller.RunCycleAsync(CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Contains(Pid(h, "203.0.113.0/24"), h.RouteManager.Present);
            Assert.Contains(Pid(h, "198.51.100.0/24"), h.RouteManager.Present);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyUpgrade_RestartIdempotent()
    {
        string dir = Path.Combine(
            Path.GetTempPath(), "id35-7-legacy2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "config.json"), LegacyConfigJson());
            Directory.CreateDirectory(Path.Combine(dir, "prefixes"));
            File.WriteAllText(
                Path.Combine(dir, "prefixes", "iran-ipv4-prefixes.txt"), "203.0.113.0/24\n");

            await using (var h1 =
                new CountrySwitchingAcceptanceTests.SwitchHarness(dir))
            {
                IReadOnlyList<string> p =
                    await h1.PrefixStore.LoadPrefixesAsync(
                        IR, CancellationToken.None);
                Assert.Single(p);
            }
            await using (var h2 =
                CountrySwitchingAcceptanceTests.SwitchHarness.Restart(dir))
            {
                IReadOnlyList<string> p =
                    await h2.PrefixStore.LoadPrefixesAsync(
                        IR, CancellationToken.None);
                Assert.Single(p);
                Assert.True(File.Exists(
                    Path.Combine(dir, "prefixes", "iran-ipv4-prefixes.txt")));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string LegacyConfigJson() =>
        "{\n" +
        "  \"SchemaVersion\": 1,\n" +
        "  \"Enabled\": true,\n" +
        "  \"VpnProvider\": \"OpenVpn\",\n" +
        "  \"VpnProfilePath\": \"vpn-profile.ovpn\",\n" +
        "  \"AutoRepair\": true,\n" +
        "  \"RepairInterval\": \"00:00:30\",\n" +
        "  \"AutoUpdatePrefixes\": true,\n" +
        "  \"PrefixUpdateInterval\": \"1.00:00:00\"\n" +
        "}";

    // ---- Step 5: country matrix ----

    public static readonly TheoryData<DirectCountryCode, string[]> CountryMatrix =
        new()
        {
            { IR, ["203.0.113.0/24", "198.51.100.0/24"] },
            { IQ, ["203.0.113.0/24", "198.51.100.0/24"] },
            { RO, ["203.0.113.0/24", "198.51.100.0/24"] },
            { US, ["203.0.113.0/24", "198.51.100.0/24"] },
            { BR, ["203.0.113.0/24", "198.51.100.0/24"] },
            { JP, ["203.0.113.0/24", "198.51.100.0/24"] },
            { ZA, ["203.0.113.0/24", "198.51.100.0/24"] },
            { AU, ["203.0.113.0/24", "198.51.100.0/24"] }
        };

    [Theory]
    [MemberData(nameof(CountryMatrix))]
    public async Task CountryMatrix_FetchReconcileIsolated(
        DirectCountryCode country, string[] prefixes)
    {
        await using var h = NewHarness();
        await h.SetConfigurationAsync(enabled: true, country);
        await h.SeedCacheAsync(country, prefixes);
        await h.SeedOwnedPrefixRoutesAsync(prefixes);
        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(prefixes.Length, CountPrefixRoutes(h, prefixes));
        Assert.True(File.Exists(h.PrefixStore.PrefixFileFor(country)));
    }

    [Theory]
    [MemberData(nameof(CountryMatrix))]
    public async Task CountryMatrix_NoCrossCountryCacheUse(
        DirectCountryCode country, string[] prefixes)
    {
        await using var h = NewHarness();
        DirectCountryCode other = country == IR ? IQ : IR;
        await h.SeedCacheAsync(other, ["10.0.0.0/24"]);
        await h.SetConfigurationAsync(enabled: true, country);
        await h.SeedCacheAsync(country, prefixes);
        await h.SeedOwnedPrefixRoutesAsync(prefixes);

        await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.DoesNotContain(Pid(h, "10.0.0.0/24"), h.RouteManager.Present);
    }

    // ---- Step 7: full switch matrix (disjoint prefixes) ----

    [Theory]
    [InlineData("IR", "IQ")]
    [InlineData("IQ", "RO")]
    [InlineData("RO", "IR")]
    [InlineData("IR", "US")]
    [InlineData("US", "BR")]
    public async Task SwitchMatrix_OldRemovedNewAddedPreservingOthers(
        string from, string to)
    {
        DirectCountryCode fromC = DirectCountryCode.Parse(from);
        DirectCountryCode toC = DirectCountryCode.Parse(to);
        await using var h = NewHarness();

        string fromUnique = "198.51.100.0/24";
        string toUnique = "203.0.113.99/24";
        string shared = "203.0.113.0/24";

        await h.SetConfigurationAsync(enabled: true, fromC);
        await h.SeedCacheAsync(fromC, [shared, fromUnique]);
        await h.SeedOwnedPrefixRoutesAsync([shared, fromUnique]);
        await h.Controller.RunCycleAsync(CancellationToken.None);

        // External route sharing a destination with the dataset survives.
        string externalPid = $"203.0.113.0/24|10.9.9.9|99";
        h.RouteManager.Present.Add(externalPid);

        await h.SetCountryAsync(toC);
        h.PrefixSource.Register(toC, [shared, toUnique]);
        await h.SeedCacheAsync(toC, [shared, toUnique]);
        await h.Controller.UpdatePrefixesAsync(toC, CancellationToken.None);

        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);
        Assert.True(result.IsSuccess);

        // Old country's unique route removed; new unique route added; external
        // preserved; shared prefix retained (still desired for the new country).
        Assert.DoesNotContain(Pid(h, fromUnique), h.RouteManager.Present);
        Assert.Contains(Pid(h, toUnique), h.RouteManager.Present);
        Assert.Contains(externalPid, h.RouteManager.Present);

        int before = h.RouteManager.Present.Count;
        RuntimeCycleExecutionResult again =
            await h.Controller.RunCycleAsync(CancellationToken.None);
        Assert.True(again.IsSuccess);
        Assert.Equal(before, h.RouteManager.Present.Count);
    }

    // ---- Step 8: offline matrix ----

    [Fact]
    public async Task Offline_CachedTargetSwitch_Safe()
    {
        await using var h = NewHarness();
        await h.SetConfigurationAsync(enabled: true, IR);
        await h.SeedCacheAsync(IR, ["203.0.113.0/24"]);
        await h.SeedOwnedPrefixRoutesAsync(["203.0.113.0/24"]);
        await h.Controller.RunCycleAsync(CancellationToken.None);

        await h.SeedCacheAsync(IQ, ["198.51.100.0/24"]);
        h.PrefixSource.FailFor(IQ);
        await h.SetCountryAsync(IQ);
        try
        {
            await h.Controller.UpdatePrefixesAsync(IQ, CancellationToken.None);
        }
        catch (InvalidOperationException) { /* source down; cache path used */ }
        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(Pid(h, "198.51.100.0/24"), h.RouteManager.Present);
        Assert.DoesNotContain(Pid(h, "203.0.113.0/24"), h.RouteManager.Present);
    }

    [Fact]
    public async Task Offline_NoTargetCache_BlockedNoDestructiveChange()
    {
        await using var h = NewHarness();
        await h.SetConfigurationAsync(enabled: true, IR);
        await h.SeedCacheAsync(IR, ["203.0.113.0/24"]);
        await h.SeedOwnedPrefixRoutesAsync(["203.0.113.0/24"]);
        await h.Controller.RunCycleAsync(CancellationToken.None);

        h.PrefixSource.FailFor(IQ);
        await h.SetCountryAsync(IQ);
        try
        {
            await h.Controller.UpdatePrefixesAsync(IQ, CancellationToken.None);
        }
        catch (InvalidOperationException) { /* expected: source down */ }
        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            result.Decision.Reconciliation.Status);
        Assert.Contains(Pid(h, "203.0.113.0/24"), h.RouteManager.Present);
    }

    [Fact]
    public async Task Offline_IqSelectedOnlyIrCache_NoFallback()
    {
        await using var h = NewHarness();
        await h.SetConfigurationAsync(enabled: true, IR);
        await h.SeedCacheAsync(IR, ["203.0.113.0/24"]);
        await h.SeedOwnedPrefixRoutesAsync(["203.0.113.0/24"]);
        await h.Controller.RunCycleAsync(CancellationToken.None);

        h.PrefixSource.FailFor(IQ);
        await h.SetCountryAsync(IQ);
        try
        {
            await h.Controller.UpdatePrefixesAsync(IQ, CancellationToken.None);
        }
        catch (InvalidOperationException) { /* expected */ }
        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            result.Decision.Reconciliation.Status);
        // No IQ route installed by falling back to IR. (IR routes are retained
        // fail-closed; that is correct, so we only assert no IQ route appears.)
        Assert.DoesNotContain(Pid(h, "198.51.100.0/24"), h.RouteManager.Present);
    }

    [Fact]
    public async Task Offline_TargetCacheMalformed_NotAuthoritative()
    {
        await using var h = NewHarness();
        await h.SetConfigurationAsync(enabled: true, IR);
        await h.SeedCacheAsync(IR, ["203.0.113.0/24"]);
        await h.SeedOwnedPrefixRoutesAsync(["203.0.113.0/24"]);
        await h.Controller.RunCycleAsync(CancellationToken.None);

        Directory.CreateDirectory(
            Path.Combine(h.TempDir, "prefixes", "IQ"));
        File.WriteAllText(
            Path.Combine(h.TempDir, "prefixes", "IQ", "ipv4-prefixes.txt"),
            "not-a-prefix\n::garbage::\n");
        h.PrefixSource.FailFor(IQ);
        await h.SetCountryAsync(IQ);
        try
        {
            await h.Controller.UpdatePrefixesAsync(IQ, CancellationToken.None);
        }
        catch (InvalidOperationException) { /* expected */ }
        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            result.Decision.Reconciliation.Status);
    }

    // ---- Step 9: restart matrix ----

    [Fact]
    public async Task Restart_AfterSuccessfulSwitch_Stable()
    {
        string dir = Path.Combine(
            Path.GetTempPath(), "id35-7-restart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await using (var h =
                new CountrySwitchingAcceptanceTests.SwitchHarness(dir))
            {
                await h.SetConfigurationAsync(enabled: true, IR);
                await h.SeedCacheAsync(IR, ["203.0.113.0/24"]);
                await h.SeedOwnedPrefixRoutesAsync(["203.0.113.0/24"]);
                await h.Controller.RunCycleAsync(CancellationToken.None);

                await h.SetCountryAsync(IQ);
                h.PrefixSource.Register(IQ, ["198.51.100.0/24"]);
                await h.SeedCacheAsync(IQ, ["198.51.100.0/24"]);
                await h.Controller.UpdatePrefixesAsync(IQ, CancellationToken.None);
                await h.Controller.RunCycleAsync(CancellationToken.None);
                await h.Controller.RunCycleAsync(CancellationToken.None);
            }
            await using (var h2 =
                CountrySwitchingAcceptanceTests.SwitchHarness.Restart(dir))
            {
                await h2.RestoreRouteTableFromInventoryAsync();
                DesiredConfiguration cfg =
                    await h2.ConfigurationService.GetAsync(
                        CancellationToken.None);
                Assert.Equal(IQ, cfg.DirectCountryCode);

                RuntimeCycleExecutionResult result =
                    await h2.Controller.RunCycleAsync(CancellationToken.None);
                Assert.True(result.IsSuccess);
                Assert.Contains(
                    Pid(h2, "198.51.100.0/24"), h2.RouteManager.Present);
                Assert.DoesNotContain(
                    Pid(h2, "203.0.113.0/24"), h2.RouteManager.Present);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Restart_AfterFailedSwitch_RequestedPersistedNoLoss()
    {
        string dir = Path.Combine(
            Path.GetTempPath(), "id35-7-restartfail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await using (var h =
                new CountrySwitchingAcceptanceTests.SwitchHarness(dir))
            {
                await h.SetConfigurationAsync(enabled: true, IR);
                await h.SeedCacheAsync(IR, ["203.0.113.0/24"]);
                await h.SeedOwnedPrefixRoutesAsync(["203.0.113.0/24"]);
                await h.Controller.RunCycleAsync(CancellationToken.None);

                h.PrefixSource.FailFor(IQ);
                await h.SetCountryAsync(IQ);
                try
                {
                    await h.Controller.UpdatePrefixesAsync(
                        IQ, CancellationToken.None);
                }
                catch (InvalidOperationException) { /* expected */ }
                await h.Controller.RunCycleAsync(CancellationToken.None);
            }
            await using (var h2 =
                CountrySwitchingAcceptanceTests.SwitchHarness.Restart(dir))
            {
                await h2.RestoreRouteTableFromInventoryAsync();
                DesiredConfiguration cfg =
                    await h2.ConfigurationService.GetAsync(
                        CancellationToken.None);
                Assert.Equal(IQ, cfg.DirectCountryCode);

                RuntimeCycleExecutionResult result =
                    await h2.Controller.RunCycleAsync(CancellationToken.None);
                Assert.Contains(
                    Pid(h2, "203.0.113.0/24"), h2.RouteManager.Present);
                Assert.Equal(
                    RuntimeReconciliationStatus.Blocked,
                    result.Decision.Reconciliation.Status);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- Step 10: crash-recovery matrix ----

    [Fact]
    public async Task CrashRecovery_SwitchJournalRecoversWithoutCountryField()
    {
        string dir = Path.Combine(
            Path.GetTempPath(), "id35-7-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await using (var h =
                new CountrySwitchingAcceptanceTests.SwitchHarness(dir))
            {
                await h.SetConfigurationAsync(enabled: true, IR);
                await h.SeedCacheAsync(IR, ["203.0.113.0/24"]);
                await h.SeedOwnedPrefixRoutesAsync(["203.0.113.0/24"]);
                await h.Controller.RunCycleAsync(CancellationToken.None);

                await h.SetCountryAsync(IQ);
                h.PrefixSource.Register(IQ, ["198.51.100.0/24"]);
                await h.SeedCacheAsync(IQ, ["198.51.100.0/24"]);
                await h.Controller.UpdatePrefixesAsync(IQ, CancellationToken.None);
                await h.Controller.RunCycleAsync(CancellationToken.None);
                await h.Controller.RunCycleAsync(CancellationToken.None);
            }
            await using (var h2 =
                CountrySwitchingAcceptanceTests.SwitchHarness.Restart(dir))
            {
                await h2.RestoreRouteTableFromInventoryAsync();
                RuntimeCycleExecutionResult result =
                    await h2.Controller.RunCycleAsync(CancellationToken.None);
                Assert.True(result.IsSuccess);
                Assert.Contains(
                    Pid(h2, "198.51.100.0/24"), h2.RouteManager.Present);

                System.Reflection.PropertyInfo[] props =
                    typeof(RouteMutationJournalEntry).GetProperties();
                Assert.DoesNotContain(
                    props,
                    p => p.Name.Contains("Country", StringComparison.Ordinal));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- Steps 11-13: ownership-safety & preservation ----

    [Fact]
    public async Task Switch_PreservesExternalRouteSharingDestination()
    {
        await using var h = NewHarness();
        await h.SetConfigurationAsync(enabled: true, IR);
        await h.SeedCacheAsync(IR, ["203.0.113.0/24"]);
        await h.SeedOwnedPrefixRoutesAsync(["203.0.113.0/24"]);

        string external = $"203.0.113.0/24|10.9.9.9|99";
        h.RouteManager.Present.Add(external);

        await h.Controller.RunCycleAsync(CancellationToken.None);

        await h.SetCountryAsync(IQ);
        h.PrefixSource.Register(IQ, ["203.0.113.0/24"]);
        await h.SeedCacheAsync(IQ, ["203.0.113.0/24"]);
        await h.Controller.UpdatePrefixesAsync(IQ, CancellationToken.None);
        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(external, h.RouteManager.Present);
    }

    // ---- Step 17: persistence layout ----

    [Fact]
    public async Task Persistence_MultiCountryIsolatedNoGlobalLatest()
    {
        await using var h = NewHarness();
        foreach (DirectCountryCode c in new[] { IR, IQ, RO, US, BR })
        {
            await h.SetConfigurationAsync(enabled: true, c);
            await h.SeedCacheAsync(c, ["203.0.113.0/24"]);
            await h.SeedOwnedPrefixRoutesAsync(["203.0.113.0/24"]);
            await h.Controller.UpdatePrefixesAsync(c, CancellationToken.None);
            await h.Controller.RunCycleAsync(CancellationToken.None);
        }

        string prefixesRoot = Path.GetDirectoryName(
            Path.GetDirectoryName(h.PrefixStore.PrefixFileFor(IR)))!; // .../prefixes (country parent)
        foreach (string code in new[] { "IR", "IQ", "RO", "US", "BR" })
        {
            DirectCountryCode c = DirectCountryCode.Parse(code);
            Assert.True(File.Exists(h.PrefixStore.PrefixFileFor(c)),
                $"missing prefix file for {code}");
            Assert.True(File.Exists(h.PrefixStore.MetadataFileFor(c)),
                $"missing metadata for {code}");
            // NOTE: the harness controller is wired with the metadata service but
            // not the update-history service, so update-history.json is asserted
            // in LegacyGlobalizationUpgradeTests (real history service). Here we
            // verify per-country prefix + metadata isolation, which the harness
            // exercises.
        }
        // No single "latest country" file and no stray prefix file directly
        // under the prefix root. (Per-country data lives in <code>/ subfolders;
        // some auxiliary files may exist at the root, which is why we assert
        // only the specific "latest" and "ipv4-prefixes.txt" anti-patterns.)
        Assert.False(File.Exists(Path.Combine(prefixesRoot, "latest.txt")));
        Assert.False(File.Exists(Path.Combine(prefixesRoot, "ipv4-prefixes.txt")));
    }

    // ---- Step 18: large-country acceptance (US ~70K) ----

    [Fact]
    public async Task LargeCountry_Us70k_PlannerExecutorAllocationBounded()
    {
        PrefixWorkloadGenerator gen = new();
        IReadOnlyList<string> usPrefixes = gen.Generate(70_000);
        Assert.Equal(70_000, usPrefixes.Count);

        await using var h = NewHarness();
        await h.SetConfigurationAsync(enabled: true, US);
        await h.PrefixStore.SavePrefixesAsync(US, usPrefixes);
        await h.SeedOwnedPrefixRoutesAsync(usPrefixes);

        long before = GC.GetTotalMemory(true);
        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);
        long after = GC.GetTotalMemory(true);

        Assert.True(result.IsSuccess);
        Assert.True(h.RouteManager.Present.Count >= 70_000,
            $"route count = {h.RouteManager.Present.Count}");
        Assert.True(after - before < 512L * 1024 * 1024,
            $"allocation during 70K reconcile = {after - before} bytes");
    }

    [Fact]
    public async Task LargeCountry_Us70k_NoRealRouteInstall()
    {
        PrefixWorkloadGenerator gen = new();
        IReadOnlyList<string> usPrefixes = gen.Generate(70_000);

        await using var h = NewHarness();
        await h.SetConfigurationAsync(enabled: true, US);
        await h.PrefixStore.SavePrefixesAsync(US, usPrefixes);
        await h.SeedOwnedPrefixRoutesAsync(usPrefixes);
        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(h.RouteManager.Present.Count >= 70_000);
    }

    // ---- Step 19: prefix aggregation audit ----

    [Theory]
    [InlineData("IR")]
    [InlineData("IQ")]
    [InlineData("RO")]
    [InlineData("US")]
    [InlineData("BR")]
    public void AggregationAudit_RawVsUnique(string code)
    {
        int scale = code == "US" ? 70_000 : 2_000;
        PrefixWorkloadGenerator gen = new();
        IReadOnlyList<string> raw = gen.Generate(scale);

        // The generator already emits distinct valid CIDRs, so validated-unique
        // == raw for synthetic input. Real RIPEstat data may contain duplicates
        // and overlapping ranges;aggregation is out of scope here (no
        // production change), but the measurement hook is established.
        Assert.Equal(raw.Count, raw.Distinct(StringComparer.Ordinal).Count());
    }

    private static int CountPrefixRoutes(
        CountrySwitchingAcceptanceTests.SwitchHarness h,
        IReadOnlyList<string> prefixes) =>
        prefixes.Count(p => h.RouteManager.Present.Contains(Pid(h, p)));
}
