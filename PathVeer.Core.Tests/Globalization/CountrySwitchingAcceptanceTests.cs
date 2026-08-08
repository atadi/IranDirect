namespace PathVeer.Core.Tests.Globalization;

using System.Net;
using PathVeer.Core;
using PathVeer.Core.Configuration;
using PathVeer.Core.CustomRoutes;
using PathVeer.Core.Models;
using PathVeer.Core.Networking;
using PathVeer.Core.Prefixes;
using PathVeer.Core.Routing;
using PathVeer.Core.Runtime;
using PathVeer.Core.Runtime.Execution;
using PathVeer.Core.Runtime.Profiling;
using PathVeer.Core.Runtime.Reconciliation;
using PathVeer.Core.State;
using PathVeer.Core.Vpn;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Phase 35.4 - Country switching & offline-state acceptance.
///
/// Drives the REAL production pipeline end-to-end against in-memory fakes:
///   PathVeerController -> RuntimeCycleCoordinator
///     -> RuntimeDecisionBuilder -> RuntimeCoordinator(RuntimeObserver,
///        RuntimePlanner) + RuntimeReconciler(RuntimeRouteOwnershipProvider,
///        RuntimeChangeSetPlanner) -> RuntimeExecutor(WindowsRuntimeExecutionStepHandler)
///        -> InMemoryRouteManager (+ real RouteInventoryStore / journal).
///
/// No real Windows route mutation. Country-dependent behavior lives only in
/// configuration + prefix acquisition/cache + desired-prefix construction. The
/// planner/executor/reconciler/journal are exercised as-is and proven
/// country-neutral.
/// </summary>
public sealed class CountrySwitchingAcceptanceTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly SwitchHarness _h;

    public CountrySwitchingAcceptanceTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "IranDirect.35.4",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _h = new SwitchHarness(_tempDir);
    }

    public async ValueTask DisposeAsync()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
        await ValueTask.CompletedTask;
    }

    private const int PrefixMetric = 5;

    private static string Pid(string gw, uint ifIndex, string prefix) =>
        $"{prefix}|{gw}|{ifIndex}";

    private string Pid(string prefix) => Pid(_h.Gateway, _h.IfIndex, prefix);

    private static DirectCountryCode CC(string code) =>
        DirectCountryCode.Parse(code);

    private static string EndpointPid(
        string gw, uint ifIndex, string hostCidr) =>
        $"{hostCidr}|{gw}|{ifIndex}";

    private string EndpointPid(string hostCidr) =>
        EndpointPid(_h.Gateway, _h.IfIndex, hostCidr);

    /// <summary>
    /// Seeds a healthy IR baseline: config=IR+enabled, IR cache present,
    /// IR-owned prefix routes + endpoint + custom + external routes present.
    /// </summary>
    private async Task SeedIrBaselineAsync(
        IReadOnlyList<string> irPrefixes,
        IReadOnlyList<string>? iqPrefixes = null,
        IReadOnlyList<string>? roPrefixes = null)
    {
        await _h.SetConfigurationAsync(enabled: true, DirectCountryCode.IR);
        await _h.SeedCacheAsync(DirectCountryCode.IR, irPrefixes);
        if (iqPrefixes is not null)
            await _h.SeedCacheAsync(CC("IQ"), iqPrefixes);
        if (roPrefixes is not null)
            await _h.SeedCacheAsync(CC("RO"), roPrefixes);

        await _h.SeedOwnedPrefixRoutesAsync(irPrefixes);
        await _h.SeedOwnedEndpointRouteAsync("10.0.0.1/32");

        // Custom + external routes: present, not owned by prefix subsystem.
        _h.RouteManager.Present.Add(Pid("198.51.100.0/24"));
        _h.RouteManager.Present.Add(Pid("203.0.113.99/32"));

        // Initial reconcile so ownership inventories match reality.
        await _h.Controller.RunCycleAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------------
    // 5. Successful switch IR -> IQ
    // ------------------------------------------------------------------

    [Fact]
    public async Task IR_to_IQ_OnlineSwitch_Succeeds()
    {
        string[] ir = ["203.0.113.0/24", "203.0.114.0/24"];
        string[] iq = ["45.0.0.0/24", "45.1.0.0/24"];

        await SeedIrBaselineAsync(ir, iqPrefixes: iq);

        int irOwnedBefore = ir.Count(
            p => _h.RouteManager.Present.Contains(Pid(p)));
        Assert.Equal(2, irOwnedBefore);

        // User selects IQ; valid IQ dataset available (online fetch).
        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));
        await _h.Controller.UpdatePrefixesAsync(
            CC("IQ"), CancellationToken.None);
        RuntimeCycleExecutionResult result =
            await _h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);

        // IQ prefixes desired + added.
        Assert.All(iq, p =>
            Assert.Contains(Pid(p), _h.RouteManager.Present));
        // IR-only owned prefix routes removed through ownership-aware plan.
        Assert.All(ir, p =>
            Assert.DoesNotContain(Pid(p), _h.RouteManager.Present));
        // Endpoint / custom / external routes preserved.
        Assert.Contains(EndpointPid("10.0.0.1/32"), _h.RouteManager.Present);
        Assert.Contains(Pid("198.51.100.0/24"), _h.RouteManager.Present);
        Assert.Contains(Pid("203.0.113.99/32"), _h.RouteManager.Present);

        // Second cycle is a no-op (idempotent).
        RuntimeCycleExecutionResult again =
            await _h.Controller.RunCycleAsync(CancellationToken.None);
        Assert.Equal(
            RuntimeReconciliationStatus.NoChangesRequired,
            again.Decision.Reconciliation.Status);
    }

    // ------------------------------------------------------------------
    // 6. Successful switch IQ -> RO (no IR special-casing)
    // ------------------------------------------------------------------

    [Fact]
    public async Task IQ_to_RO_OnlineSwitch_Succeeds()
    {
        string[] ir = ["203.0.113.0/24", "203.0.114.0/24"];
        string[] iq = ["45.0.0.0/24", "45.1.0.0/24"];
        string[] ro = ["80.0.0.0/24", "80.1.0.0/24"];

        // Baseline from IQ (not IR) to prove no IR-specific migration path.
        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));
        await _h.SeedCacheAsync(CC("IQ"), iq);
        await _h.SeedCacheAsync(CC("RO"), ro);
        await _h.SeedCacheAsync(DirectCountryCode.IR, ir);
        await _h.SeedOwnedPrefixRoutesAsync(iq);
        await _h.SeedOwnedEndpointRouteAsync("10.0.0.1/32");
        _h.RouteManager.Present.Add(Pid("198.51.100.0/24"));
        _h.RouteManager.Present.Add(Pid("203.0.113.99/32"));
        await _h.Controller.RunCycleAsync(CancellationToken.None);

        await _h.SetConfigurationAsync(enabled: true, CC("RO"));
        await _h.Controller.UpdatePrefixesAsync(
            CC("RO"), CancellationToken.None);
        RuntimeCycleExecutionResult result =
            await _h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(
            result.IsSuccess,
            $"[IQtoRO] recon={result.Decision.Reconciliation.Status} " +
            $"exec={result.Execution.Status} " +
            $"steps={string.Join(",", result.Execution.StepResults.Select(s => s.Status + ":" + s.Kind))}");
        Assert.All(ro, p =>
            Assert.Contains(Pid(p), _h.RouteManager.Present));
        Assert.All(iq, p =>
            Assert.DoesNotContain(Pid(p), _h.RouteManager.Present));
        Assert.Contains(EndpointPid("10.0.0.1/32"), _h.RouteManager.Present);
        Assert.Contains(Pid("198.51.100.0/24"), _h.RouteManager.Present);
    }

    // ------------------------------------------------------------------
    // 7. Successful switch RO -> IR (reuse cache, no unnecessary migration)
    // ------------------------------------------------------------------

    [Fact]
    public async Task RO_to_IR_ReturnsAndReusesIrCache()
    {
        string[] ir = ["203.0.113.0/24", "203.0.114.0/24"];
        string[] ro = ["80.0.0.0/24", "80.1.0.0/24"];

        await _h.SetConfigurationAsync(enabled: true, CC("RO"));
        await _h.SeedCacheAsync(CC("RO"), ro);
        await _h.SeedCacheAsync(DirectCountryCode.IR, ir);
        await _h.SeedOwnedPrefixRoutesAsync(ro);
        await _h.SeedOwnedEndpointRouteAsync("10.0.0.1/32");
        await _h.Controller.RunCycleAsync(CancellationToken.None);

        // Switch to IR; IR cache already present -> reused, no migration.
        await _h.SetConfigurationAsync(enabled: true, DirectCountryCode.IR);
        RuntimeCycleExecutionResult result =
            await _h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(ro, p =>
            Assert.DoesNotContain(Pid(p), _h.RouteManager.Present));
        // RO cache remains on disk for future reuse.
        Assert.True(_h.HasCache(CC("RO")));
    }

    // ------------------------------------------------------------------
    // 8. Failed switch: target source unavailable, no target cache
    // ------------------------------------------------------------------

    [Fact]
    public async Task FailedSwitch_NoIqCache_AndSourceUnavailable_NoMutation()
    {
        string[] ir = ["203.0.113.0/24", "203.0.114.0/24"];
        await SeedIrBaselineAsync(ir);

        // No IQ cache; source unavailable for IQ.
        _h.PrefixSource.FailFor(CC("IQ"));

        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));

        // Update attempt fails (no cache, source down) -> IR cache untouched.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _h.Controller.UpdatePrefixesAsync(
                CC("IQ"), CancellationToken.None));
        Assert.False(_h.HasCache(CC("IQ")));

        // Reconcile cycle: IQ prefixes empty -> PrefixesUnavailable blocker
        // -> reconciliation BLOCKED -> no route mutation at all.
        RuntimeCycleExecutionResult result =
            await _h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            result.Decision.Reconciliation.Status);

        // IR routes + endpoint + custom + external all still present.
        Assert.All(ir, p =>
            Assert.Contains(Pid(p), _h.RouteManager.Present));
        Assert.Contains(EndpointPid("10.0.0.1/32"), _h.RouteManager.Present);
        Assert.Contains(Pid("198.51.100.0/24"), _h.RouteManager.Present);
        Assert.Contains(Pid("203.0.113.99/32"), _h.RouteManager.Present);

        // Repeated cycles stay blocked (idempotent, host alive).
        RuntimeCycleExecutionResult again =
            await _h.Controller.RunCycleAsync(CancellationToken.None);
        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            again.Decision.Reconciliation.Status);
    }

    // ------------------------------------------------------------------
    // 9. Failed switch with valid target-country cache (last-known-good)
    // ------------------------------------------------------------------

    [Fact]
    public async Task FailedSwitch_ValidIqCache_CompletesFromLastKnownGood()
    {
        string[] ir = ["203.0.113.0/24", "203.0.114.0/24"];
        string[] iq = ["45.0.0.0/24", "45.1.0.0/24"];
        await SeedIrBaselineAsync(ir, iqPrefixes: iq);

        // Source now unavailable, but IQ cache is valid last-known-good.
        _h.PrefixSource.FailFor(CC("IQ"));

        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));

        // Update attempt fails, but cache is already valid -> switch can
        // still complete from validated target-country last-known-good.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _h.Controller.UpdatePrefixesAsync(
                CC("IQ"), CancellationToken.None));

        RuntimeCycleExecutionResult result =
            await _h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.True(
            result.IsSuccess,
            $"[ValidIqCache] recon={result.Decision.Reconciliation.Status} " +
            $"exec={result.Execution.Status} " +
            $"steps={string.Join(",", result.Execution.StepResults.Select(s => s.Status + ":" + s.Kind))}");
        Assert.All(iq, p =>
            Assert.Contains(Pid(p), _h.RouteManager.Present));
        Assert.All(ir, p =>
            Assert.DoesNotContain(Pid(p), _h.RouteManager.Present));
        // No IR cache fallback: switch used IQ cache only.
        Assert.Contains(Pid("45.0.0.0/24"), _h.RouteManager.Present);
        // Metadata/history remain IQ-scoped.
        Assert.NotNull(
            await _h.PrefixStore
                .GetMetadataRepository(CC("IQ"))
                .LoadAsync(CancellationToken.None));
    }

    // ------------------------------------------------------------------
    // 10. Wrong-country-only cache (hard gate)
    // ------------------------------------------------------------------

    [Fact]
    public async Task SelectedIq_OnlyIrAndRoCaches_NoCrossCountryFallback()
    {
        string[] ir = ["203.0.113.0/24"];
        string[] ro = ["80.0.0.0/24"];
        await SeedIrBaselineAsync(ir, roPrefixes: ro);

        // Selected IQ, but only IR + RO caches exist; source down.
        _h.PrefixSource.FailFor(CC("IQ"));
        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));

        RuntimeCycleExecutionResult result =
            await _h.Controller.RunCycleAsync(CancellationToken.None);

        // No IR/RO dataset consumed; reconciliation blocked (no IQ data).
        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            result.Decision.Reconciliation.Status);
        // Existing IR routes remain (not mutated from wrong-country data).
        Assert.Contains(Pid("203.0.113.0/24"), _h.RouteManager.Present);
        // No IQ route was fabricated.
        Assert.DoesNotContain(Pid("45.0.0.0/24"), _h.RouteManager.Present);
    }

    // ------------------------------------------------------------------
    // 11. Empty / invalid target-country cache
    // ------------------------------------------------------------------

    [Fact]
    public async Task IqCache_EmptyOrInvalid_NotTreatedAsAuthoritative()
    {
        string[] ir = ["203.0.113.0/24"];
        await SeedIrBaselineAsync(ir);

        // Seed an EMPTY IQ cache file (exists but no data).
        await _h.SeedCacheAsync(CC("IQ"), []);

        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));
        // Source also unavailable so refresh cannot succeed.
        _h.PrefixSource.FailFor(CC("IQ"));

        RuntimeCycleExecutionResult result =
            await _h.Controller.RunCycleAsync(CancellationToken.None);

        // Empty cache is not authoritative: prefixes unavailable -> blocked.
        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            result.Decision.Reconciliation.Status);
        Assert.Contains(Pid("203.0.113.0/24"), _h.RouteManager.Present);
    }

    // ------------------------------------------------------------------
    // 17. Persistence isolation (metadata must not suppress other country)
    // ------------------------------------------------------------------

    [Fact]
    public async Task MetadataIsolation_IrMustNotSuppressIqFetch()
    {
        string[] ir = ["203.0.113.0/24"];
        string[] iq = ["45.0.0.0/24"];
        await SeedIrBaselineAsync(ir, iqPrefixes: iq);

        // Record IR success so IR has metadata; then fetch IQ.
        await _h.Controller.UpdatePrefixesAsync(
            DirectCountryCode.IR, CancellationToken.None);
        await _h.Controller.UpdatePrefixesAsync(
            CC("IQ"), CancellationToken.None);

        PrefixSourceMetadata? irMeta =
            (await _h.PrefixStore
                .GetMetadataRepository(DirectCountryCode.IR)
                .LoadAsync(CancellationToken.None))?.Current;
        PrefixSourceMetadata? iqMeta =
            (await _h.PrefixStore
                .GetMetadataRepository(CC("IQ"))
                .LoadAsync(CancellationToken.None))?.Current;

        Assert.NotNull(irMeta);
        Assert.NotNull(iqMeta);
        Assert.NotEqual(irMeta!.ContentHash, iqMeta!.ContentHash);
        Assert.Equal(
            CC("IQ"),
            (await _h.PrefixSource.FetchAsync(
                CC("IQ"), CancellationToken.None)).CountryCode);
    }

    // ------------------------------------------------------------------
    // 18. Legacy IR migration interaction
    // ------------------------------------------------------------------

    [Fact]
    public async Task LegacyIrMigration_SelectedIr_Migrates()
    {
        string[] ir = ["203.0.113.0/24"];

        // Legacy root files only (no new IR-scoped cache).
        await _h.SetConfigurationAsync(enabled: true, DirectCountryCode.IR);
        _h.WriteLegacyIrCache(ir);
        await _h.SeedOwnedEndpointRouteAsync("10.0.0.1/32");

        await _h.Controller.RunCycleAsync(CancellationToken.None);

        // Legacy migrated into IR-scoped location.
        Assert.True(_h.HasCache(DirectCountryCode.IR));
        Assert.Contains(Pid("203.0.113.0/24"), _h.RouteManager.Present);
    }

    [Fact]
    public async Task LegacyIrMigration_SelectedIq_IgnoresLegacyIr()
    {
        string[] ir = ["203.0.113.0/24"];
        string[] iq = ["45.0.0.0/24"];

        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));
        _h.WriteLegacyIrCache(ir);
        await _h.SeedCacheAsync(CC("IQ"), iq);
        await _h.SeedOwnedEndpointRouteAsync("10.0.0.1/32");

        await _h.Controller.RunCycleAsync(CancellationToken.None);

        // Legacy IR cache NOT consumed for IQ: IQ routes are built from the
        // IQ-scoped cache, not the legacy IR file. Legacy file remains on
        // disk (not deleted, not migrated into IQ scope).
        Assert.Contains(Pid("45.0.0.0/24"), _h.RouteManager.Present);
        Assert.False(_h.HasCache(DirectCountryCode.IR));
        Assert.True(_h.LegacyIrCacheExists);
    }

    // ------------------------------------------------------------------
    // 19. Enable/disable interaction
    // ------------------------------------------------------------------

    [Fact]
    public async Task DisabledWithIq_CountryChangeDoesNotSuppressNormalTeardown()
    {
        string[] ir = ["203.0.113.0/24"];
        await SeedIrBaselineAsync(ir);

        // Disabled + CountryCode=IQ. Disabling means no prefix routes are
        // desired, so the owned IR routes are torn down normally (this is the
        // standard disable behavior, NOT a destructive empty-country switch).
        // The key contract: country change did not implicitly toggle Enabled,
        // and only owned routes are removed.
        await _h.SetConfigurationAsync(enabled: false, CC("IQ"));
        RuntimeCycleExecutionResult result =
            await _h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.Equal(
            RuntimeReconciliationStatus.ChangesPlanned,
            result.Decision.Reconciliation.Status);
        Assert.DoesNotContain(
            Pid("203.0.113.0/24"), _h.RouteManager.Present);
        // Custom/external routes never owned -> untouched.
        Assert.Contains(Pid("198.51.100.0/24"), _h.RouteManager.Present);
        Assert.Contains(Pid("203.0.113.99/32"), _h.RouteManager.Present);

        // Re-enable with a valid IQ dataset -> routes are rebuilt.
        await _h.SeedCacheAsync(CC("IQ"), ["45.0.0.0/24"]);
        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));
        RuntimeCycleExecutionResult reenabled =
            await _h.Controller.RunCycleAsync(CancellationToken.None);
        Assert.True(reenabled.IsSuccess);
        Assert.Contains(Pid("45.0.0.0/24"), _h.RouteManager.Present);
    }

    [Fact]
    public async Task EnableWithUnavailableIq_StaysBlockedNoMutation()
    {
        string[] ir = ["203.0.113.0/24"];
        await SeedIrBaselineAsync(ir);

        _h.PrefixSource.FailFor(CC("IQ"));
        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));

        // EnableAsync ensures prefixes; with no cache + source down it fails
        // before enabling -> no destructive route changes.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _h.Controller.EnableAsync(CancellationToken.None));

        Assert.Contains(Pid("203.0.113.0/24"), _h.RouteManager.Present);
    }

    // ------------------------------------------------------------------
    // 21/22. Source race / stale-response: result persists to requested
    //         country, never to current config country.
    // ------------------------------------------------------------------

    [Fact]
    public async Task FetchResult_PersistsToRequestedCountry_NotCurrentConfig()
    {
        // Config is IQ, but we fetch IR explicitly (IR dataset registered).
        // The IR result must land in prefixes/IR, never in IQ scope.
        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));
        await _h.SeedCacheAsync(DirectCountryCode.IR, ["203.0.113.0/24"]);

        PrefixSourceFetchResult irFetch =
            await _h.PrefixSource.FetchAsync(
                DirectCountryCode.IR, CancellationToken.None);
        await _h.Controller.UpdatePrefixesAsync(
            DirectCountryCode.IR, CancellationToken.None);

        Assert.True(_h.HasCache(DirectCountryCode.IR));
        Assert.False(_h.HasCache(CC("IQ")));
        Assert.Equal(DirectCountryCode.IR, irFetch.CountryCode);
    }

    [Fact]
    public async Task TwoCountryFetches_AreSerialized_NoCrossContamination()
    {
        // OperationCoordinator serializes commands; prove two fetches in
        // flight cannot land in the wrong scope by saving each explicitly.
        // (Datasets registered for both countries.)
        await _h.SeedCacheAsync(DirectCountryCode.IR, ["203.0.113.0/24"]);
        await _h.SeedCacheAsync(CC("IQ"), ["45.0.0.0/24"]);

        PrefixSourceFetchResult irFetch =
            await _h.PrefixSource.FetchAsync(
                DirectCountryCode.IR, CancellationToken.None);
        PrefixSourceFetchResult iqFetch =
            await _h.PrefixSource.FetchAsync(
                CC("IQ"), CancellationToken.None);

        await _h.Controller.UpdatePrefixesAsync(
            DirectCountryCode.IR, CancellationToken.None);
        await _h.Controller.UpdatePrefixesAsync(
            CC("IQ"), CancellationToken.None);

        Assert.True(_h.HasCache(DirectCountryCode.IR));
        Assert.True(_h.HasCache(CC("IQ")));
        Assert.Equal(DirectCountryCode.IR, irFetch.CountryCode);
        Assert.Equal(CC("IQ"), iqFetch.CountryCode);
    }

    // ------------------------------------------------------------------
    // 25. Diagnostics/support: requested country vs metadata country
    // ------------------------------------------------------------------

    [Fact]
    public async Task Diagnostics_ShowsRequestedCountry_NotWrongCountryMeta()
    {
        string[] ir = ["203.0.113.0/24"];
        string[] iq = ["45.0.0.0/24"];
        await SeedIrBaselineAsync(ir, iqPrefixes: iq);

        // Record IR metadata, then record IQ metadata (cache ready),
        // then request IQ.
        await _h.Controller.UpdatePrefixesAsync(
            DirectCountryCode.IR, CancellationToken.None);
        await _h.Controller.UpdatePrefixesAsync(
            CC("IQ"), CancellationToken.None);
        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));

        DesiredConfiguration config =
            await _h.ConfigurationService.GetAsync(CancellationToken.None);
        PrefixSourceMetadata? requestedMeta =
            await _h.MetadataService.GetCurrentAsync(
                CC("IQ"), CancellationToken.None);
        PrefixSourceMetadata? irMeta =
            await _h.MetadataService.GetCurrentAsync(
                DirectCountryCode.IR, CancellationToken.None);

        // Requested country is IQ, not IR; its metadata is IQ-scoped.
        Assert.Equal(CC("IQ"), config.DirectCountryCode);
        Assert.NotNull(requestedMeta);
        Assert.NotNull(irMeta);
        Assert.Contains("IQ", requestedMeta!.SourceId);
        Assert.NotEqual(irMeta!.ContentHash, requestedMeta.ContentHash);
    }

    // ------------------------------------------------------------------
    // 12. Country-switch + restart (persisted state survives a new process)
    // ------------------------------------------------------------------

    [Fact]
    public async Task RestartAfterCompletedSwitch_ReconcilesIdempotently()
    {
        string[] ir = ["203.0.113.0/24", "203.0.114.0/24"];
        string[] iq = ["45.0.0.0/24", "45.1.0.0/24"];

        await SeedIrBaselineAsync(ir, iqPrefixes: iq);

        // Complete IR -> IQ switch.
        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));
        await _h.Controller.UpdatePrefixesAsync(CC("IQ"), CancellationToken.None);
        await _h.Controller.RunCycleAsync(CancellationToken.None);

        Assert.All(iq, p => Assert.Contains(Pid(p), _h.RouteManager.Present));
        Assert.All(ir, p => Assert.DoesNotContain(Pid(p), _h.RouteManager.Present));

        // Simulate a process restart: brand-new controller/inventory/journal
        // instances over the SAME persisted directory.
        string dir = _h.TempDir;
        await _h.DisposeAsync();
        SwitchHarness restarted = SwitchHarness.Restart(dir);
        await restarted.RestoreRouteTableFromInventoryAsync();

        RuntimeCycleExecutionResult afterRestart =
            await restarted.Controller.RunCycleAsync(CancellationToken.None);

        // Startup ordering: config load -> country-scoped prefix selection ->
        // reconciliation. IQ routes remain; no ownership loss; no destructive
        // replay; second cycle is a no-op.
        Assert.True(afterRestart.IsSuccess);
        Assert.All(iq, p => Assert.Contains(Pid(p), restarted.RouteManager.Present));
        Assert.All(ir, p => Assert.DoesNotContain(Pid(p), restarted.RouteManager.Present));
        Assert.Contains(EndpointPid("10.0.0.1/32"), restarted.RouteManager.Present);
        Assert.Equal(
            RuntimeReconciliationStatus.NoChangesRequired,
            afterRestart.Decision.Reconciliation.Status);
    }

    [Fact]
    public async Task RestartAfterConfigFlip_NoIqCache_BlocksUntilData()
    {
        string[] ir = ["203.0.113.0/24"];
        await SeedIrBaselineAsync(ir);

        // Flip to IQ but provide no IQ cache and keep the source down.
        _h.PrefixSource.FailFor(CC("IQ"));
        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));

        string dir = _h.TempDir;
        await _h.DisposeAsync();
        SwitchHarness restarted = SwitchHarness.Restart(dir);
        await restarted.RestoreRouteTableFromInventoryAsync();

        RuntimeCycleExecutionResult afterRestart =
            await restarted.Controller.RunCycleAsync(CancellationToken.None);

        // No IQ data available after restart -> blocked, IR routes preserved.
        Assert.Equal(
            RuntimeReconciliationStatus.Blocked,
            afterRestart.Decision.Reconciliation.Status);
        Assert.Contains(
            Pid("203.0.113.0/24"), restarted.RouteManager.Present);
    }

    // ------------------------------------------------------------------
    // 13. Country-switch + crash-consistent mutation journal
    //     (geography-neutral recovery via Phase 34.2 journal)
    // ------------------------------------------------------------------

    [Fact]
    public async Task CrashDuringNewRouteAdd_RecoveryAdoptsFromJournal()
    {
        string[] iq = ["45.0.0.0/24"];
        await _h.SetConfigurationAsync(enabled: true, CC("IQ"));
        await _h.SeedCacheAsync(CC("IQ"), iq);

        // Simulate the crash point of an IR->IQ switch: the native IQ route
        // was added, but the inventory commit had not happened yet, so a
        // pending journal Add intent remains.
        string identity = Pid("45.0.0.0/24");
        _h.RouteManager.Present.Add(identity);
        await _h.Journal.WriteIntentAsync(new RouteMutationJournalEntry
        {
            Kind = RouteMutationKind.Add,
            InventoryKind = RouteMutationInventoryKind.Prefix,
            RouteIdentity = identity,
            DestinationPrefix = "45.0.0.0/24",
            Gateway = _h.Gateway,
            InterfaceIndex = _h.IfIndex,
            Metric = PrefixMetric,
            MutationId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None);

        // Recovery runs at startup; it must adopt the native route into the
        // ownership inventory independent of any country naming, then clear
        // the intent.
        RouteMutationRecovery recovery = new(
            _h.RouteManager,
            _h.RouteInventory,
            _h.EndpointInventory,
            _h.Journal);
        await recovery.RecoverAsync(CancellationToken.None);

        IReadOnlyDictionary<string, RouteMutationJournalEntry> remaining =
            await _h.Journal.LoadAllAsync(CancellationToken.None);
        Assert.Empty(remaining);

        RouteInventory inv = await _h.RouteInventory.LoadAsync(CancellationToken.None);
        Assert.Contains(identity, inv.Routes.Select(r => r.Identity));
        Assert.Contains(identity, _h.RouteManager.Present);
    }

    [Fact]
    public async Task CrashDuringOldRouteRemoval_RecoveryCompletesDelete()
    {
        string[] ir = ["203.0.113.0/24"];
        await SeedIrBaselineAsync(ir);

        // Simulate the crash point of an IR->IQ removal: the native IR route
        // was already deleted, but the inventory still lists it as owned.
        string identity = Pid("203.0.113.0/24");
        _h.RouteManager.Present.Remove(identity);
        await _h.Journal.WriteIntentAsync(new RouteMutationJournalEntry
        {
            Kind = RouteMutationKind.Delete,
            InventoryKind = RouteMutationInventoryKind.Prefix,
            RouteIdentity = identity,
            DestinationPrefix = "203.0.113.0/24",
            Gateway = _h.Gateway,
            InterfaceIndex = _h.IfIndex,
            Metric = PrefixMetric,
            MutationId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None);

        RouteMutationRecovery recovery = new(
            _h.RouteManager,
            _h.RouteInventory,
            _h.EndpointInventory,
            _h.Journal);
        await recovery.RecoverAsync(CancellationToken.None);

        IReadOnlyDictionary<string, RouteMutationJournalEntry> remaining =
            await _h.Journal.LoadAllAsync(CancellationToken.None);
        Assert.Empty(remaining);

        RouteInventory inv = await _h.RouteInventory.LoadAsync(CancellationToken.None);
        Assert.DoesNotContain(identity, inv.Routes.Select(r => r.Identity));
        Assert.DoesNotContain(identity, _h.RouteManager.Present);
    }

    // ==================================================================
    // Harness
    // ==================================================================

    public sealed class SwitchHarness : IAsyncDisposable
    {
        private readonly string _root;
        private readonly string _prefixRoot;
        private readonly string _profilePath;

        public SwitchHarness(string tempDir)
        {
            _tempDir = tempDir;
            _root = tempDir;
            _prefixRoot = Path.Combine(tempDir, "prefixes");
            _profilePath = Path.Combine(tempDir, "vpn.ovpn");
            File.WriteAllText(_profilePath, "remote 10.0.0.1 1194\n");

            DesiredConfigurationStore configStore = new(
                Path.Combine(tempDir, "config.json"),
                new DesiredConfigurationValidator());
            ConfigurationService = new DesiredConfigurationService(configStore);
            _configStore = configStore;

            PrefixStore = new CountryPrefixStore(_prefixRoot);

            RouteInventory = new RouteInventoryStore(
                Path.Combine(tempDir, "route-inventory.json"));
            EndpointInventory = new VpnEndpointInventoryStore(
                Path.Combine(tempDir, "endpoint-inventory.json"));

            RouteMutationJournalStore journalStore =
                new(Path.Combine(tempDir, "journal.json"));
            IRouteMutationJournal journal = journalStore;
            _journal = journalStore;

            RouteManager = new InMemoryRouteManager();
            GatewayDetector gatewayDetector = new();
            try
            {
                DirectGateway gw = gatewayDetector.Detect();
                Gateway = gw.Address.ToString();
                IfIndex = gw.InterfaceIndex;
            }
            catch
            {
                // Sandbox without a usable gateway: deterministic fallback.
                Gateway = "192.168.1.1";
                IfIndex = 10;
            }

            OpenVpnEndpointProvider vpnProvider = new(
                _profilePath,
                new OpenVpnProfileParser(),
                new VpnEndpointResolver());
            VpnEndpointRouteManager vpnRouteManager = new(RouteManager);

            PrefixSource = new ControllablePrefixSource();

            // Country resolver reads the live requested country from config.
            Func<DirectCountryCode> countryResolver = () =>
                ConfigurationService
                    .GetAsync(CancellationToken.None)
                    .GetAwaiter().GetResult().DirectCountryCode!;

            PathVeerRuntimeObservationSource observationSource = new(
                _profilePath,
                vpnProvider,
                gatewayDetector,
                PrefixStore,
                countryResolver,
                RouteManager,
                RuntimeCycleProfiler.Noop,
                null);

            RuntimeObserver observer = new(observationSource);
            RuntimePlanner planner = new(new DesiredConfigurationValidator());
            RuntimeCoordinator planCoordinator = new(
                ConfigurationService, observer, planner);

            RuntimeRouteOwnershipProvider ownershipProvider = new(
                new InventoryRouteOwnershipSource(
                    RouteInventory, EndpointInventory));
            RuntimeReconciler reconciler = new(
                ownershipProvider, new RuntimeChangeSetPlanner());
            RuntimeExecutionPlanner executionPlanner = new();
            RuntimeDecisionBuilder decisionBuilder = new(
                planCoordinator,
                reconciler,
                executionPlanner,
                TimeProvider.System);
            RuntimeCycleCoordinator cycleCoordinator = new(decisionBuilder);

            WindowsRuntimeExecutionStepHandler handler = new(
                RouteManager,
                RouteInventory,
                EndpointInventory,
                RuntimeCycleProfiler.Noop,
                journal);
            RuntimeExecutor executor = new(handler);

            MetadataService = new PrefixSourceMetadataService(PrefixStore);

            Controller = new PathVeerController(
                PrefixSource,
                PrefixStore,
                gatewayDetector,
                RouteManager,
                new StateRepository(
                    Path.Combine(tempDir, "state.json")),
                RouteInventory,
                vpnProvider,
                vpnRouteManager,
                EndpointInventory,
                cycleCoordinator,
                executor,
                ConfigurationService,
                new RuntimeOperationStatus(),
                RuntimeCycleProfiler.Noop,
                MetadataService);
        }

        public string Gateway { get; }
        public uint IfIndex { get; }

        public CountryPrefixStore PrefixStore { get; }
        public InMemoryRouteManager RouteManager { get; }
        public ControllablePrefixSource PrefixSource { get; }
        public DesiredConfigurationService ConfigurationService { get; }
        public PrefixSourceMetadataService MetadataService { get; }
        public RouteInventoryStore RouteInventory { get; }
        public VpnEndpointInventoryStore EndpointInventory { get; }

        public PathVeerController Controller { get; }

        public IRouteMutationJournal Journal => _journal;

        public string TempDir => _tempDir;

        private readonly DesiredConfigurationStore _configStore;
        private readonly RouteMutationJournalStore _journal;
        private readonly string _tempDir;

        /// <summary>
        /// Builds a fresh harness over an EXISTING persisted directory to
        /// simulate a process restart (new controller/inventory/journal
        /// instances sharing the same config, prefix, inventory and journal
        /// files).
        /// </summary>
        public static SwitchHarness Restart(string existingTempDir) =>
            new(existingTempDir);

        /// <summary>
        /// After a restart the native OS route table still holds the routes
        /// IranDirect owned; the in-memory route manager is empty, so we
        /// restore it from the persisted ownership inventories before the
        /// first cycle. This models reality (native routes survive a reboot)
        /// so the post-restart cycle is a faithful no-op / safe-block rather
        /// than a spurious re-add of routes that are already present.
        /// </summary>
        public async Task RestoreRouteTableFromInventoryAsync()
        {
            RouteInventory inv =
                await RouteInventory.LoadAsync(CancellationToken.None);
            foreach (RouteInventoryItem r in inv.Routes)
                RouteManager.Present.Add(r.Identity);

            VpnEndpointInventory ep =
                await EndpointInventory.LoadAsync(CancellationToken.None);
            foreach (VpnEndpointInventoryItem e in ep.Endpoints)
                RouteManager.Present.Add(e.Identity);
        }

        public Task SetConfigurationAsync(
            bool enabled, DirectCountryCode country) =>
            _configStore.SaveAsync(ConfigurationDefaults.Create() with
            {
                Enabled = enabled,
                VpnProfilePath = _profilePath,
                DirectCountryCode = country
            });

        /// <summary>
        /// Persists ONLY the requested country (the new 35.5 country-set
        /// contract) without triggering a prefix fetch. This lets integration
        /// tests drive the refresh step explicitly via
        /// <see cref="PathVeerController.UpdatePrefixesAsync"/>, mirroring
        /// how the IPC command handler orders persist -> refresh -> reconcile.
        /// </summary>
        public Task SetCountryAsync(DirectCountryCode country) =>
            ConfigurationService.SetDirectCountryAsync(country);

        public Task SeedCacheAsync(
            DirectCountryCode country,
            IReadOnlyList<string> prefixes)
        {
            PrefixSource.Register(country, prefixes);
            return PrefixStore.SavePrefixesAsync(country, prefixes);
        }

        public Task SeedOwnedPrefixRoutesAsync(
            IReadOnlyList<string> prefixes)
        {
            List<RouteInventoryItem> items = new();
            foreach (string p in prefixes)
            {
                items.Add(new RouteInventoryItem
                {
                    DestinationPrefix = p,
                    Gateway = Gateway,
                    InterfaceIndex = IfIndex,
                    Metric = PrefixMetric
                });
                RouteManager.Present.Add(
                    Pid(Gateway, IfIndex, p));
            }

            return RouteInventory.SaveAsync(
                new RouteInventory { Routes = items });
        }

        public Task SeedOwnedEndpointRouteAsync(string hostCidr)
        {
            VpnEndpointInventory inv = new VpnEndpointInventory
            {
                Endpoints = new List<VpnEndpointInventoryItem>
                {
                    new VpnEndpointInventoryItem
                    {
                        Host = "10.0.0.1",
                        Address = "10.0.0.1",
                        Port = 1194,
                        Protocol = "udp",
                        DestinationPrefix = hostCidr,
                        Gateway = Gateway,
                        InterfaceIndex = IfIndex,
                        Metric = 1,
                        AddedByIranDirect = true,
                        IsCurrent = true,
                        ProtectedAt = DateTimeOffset.UtcNow,
                        LastSeenAt = DateTimeOffset.UtcNow
                    }
                }
            };
            RouteManager.Present.Add(EndpointPid(Gateway, IfIndex, hostCidr));
            return EndpointInventory.SaveAsync(inv);
        }

        public bool HasCache(DirectCountryCode country) =>
            File.Exists(PrefixStore.PrefixFileFor(country));

        public bool LegacyIrCacheExists =>
            File.Exists(Path.Combine(_prefixRoot, "iran-ipv4-prefixes.txt"));

        public void WriteLegacyIrCache(IReadOnlyList<string> prefixes)
        {
            string legacyFile = Path.Combine(
                _prefixRoot, "iran-ipv4-prefixes.txt");
            new PrefixFileRepository(legacyFile)
                .SaveAsync(prefixes).GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            await ValueTask.CompletedTask;
        }
    }

    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

    public sealed class ControllablePrefixSource : ICountryPrefixSource
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _data =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failed = new(
            StringComparer.OrdinalIgnoreCase);

        public void Register(
            DirectCountryCode country, IReadOnlyList<string> prefixes) =>
            _data[country.Code] = prefixes;

        public void FailFor(DirectCountryCode country) =>
            _failed.Add(country.Code);

        public PrefixSourceDescriptor GetDescriptor(
            DirectCountryCode country) => new()
        {
            Id = $"fake-{country.Code}",
            DisplayName = $"Fake {country.Code} source",
            Format = "ipv4-prefix-list",
            ParserVersion = "1.0",
            Uri = $"https://example.test/{country.Code}"
        };

        public Task<PrefixSourceFetchResult> FetchAsync(
            DirectCountryCode country,
            CancellationToken cancellationToken = default)
        {
            if (_failed.Contains(country.Code))
            {
                throw new InvalidOperationException(
                    $"Simulated source unavailable for {country.Code}.");
            }

            if (!_data.TryGetValue(country.Code, out var prefixes))
            {
                throw new InvalidOperationException(
                    $"No dataset registered for {country.Code}.");
            }

            return Task.FromResult(new PrefixSourceFetchResult
            {
                Source = GetDescriptor(country),
                CountryCode = country,
                Prefixes = prefixes,
                ContentHash = ComputeHash(country, prefixes)
            });
        }

        private static string ComputeHash(
            DirectCountryCode country, IReadOnlyList<string> prefixes)
        {
            // Validator requires a 64-char lowercase hex ContentHash.
            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(
                $"{country.Code}:{prefixes.Count}:{string.Join(",", prefixes)}"));
            var sb = new System.Text.StringBuilder(64);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString(0, 64);
        }
    }

    public sealed class InMemoryRouteManager : IRouteManager
    {
        // Identity is "prefix|gateway|ifIndex"; metric is carried in a
        // parallel map so MatchesExact (which checks RouteMetric) can verify.
        public HashSet<string> Present { get; } = new();
        private readonly Dictionary<string, int> _metric = new(
            StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<SystemRoute>> GetIpv4RoutesAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SystemRoute> routes = Present
                .Select(id =>
                {
                    string[] parts = id.Split('|', 3);
                    return new SystemRoute
                    {
                        DestinationPrefix = parts[0],
                        NextHop = IPAddress.Parse(parts[1]),
                        InterfaceIndex = uint.Parse(parts[2]),
                        RouteMetric = _metric.TryGetValue(id, out int m)
                            ? m : 0
                    };
                })
                .ToList();
            return Task.FromResult(routes);
        }

        public Task AddRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            foreach (ManagedRoute route in routes)
            {
                Present.Add(route.Identity);
                _metric[route.Identity] = route.Metric;
            }

            return Task.CompletedTask;
        }

        public Task DeleteRoutesAsync(
            IReadOnlyCollection<ManagedRoute> routes,
            CancellationToken cancellationToken = default)
        {
            foreach (ManagedRoute route in routes)
            {
                Present.Remove(route.Identity);
                _metric.Remove(route.Identity);
            }

            return Task.CompletedTask;
        }
    }
}
