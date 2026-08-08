# Phase 35.6 — Legacy Upgrade & Compatibility Validation

Status: COMPLETE (validation only — no production code changes required; the
Phase 35.3 migration already satisfies the upgrade contract).
Branch: `development/service-authority`
Base: `e7a9151 feat(globalization): add direct-country selection UX` (Phase 35.5)
Legacy reference: `93e2d87` (parent of `889b74e` / Phase 35.2 — last
pre-country-generalization commit)

## 1. Objective

Prove existing IranDirect installations can upgrade to the country-agnostic
implementation without losing configuration, routing ownership, cached data, or
operational safety — before Phase 35.7 (global acceptance) and Phase 36 (the
IranDirect → PathVeer rename).

## 2. Precondition gate (Step 1)

- branch `development/service-authority` ✓
- Phase 35.5 committed (`e7a9151`) ✓
- clean working tree at start ✓

## 3. Baseline (Step 2)

| Suite | Count | Result |
|---|---|---|
| IranDirect.Core.Tests (Debug) | 2277 | pass, 0 fail |
| IranDirect.Service.Tests (Debug) | 50 | pass, 0 fail |
| Stress (Core) | 12 | pass |
| IranDirect.Benchmarks (Release) | build | clean |

New warnings observed (carried from 35.5, pre-existing, benign): two nullable
analyzer warnings in `DirectCountryCode.cs` (lines 51, 134) — the `Lazy`
`AllSupported` `OrderBy(KnownCodes…)` source and a null-conversion in the
operators. These are static-init-order false positives (the field is
initialized at class load); no functional defect.

## 4. Historical artifact inventory (Steps 3, 5, 6, 22)

Authentically obtained from `git show 93e2d87:…` (legacy reference).

| Artifact | Legacy path (pre-35.2) | New path (35.3+) | Authority | Migration |
|---|---|---|---|---|
| Desired config | `<root>/desired-config.json` | same | authoritative | n/a (field added, default IR) |
| IR prefix cache | `<root>/iran-ipv4-prefixes.txt` | `<root>/prefixes/IR/ipv4-prefixes.txt` | authoritative (cache) | copy-on-read, IR only, idempotent, legacy kept |
| Prefix metadata | `<root>/prefix-source-metadata.json` | `<root>/prefixes/IR/metadata.json` | diagnostic/cache | copy-on-missing, IR only |
| Prefix update history | `<root>/prefix-source-update-history.json` | `<root>/prefixes/IR/update-history.json` | diagnostic | copy-on-missing, IR only |
| Route inventory | `<root>/route-inventory.json` (or store) | same | authoritative (ownership) | none needed (no country field) |
| VPN endpoint inventory | `<root>/vpn-endpoint-inventory.json` | same | authoritative (ownership) | none needed (no country field) |
| Route mutation journal | `<root>/route-mutation-journal.json` | same | authoritative (ownership proof) | none needed (no country field) |
| Custom routes | `<root>/custom-routes.json` (+ DNS cache) | same | authoritative | none needed |
| Runtime state | in-memory + journal | same | derived | n/a |
| VPN profile | `VpnProfilePath` file | same | user-managed | none |

No legacy artifact is deleted on migration. The legacy IR prefix/metadata/history
files are left in place until the new IR-scoped copy is durable (retry-safe).

## 5. DesiredConfiguration compatibility (Step 4)

- `DirectCountryCode` is `DirectCountryCode?` with a record initializer default
  of `IR`. A legacy file (no field) deserializes with the default → `IR`.
- `DirectCountryCodeJsonConverter` treats an absent field as the property
  default (IR); an explicit `null` returns null (degenerate new-JSON case); an
  invalid string fails closed (corrupt config).
- `DesiredConfigurationValidator` does NOT require `DirectCountryCode` (null is
  valid), so legacy configs validate unchanged. `Enabled`, `VpnProfilePath`, and
  every other legacy property are preserved verbatim.
- No rewrite is required merely to read a legacy file (proven: load without
  save leaves the file unchanged, no `DirectCountryCode` injected).
- Missing config continues to follow Phase 34.4: `DesiredConfigurationStore`
  throws `DesiredConfigurationMissingException` from file absence; it is never
  coerced to a disabled config.

## 6. Legacy IR prefix migration (Step 5)

`CountryPrefixStore.LoadPrefixesAsync(country)`:

- If `<prefixes/CC/ipv4-prefixes.txt>` exists → use it.
- Else if `country == IR` and `<root>/iran-ipv4-prefixes.txt>` exists with >0
  lines → copy to the IR scope and return.
- Else → empty.
- A non-IR country NEVER reads the legacy IR file. `HasPrefixes`/`GetPrefixLast
  Modified` follow the same IR-only guard.

Cases verified (permanent tests): only-legacy → migrates; only-new → uses new;
both-identical → no duplication; migration idempotent (2nd startup uses new
file, legacy preserved); legacy-empty → no empty IR scope created; IQ selected →
legacy IR never consumed.

## 7. Metadata / history migration (Step 6)

`MigrateLegacyMetadataIfNeededAsync` / `MigrateLegacyHistoryIfNeededAsync`:
copy the legacy IR file into `prefixes/IR/metadata.json` / `update-history.json`
only when the target is absent and the source exists; IR-only. Non-IR countries
get nothing — proven: IQ metadata/history files are never created from IR
legacy data, so an IR ETag/hash can never suppress or become IQ state. History
remains country-scoped. (No diagnostic/history data is intentionally dropped;
the legacy files are simply copied, not transformed.)

## 8. Inventory / journal / custom-route compatibility (Steps 7, 8, 9, 10)

- `RouteInventoryItem` has no country field (identity = DestinationPrefix /
  Gateway / InterfaceIndex / Metric). Route ownership is route-identity based.
- `VpnEndpointInventory` has no country field. Endpoint protection is independent
  of country selection. (No country field added — confirmed.)
- `RouteMutationJournalEntry` has no country field (identity = RouteIdentity /
  DestinationPrefix / Gateway / InterfaceIndex). Journal recovery runs before
  normal reconciliation exactly as Phase 34.4 established; country
  generalization does not invalidate it. No journal entries are migrated merely
  to add country identity.
- Custom routes: no schema change; country switching never reinterprets custom
  routes as country prefixes.

Reflective guards in the permanent suite assert the absence of any `Country`
member on these three types, locking the "no country in inventory" principle.

## 9. First start / restart / disabled / offline (Steps 11, 12, 13)

Permanent tests seed a temp dir exactly as an older version would (legacy
`desired-config.json` without `DirectCountryCode`, root-level
`iran-ipv4-prefixes.txt`, legacy metadata/history) and instantiate the CURRENT
production `DesiredConfigurationStore` + `CountryPrefixStore`:

- enabled legacy → loads IR, enabled; IR cache migrates; stable.
- restart → idempotent (exactly one IR file, legacy preserved).
- disabled legacy → IR default, stays disabled; IR cache may migrate without
  enabling.
- offline (no network calls in config load + cache migration) → legacy config →
  IR; legacy cache → usable IR cache; service continues without RIPEstat.

## 10. Corrupt legacy data (Step 15)

- Corrupt config (`{ "Enabled": `) → `DesiredConfigurationStore.LoadAsync`
  throws (corrupt, fail-closed); it is never coerced to disabled. The original
  file is preserved.
- Corrupt prefix file → `PrefixFileRepository` returns the lines verbatim (no
  format validation at this layer); migration copies them as-is, does NOT
  silently empty them, preserves the legacy file, and never exposes them to a
  non-IR country. Confirmed non-destructive, not "authoritative empty."
- Corrupt ownership/journal data → not authored by these tests, but the journal
  path already fails closed on parse errors (Phase 34.4) and never infers
  ownership from native route shape, so it cannot authorize deletion of external
  routes.

## 11. Mixed-version IPC (Step 16) & JSON (Step 17)

- `SetConfigurationDirectCountry` / `RequestedCountryCode` were added in 35.5.
  OLD client → NEW service: legacy commands (status/enable/disable/profile/
  update-prefixes) continue to work; the new service simply never receives the
  new command. NEW client → OLD service: `country set` sends a command the old
  service does not understand → predictable failure (unknown command), while
  unrelated commands still work. No protocol redesign; the existing
  `IranDirectCommand` enum + `ServiceRequest.Value` envelope is unchanged, so
  backward compatibility holds for all historical commands.
- JSON: new config containing `DirectCountryCode` is readable by the historical
  model (System.Text.Json ignores unknown members by default) — proven by
  round-tripping a new-shaped JSON through a legacy-shaped record. Old JSON →
  new model works (field absent → IR default). The production `DesiredConfig
  urationStore` adds `JsonStringEnumConverter` + `DirectCountryCodeJsonConverter`
  (mirrored in the test options).

## 12. CLI / Tray compatibility (Steps 18, 19)

- Every legacy CLI command (`status`, `enable`, `disable`, `set-profile`,
  `update-prefixes`, diagnostics/support) preserves `DirectCountryCode` because
  it builds the new config via `with` (record default would otherwise reset to
  IR). A permanent test reproduces the anti-pattern and asserts the `with` form
  preserves IQ.
- Tray: the country selector is additive; no implicit country reset on Tray
  refresh/restart. The menu reflects the requested country from
  `IranDirectStatus.RequestedCountryCode`.

## 13. Synchronous country-refresh timing (Step 20) & HTTP timeout (Step 21)

`NamedPipeCommandServer.SetConfigurationDirectCountryAsync` performs
persist → `UpdatePrefixesAsync(country)` inside `OperationCoordinator`. The
prefix fetch is bounded:

- `OfficialCountryPrefixSource` is registered via
  `services.AddHttpClient<OfficialCountryPrefixSource>(c => c.Timeout =
  TimeSpan.FromSeconds(30))` (ServiceCompositionRoot).
- `CountryPrefixUpdateChecker` additionally applies
  `PrefixUpdateCheckOptions.Timeout` (default 5s) via
  `CancellationTokenSource.CancelAfter`.

Conclusion: the synchronous refresh cannot stall indefinitely; worst-case
bounded ≈ 30s by `HttpClient.Timeout`, with the checker timing out at 5s and
returning a `Failed` result (no unbounded hang). For v1 this is acceptable
operational impact: `country set` blocks until the fetch returns or times out,
then reports a partial result (config accepted, prefix refresh pending) — the
Phase 35.4/35.5 fail-closed semantics are preserved. No event bus / background
scheduler introduced. Classified as acceptable for v1; if a future requirement
demands non-blocking UX, that is a Phase 35.7/36 enhancement, not a defect.

## 14. Install / upgrade boundary (Step 22)

Application data lives under the existing data root consumed by
`DesiredConfigurationStore` / `CountryPrefixStore` / `RouteMutationJournalStore`
/ custom-route repositories. The migration logic operates entirely within that
root (root-level legacy files → `prefixes/<CC>/` subfolders). No installer
change is required for upgrade: an in-place binary replacement leaves the data
root intact, and the first run migrates legacy IR artifacts locally. Uninstall
behavior is unchanged (data-root removal is explicit/user-driven). This baseline
is essential for Phase 36 PathVeer: the data root must be remapped under the new
identity, but the legacy → country-scoped layout established here survives the
rename because paths are not country-namespaced beyond `<CC>`.

## 15. Production code policy (Step 25)

**Zero production code changes.** The Phase 35.3 migration already implements
the full upgrade contract correctly and idempotently. Tests revealed no genuine
upgrade defect, so no fix was warranted. The only new code is the permanent
acceptance test file.

## 16. Permanent tests added (Step 24)

`IranDirect.Core.Tests/Globalization/LegacyGlobalizationUpgradeTests.cs` — 23
tests covering: legacy config → IR (enabled/disabled); no-rewrite-to-read;
missing-config Phase 34.4 semantics; legacy IR prefix migration (only-legacy,
IQ-never-consumes, idempotent, both-identical, legacy-empty); legacy metadata →
IR scope (IQ untouched); legacy history → IR scope (IQ untouched); IR ETag
cannot become IQ state; inventory/journal/endpoint have no country field
(reflective); enabled legacy upgrade → stable IR; restart idempotent; disabled
legacy → IR + stays disabled; offline upgrade usable without network; corrupt
prefix preserved + not consumed by IQ; corrupt config fails closed (not
disabled); new-JSON-with-country ignored by historical model; legacy CLI `with`
preserves country.

## 17. Verification evidence (Step 27)

- `dotnet test IranDirect.Core.Tests -c Debug` → **2300 passed** (2277 baseline
  + 23 new), 0 failed.
- `dotnet test IranDirect.Service.Tests -c Debug` → **50 passed**.
- Stress → **12 passed**.
- `dotnet build IranDirect.Benchmarks -c Release` → clean.
- Focused filter `FullyQualifiedName~LegacyGlobalizationUpgradeTests` → 23/23.
- No new warnings introduced beyond the two pre-existing 35.5 nullable warnings.

## 18. Scope proof (Step 28)

`git status --short` shows exactly one untracked file:
`IranDirect.Core.Tests/Globalization/LegacyGlobalizationUpgradeTests.cs`.
`git diff --stat` (tracked) is empty. No production, IPC, Tray, CLI, or
installer source was modified. No brand rename performed.

## 19. Remaining risks (Step 33)

- The 35.5 synchronous country-refresh blocks the CLI/Tray call for up to
  ~30s on a slow source (acceptable for v1; non-blocking UX is a future
  enhancement).
- RIPEstat commercial-use terms review (from 35.3/35.4) remains a follow-up.
- Phase 36 PathVeer must remap the data root under the new identity; the
  country-scoped layout here is rename-safe.

## 20. Recommended Phase 35.7 acceptance scope (Step 34)

Phase 35.7 should: (a) run the full globalization acceptance matrix end-to-end
on a clean machine with a real legacy data directory; (b) confirm the
35.5/35.6 behaviors under a genuine service lifecycle (install → upgrade →
switch → offline → crash-recover); (c) add a cross-version IPC fixture using
actual historical `IranDirectCommand`/`ServiceResponse` shapes if a true
old-service harness is feasible; (d) sign off on the synchronous-refresh timing
as acceptable for v1; (e) gate the Phase 36 rename on the data-root remap plan.
