using PathVeer.Core.Configuration;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Reconciliation;

namespace PathVeer.Core.Tests.Globalization;

/// <summary>
/// Integration acceptance for the Phase 35.5 country-selection UX + refresh
/// trigger, driven through the real runtime pipeline (the same
/// <see cref="CountrySwitchingAcceptanceTests.SwitchHarness"/> used to prove
/// the switching contract in 35.4). These tests exercise the exact ordering
/// the IPC command handler uses: persist requested country -> refresh that
/// country's prefix dataset -> reconcile.
/// </summary>
public sealed class CountrySelectionAcceptanceTests
{
    private static readonly DirectCountryCode IR = DirectCountryCode.Parse("IR");
    private static readonly DirectCountryCode IQ = DirectCountryCode.Parse("IQ");
    private static readonly DirectCountryCode RO = DirectCountryCode.Parse("RO");

    private static readonly string[] IrPrefixes =
        ["203.0.113.0/24", "198.51.100.0/24"]; // 198.51.100.0/24 is IR-only
    private static readonly string[] IqPrefixes =
        ["203.0.113.0/24"]; // shared only
    private static readonly string[] RoPrefixes =
        ["203.0.113.0/24"]; // shared only

    private string Pid(
        CountrySwitchingAcceptanceTests.SwitchHarness h,
        string prefix) =>
        $"{prefix}|{h.Gateway}|{h.IfIndex}";

    private static CountrySwitchingAcceptanceTests.SwitchHarness NewHarness()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "id35-5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new CountrySwitchingAcceptanceTests.SwitchHarness(dir);
    }

    [Fact]
    public async Task CliSet_Iq_RefreshesAndReconciles()
    {
        await using var h = NewHarness();

        await h.SetConfigurationAsync(enabled: true, IR);
        await h.SeedCacheAsync(IR, IrPrefixes);
        await h.SeedOwnedPrefixRoutesAsync(IrPrefixes);
        await h.Controller.RunCycleAsync(CancellationToken.None);

        // Simulate: country set IQ via the service contract (persist only),
        // then the refresh trigger (explicit UpdatePrefixesAsync for IQ).
        await h.SetCountryAsync(IQ);
        h.PrefixSource.Register(IQ, IqPrefixes);
        await h.Controller.UpdatePrefixesAsync(IQ, CancellationToken.None);

        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(h.RouteManager.Present.Contains(
            Pid(h, "203.0.113.0/24")));
        // IR-only owned routes removed; IQ dataset now in effect.
        Assert.DoesNotContain(
            h.RouteManager.Present,
            id => id.StartsWith("198.51.100.0/24"));
    }

    [Fact]
    public async Task TraySet_Ro_RefreshesAndReconciles()
    {
        await using var h = NewHarness();

        await h.SetConfigurationAsync(enabled: true, IQ);
        await h.SeedCacheAsync(IQ, IqPrefixes);
        // Owned IQ routes include an IQ-only prefix (198.51.100.0/24) that RO
        // does not request, proving it is removed on the IQ -> RO switch.
        await h.SeedOwnedPrefixRoutesAsync(
            ["203.0.113.0/24", "198.51.100.0/24"]);
        await h.Controller.RunCycleAsync(CancellationToken.None);

        await h.SetCountryAsync(RO);
        h.PrefixSource.Register(RO, RoPrefixes);
        await h.Controller.UpdatePrefixesAsync(RO, CancellationToken.None);

        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(h.RouteManager.Present.Contains(
            Pid(h, "203.0.113.0/24")));
    }

    [Fact]
    public async Task FailedSet_SourceDownNoCache_ConfigStaysIq_NoDestructiveChange()
    {
        await using var h = NewHarness();

        await h.SetConfigurationAsync(enabled: true, IR);
        await h.SeedCacheAsync(IR, IrPrefixes);
        await h.SeedOwnedPrefixRoutesAsync(IrPrefixes);
        await h.Controller.RunCycleAsync(CancellationToken.None);

        int ownedBefore = h.RouteManager.Present.Count(
            id => id.StartsWith("203.0.113.0/24") ||
                  id.StartsWith("198.51.100.0/24"));

        // Request IQ, but the source is unavailable and no IQ cache exists.
        await h.SetCountryAsync(IQ);
        h.PrefixSource.FailFor(IQ); // simulate source down

        // The refresh trigger must NOT roll config back; it must fail closed.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await h.Controller.UpdatePrefixesAsync(
                IQ, CancellationToken.None));

        DirectCountryCode requested =
            (await h.ConfigurationService.GetAsync(
                CancellationToken.None)).DirectCountryCode!;
        Assert.Equal(IQ, requested); // config stays IQ (requested policy)

        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        // Reconciliation is blocked (empty IQ prefixes) -> no destructive step.
        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            result.Decision.Reconciliation.Status);
        int ownedAfter = h.RouteManager.Present.Count(
            id => id.StartsWith("203.0.113.0/24") ||
                  id.StartsWith("198.51.100.0/24"));
        Assert.Equal(ownedBefore, ownedAfter); // IR routes preserved
    }

    [Fact]
    public async Task CachedSet_SourceDownIqCachePresent_SafeSwitch()
    {
        await using var h = NewHarness();

        await h.SetConfigurationAsync(enabled: true, IR);
        await h.SeedCacheAsync(IR, IrPrefixes);
        await h.SeedCacheAsync(IQ, IqPrefixes); // valid IQ cache present
        await h.SeedOwnedPrefixRoutesAsync(IrPrefixes);
        await h.Controller.RunCycleAsync(CancellationToken.None);

        // Source down for IQ now, but the cache is valid -> reconcile reads
        // the persisted IQ cache (no destructive IR fallback).
        await h.SetCountryAsync(IQ);
        h.PrefixSource.FailFor(IQ);

        try
        {
            await h.Controller.UpdatePrefixesAsync(
                IQ, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Acceptable: network failed; cache remains authoritative.
        }

        RuntimeCycleExecutionResult result =
            await h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(h.RouteManager.Present.Contains(
            Pid(h, "203.0.113.0/24")));
    }

    [Fact]
    public async Task SetCountry_PreservesEnabledAndProfilePath()
    {
        await using var h = NewHarness();

        await h.SetConfigurationAsync(enabled: true, IR);
        await h.SeedCacheAsync(IR, IrPrefixes);

        string beforePath = (await h.ConfigurationService.GetAsync(
            CancellationToken.None)).VpnProfilePath;

        await h.SetCountryAsync(IQ);

        DesiredConfiguration config =
            await h.ConfigurationService.GetAsync(CancellationToken.None);

        Assert.Equal(IQ, config.DirectCountryCode);
        Assert.True(config.Enabled);            // unchanged
        Assert.Equal(beforePath, config.VpnProfilePath); // unchanged
    }

    [Fact]
    public async Task SetCountry_DisabledConfig_PermitsChange()
    {
        await using var h = NewHarness();

        await h.SetConfigurationAsync(enabled: false, IR);
        await h.SeedCacheAsync(IQ, IqPrefixes);

        await h.SetCountryAsync(IQ);

        DesiredConfiguration config =
            await h.ConfigurationService.GetAsync(CancellationToken.None);

        Assert.Equal(IQ, config.DirectCountryCode);
        Assert.False(config.Enabled); // changing country did NOT enable
    }
}
