# Phase 35.1 — Country-Agnostic Routing Architecture Audit

Status: analysis + documentation only (no source changes)
Branch: `development/service-authority`
Base: `8b4529c fix(configuration): fail closed when desired configuration is missing` (Phase 34.4)
Affected phases: introduces Phase 35 (country generalization) and defers Phase 36 (brand rename).

This phase does NOT implement country support, rename IranDirect, change routing
behavior, add SaaS, modify telemetry contracts, or change persisted schemas.

---

## 1. Current country-routing architecture (end-to-end)

```
DesiredConfiguration
  └─ AutoUpdatePrefixes / PrefixUpdateInterval   (no country field)
        │
IranDirectController.UpdatePrefixesAsync(ct)      (calls _prefixSource.FetchAsync)
        │
IPrefixSource (abstract)  ◄──────────────────────  the ONLY geography-neutral seam
        │
OfficialIranPrefixSource.FetchAsync(req, ct)       (HARDCODED ?resource=IR)
        │   GET https://stat.ripe.net/data/country-resource-list/data.json?resource=IR
        ▼
PrefixFileRepository  →  "iran-ipv4-prefixes.txt"  (cached prefixes, no country tag)
        │
RuntimeDecisionBuilder / RuntimeCoordinator.BuildPlanAsync
        │   (planner consumes a list of prefix strings; does NOT know country)
        ▼
RuntimeChangeSetPlanner → desired prefix routes (string DestinationPrefix)
        │
RuntimeExecutor → WindowsRuntimeExecutionStepHandler → native route mutation
        (operates on ManagedRoute; no country/Iran awareness)
```

**Most important finding:** the routing engine — controller prefix-update entry
point, planner, change-set planner, executor, Windows step handler, route
inventory, mutation journal — is **already geography-neutral**. It consumes a
list of prefix strings and a desired plan. It has no branch, constant, or
assumption tied to Iran.

The country identity lives in exactly ONE place: the prefix *data source*
(`OfficialIranPrefixSource`), which bakes `?resource=IR` into its `Descriptor.Uri`
and hardcodes the word "Iranian" in one error message. The prefix *provider*
(`IranPrefixProvider`) and *update checker* (`OfficialIranPrefixUpdateChecker`)
are thin IR-named wrappers around that source.

**Answer to the central question:** "Iran" is part of the **prefix source
feeding the engine**, NOT part of the routing engine.

---

## 2. Iran-specific coupling inventory (country coupling only)

CATEGORY C — Country data source (functional):
- `IranDirect.Core/Prefixes/OfficialIranPrefixSource.cs`
  - `Descriptor.Uri = "https://stat.ripe.net/data/country-resource-list/data.json?resource=IR"` (IR hardcoded)
  - `Descriptor.Id = "ripe-stat-country-resource-list-ipv4"`, `DisplayName = "RIPEstat Iran IPv4 country resource list"`
  - error: `"RIPEstat returned no Iranian IPv4 prefixes."`
  - `CountryResourceResponse` JSON binding to RIPEstat `data.resources.ipv4`
- `IranDirect.Core/Prefixes/IranPrefixProvider.cs` — type name only; wraps source.
- `IranDirect.Core/Prefixes/OfficialIranPrefixUpdateChecker.cs` — type name;
  references `OfficialIranPrefixSource.Descriptor.Uri` (line 167, 204).

CATEGORY B — Type/project naming (behavior already reusable, rename later):
- `IranPrefixProvider`, `OfficialIranPrefixSource`, `OfficialIranPrefixUpdateChecker`
- Remaining "Iran*" in Prefixes namespace are doc-comment branding only.

CATEGORY E — Persistent identity (Iraq-specific filename):
- `ServiceCompositionRoot.cs:75` → cached prefix file `"iran-ipv4-prefixes.txt"`
- Country-neutral persisted files (rename optional, data is source-scoped, not
  country-scoped): `prefix-source-metadata.json`, `prefix-source-update-history.json`
  (these describe the source, not a country, so they stay single-instance).

CATEGORY D — Business logic (country branch): **NONE found.** No planner/executor/
handler/controller code branches on Iran or IR.

CATEGORY A/G/F (branding, docs, external identifiers): see §4.

---

## 3. Branding coupling inventory (deferred to Phase 36 — DO NOT conflate)

These are BRAND problems, not country problems. They must NOT be changed in
Phase 35.

- Project/assembly names: `IranDirect.Core`, `IranDirect.Service`, `IranDirect.Cli`,
  `IranDirect.Tray`, `IranDirect.Benchmarks`, `IranDirect.Testing`.
- Namespaces: `IranDirect.Core.*`, `IranDirect.Service.*`, etc.
- Windows Service name: `Program.cs:16` → `options.ServiceName = "IranDirect Service"`.
- Named pipe: `IranDirectPipeNames.Control` (`NamedPipeCommandServer.cs:100`).
- Telemetry: `IranDirectTelemetry.SourceName = "IranDirect.Core"`
  (`Observability/Telemetry/IranDirectTelemetry.cs:24`); metric/activity names
  `irandirect.*` (e.g. `irandirect.dns.lookups`). No country semantics.
- CLI text: `"Usage: IranDirect.Cli ..."`, `"IranDirect command failed"`,
  `"IranDirect Diagnostics"` (`DiagnosticReportCliRenderer.cs`, `CustomRouteCliRunner.cs`,
  `SupportBundlePathBuilder.cs` `DefaultFilePrefix = "IranDirect-Support"`).
- Support bundle prefix, diagnostic report titles.
- Deployment: `deployment/observability/*` references "IranDirect" (alert names,
  `.env`, collector config).
- Docs: `docs/architecture-knowledge-base/04-projects/IranDirect/*`, journal,
  glossaries, templates.

**Separation rule (mandatory):** Phase 35 removes *country* coupling (data
source + a `CountryCode` config + persistence layout). Phase 36 removes *brand*
coupling (names/telemetry/service/pipe/docs). The two never overlap in a change.

---

## 4. Prefix source abstraction audit (§7)

There is **already a clean abstraction**:

- `IPrefixSource.FetchAsync(PrefixSourceRequest, ct) → PrefixSourceFetchResult`
- `IPrefixUpdateChecker.CheckAsync(ct) → PrefixUpdateCheckerResult`
- `PrefixSourceDescriptor` (Id, DisplayName, Uri, Format, ParserVersion) — fully
  country-neutral data model.
- `PrefixSourceFetchResult` (Prefixes, ETag, LastModified, ContentHash, …) — neutral.
- `PrefixFileRepository`, `PrefixSourceMetadataStore`, `PrefixSourceUpdateHistoryStore`
  — persist the fetched dataset + source metadata; none carry a country field.

`OfficialIranPrefixSource` implements `IPrefixSource` but ignores
`PrefixSourceRequest` and bakes the country into `Descriptor.Uri`.

**Conclusion:** country support can be implemented by making the source
country-parameterized, e.g.:

```
ICountryPrefixSource : IPrefixSource
    PrefixSourceFetchResult FetchAsync(CountryPrefixRequest req, ct)
    // req.CountryCode = "IR" | "IQ" | "RO" | ...
```

or by extending `PrefixSourceRequest` with `CountryCode` and having a single
generic `OfficialCountryPrefixSource` build its URI from `?resource={code}`.
The RIPEstat `country-resource-list` endpoint already accepts any ISO alpha-2
`resource`, so the SAME endpoint serves all countries — no 200 hardcoded URLs.

---

## 5. Data-source feasibility (§8)

- **Source:** RIPEstat `data/country-resource-list` (RIPE Stat). Currently only
  `?resource=IR`.
- **Provider coverage:** RIPEstat serves `country-resource-list` for RIPE-region
  countries (EU/adjacent). For IR/IQ/RO/TR it works via RIPE. For non-RIPE
  regions (e.g. full global), a different authoritative source would be needed.
  Repository evidence establishes **one** consistent source that covers IR/IQ/RO
  (all RIPE-reachable); this satisfies the Phase requirement (IR/IQ/RO). A future
  global catalog is out of scope and must be a single configurable source, not
  200 hardcoded URLs.
- **Format:** JSON `data.resources.ipv4` (array of CIDR strings). Parsed by
  `OfficialIranPrefixSource`. IPv6 field `ipv6` exists in the endpoint but is NOT
  fetched today (see §9).
- **Update mechanism:** ETag / If-Modified-Since honored (`NotModified` path).
- **Licensing:** RIPEstat is an open public dataset; no license restriction is
  encoded in the repo. Document requirement, do not guess.

---

## 6. IPv4 and IPv6 audit (§9)

**IPv4 — generic and supported.**
The engine consumes prefix strings and routes them. `ManagedRoute.DestinationPrefix`
and `Gateway` are plain strings; the planner/executor are address-family agnostic.
The `OfficialIranPrefixSource` fetches IPv4 only, and the OS read path
(`WindowsRouteApi.cs:25`) uses `Get-NetRoute -AddressFamily IPv4`. So **today
country routing is IPv4-only**, but that limitation is in the *source* and the
*OS IPv4-only route API*, not in the core planner.

**IPv6 — NOT supported today (country-general or otherwise).**
- No IPv6 fetch in `OfficialIranPrefixSource` (only `ipv4`).
- `WindowsRouteApi` reads IPv4 only (`-AddressFamily IPv4`); write path uses the
  native IPv4 forward-entry API.
- `VpnProviderType` enum has a single `OpenVpn` value (VPN-agnostic to IP version,
  but no v6 route plumbing exists).

**Classification:**
- Country-general IPv4 support: ✅ achievable with a bounded refactor (parameterize source).
- Country-general IPv6 support: ❌ not available today; requires OS IPv6 route
  plumbing + source v6 fetch. Out of scope for the first country-generic slice;
  document as a later capability.

---

## 7. DesiredConfiguration analysis (§6)

Current fields: `SchemaVersion`, `Enabled`, `VpnProfilePath`, `AutoRepair`,
`AutoUpdatePrefixes`, `PrefixUpdateInterval`, `RepairInterval` (plus routing
booleans). **There is NO country concept and NO prefix-source identifier.**

**Smallest future addition:** a single optional string field:

```
public string? CountryCode { get; init; }   // ISO 3166-1 alpha-2, e.g. "IR"
```

- Selects exactly one direct/bypass country (matches current single-source product).
- `null`/absent on legacy persisted config → **interpret as "IR"** (backward
  compatibility). This is the required migration contract; it MUST be implemented
  in the migration phase (35.6), NOT in 35.1.
- Does **not** require a schema-version bump if treated as optional with a
  legacy-default of IR (additive field). If strict validation is desired, bump
  `SchemaVersion` to 2 with the same legacy-default rule.
- `Enabled` stays independent of `CountryCode` (see §15).

---

## 8. Persistence analysis (§16, §24)

Current persisted files (all under the service data dir):
- `iran-ipv4-prefixes.txt` — **Iraq-specific name**; the cached prefix list.
- `prefix-source-metadata.json` — source metadata (single, country-neutral).
- `prefix-source-update-history.json` — update history (single, country-neutral).
- `desired-configuration.json` — add `CountryCode` (additive).
- `route-inventory.json`, `endpoint-inventory.json`, `state.json`,
  `route-mutation-journal.json`, `custom-route-dns-cache.json` — country-neutral
  already (no rename required for country generalization).

**Recommended future layout (least disruptive):** keep a single cached prefix
file but rename to a country-parameterized path, e.g.:

```
prefixes/
  IR/dataset.json     (cached prefixes)
  IQ/dataset.json
  RO/dataset.json
```

OR keep flat `country-prefixes-{code}.txt`. Either is fine; the directory form
isolates datasets so switching IR→IQ can't mix them and stale data is easy to
prune per-country.

**Migration requirements (to be implemented in 35.6, not 35.1):**
- Legacy `iran-ipv4-prefixes.txt` → migrate into `prefixes/IR/dataset.json`.
- Existing `desired-configuration.json` with no `CountryCode` → treat as IR.
- Switching IR→IQ: obtain/validate IQ dataset, compute desired IQ routes, remove
  owned IR prefix routes no longer desired, add IQ routes, preserve endpoint
  protection + custom routes. The planner already reconciles from desired state,
  so this is natural (see §17).
- Rollback IQ→IR: same mechanism, symmetric.
- Stale-data safety: only the *currently selected* country's dataset is authoritative
  for reconciliation; previously selected country files are inert and can be pruned.
- Upgrade must not lose: desired config, route inventory, endpoint inventory,
  mutation journal, custom-route cache, prefix state. All of those are
  country-neutral or country-scoped in an isolated path → safe.

---

## 9. Planner / executor country-awareness audit (§12)

`RuntimeChangeSetPlanner`, `ExecutionPreviewBuilder`, `RuntimeExecutor`,
`WindowsRuntimeExecutionStepHandler`, route inventory models, `RouteMutationJournal*`
were inspected for Iran/country references.

**Result: COUNTRY-AGNOSTIC — NO CHANGE REQUIRED.**

The only "Iran" strings in these files are:
- namespace branding (`IranDirect.Core.Runtime.*`),
- doc comments ("IranDirect initiated the operation", "IranDirect-owned route")
  which describe route *ownership* by the product, not geography.

These are stable low-level route-ownership concepts. **Do NOT rename them for
country reasons.** (Ownership/brand wording is a Phase 36 concern if at all.)

---

## 10. VPN endpoint protection interaction (§13)

Verified: `OpenVpnEndpointProvider`, `VpnEndpointRouteManager`, `VpnEndpointInventoryStore`,
endpoint rotation, and the crash-consistent `RouteMutationJournal*` are entirely
independent of the prefix dataset. The endpoint host route is always direct
regardless of which country's prefixes are selected. **No coupling between
endpoint logic and Iran prefixes.** Country generalization does not alter
endpoint protection, gateway/interface selection, or the mutation journal.

---

## 11. Custom routes interaction (§14)

Custom routes (`CustomRoute*`) are a separate desired set, always direct, and
supplement (not replace) the country dataset. The planner merges country prefix
routes + endpoint routes + custom routes into one desired set; precedence is by
route identity, not country. Selecting/shifting `CountryCode` does not change
custom-route meaning. Existing behavior preserved; no change.

---

## 12. Enable/disable behavior (§15)

`Enabled` is orthogonal to `CountryCode`. A saved disabled config with
`CountryCode=IQ` remains disabled (the worker skips reconciliation; see Phase
34.4). No code change needed; the worker gates on `Enabled` before any planner
use. Changing country never enables/disables routing.

---

## 13. Country switching model (§17)

Because the planner reconciles from the *desired* state every cycle, switching
`CountryCode` IR→IQ is naturally handled:

1. next cycle fetches/validates IQ dataset;
2. desired prefix routes recomputed from IQ prefixes;
3. owned IR prefix routes no longer in the desired set are removed;
4. desired IQ routes added;
5. VPN endpoint protection preserved;
6. unrelated custom routes preserved;
7. ownership (route-mutation journal) updated consistently.

The engine already does 3–7 generically; only step 1–2 depend on the
country-parameterized source. **No new reconciliation logic required.**

---

## 14. Offline behavior (§18)

`OfficialIranPrefixUpdateChecker` already implements last-known-good + ETag
caching + update history. With a country-parameterized source, the chosen
country's last-validated dataset persists, so offline reconciliation continues
from it. If the user switches to a country with no cached valid dataset and the
source is unavailable, the engine must **not** use another country's dataset or
empty prefixes — it should fail safe (skip prefix reconciliation, preserve
existing authoritative route state) per the chosen contract. Current fallback is
country-neutral and reusable; the "fail safe on unknown country" branch is a
small addition in 35.4.

---

## 15. Country-code validation (§19)

Future validation: normalize to uppercase invariant (`"iq" → "IQ"`), validate
against ISO 3166-1 alpha-2. Repository already has validation patterns
(`DesiredConfigurationValidator`, `PrefixSourceMetadataValidator`). Recommended:
a small `CountryCode` value object / `ISO3166` validator (NOT a giant enum — too
much maintenance). Invalid: `"IRQ"` (3 chars), `""`/`null`, unrecognized code.
No implementation in 35.1.

---

## 16. UI / CLI / Tray audit (§20)

CLI strings are brand-only today (`IranDirect.Cli`, `"IranDirect command failed"`).
No country command exists. Future minimal CLI (Phase 35.5):
- `country get` / `country set RO` / `country list`
These operate on `DesiredConfiguration.CountryCode` via existing IPC
`SetConfiguration`. Tray: a "Direct country: [Romania (RO) ▼]" selector. No
behavior change in 35.1.

---

## 17. IPC audit (§21)

`NamedPipeCommandServer` already transports `DesiredConfiguration` (get/set via
`SetConfiguration`). Adding `CountryCode` to the config model is sufficient —
**NO new IPC command and NO protocol change required** (additive JSON field).
The pipe name, command set, and framing are brand/external identifiers (Phase 36).

---

## 18. Diagnostics / support audit (§22)

`RuntimeSnapshot`, `SupportSnapshot`, `DiagnosticReport`, `ExecutionPreview`, and
status models contain no country field. Future: surface the selected
`CountryCode` (and the active dataset's source id/version) in the snapshot so
support can answer "what country dataset was active?" without exposing network
data. Additive field only; no change in 35.1.

---

## 19. Telemetry audit (§23)

Telemetry identifiers (`IranDirect.Core` ActivitySource, `irandirect.*` metrics)
are **brand**, not country. **No country tag should be added.** Country is a
bounded dimension but must not become a telemetry tag without a separate privacy/
cardinality review. No telemetry change in Phase 35.

---

## 20. Tests & benchmark coupling (§26)

Functional country coupling in tests (22 references) is concentrated in
`IranDirect.Core.Tests/Prefixes/`:
- `OfficialIranPrefixSourceFaultInjectionTests.cs`
- `OfficialIranPrefixUpdateCheckerTests.cs`, `...FaultInjectionTests.cs`
- `PrefixSourceModelTests.cs`, `PrefixUpdateHistoryIntegrationTests.cs`,
  `PrefixUpdateMetadataIntegrationTestsTests.cs`
- plus snapshot/telemetry tests referencing the IR source URI.

These are SOURCE tests; they become country-parameterized fixtures in 35.3.
All other test "Iran" references are namespace branding (`IranDirect.*.Tests`).

**Required future test matrix (at least):** IR, IQ, RO — proving identical
routing semantics independent of country (reconciliation, switching, offline,
endpoint protection, custom-route precedence). Benchmarks are prefix-count driven
(`Sizes.cs`: 1K–50K) and already geography-neutral; 50K remains sufficient.

---

## 21. Performance (§27)

Country datasets differ in size (small: RO; large: IR). Benchmarks already cover
1K–50K route counts, so the planner/executor scale is validated for the largest
expected dataset. No optimization needed.

---

## 22. Security considerations (§28)

Existing protections in `OfficialIranPrefixSource`:
- `IsValidIpv4Prefix` (address + /length ≤ 32),
- `Distinct` + ordering (dedupe),
- `PrefixContentHasher` (integrity/diff),
- ETag / If-Modified-Since (replay/stale control).
Missing / to add in 35.3:
- upper bound on dataset size (reject unexpectedly enormous datasets),
- explicit overlap/duplicate handling at the dataset level,
- source-integrity (signature) only if evidence supports it — NOT added now,
- country-parameterized fetch must re-validate per country (no shared cache
  cross-contamination).

---

## 23. Explicit answers — Q1 to Q10 (§30)

**Q1. Can the current routing/planner/executor engine already route an arbitrary
country dataset without fundamental changes?**
**YES.** The engine is geography-neutral; it consumes prefix strings + a desired
plan. Only the prefix *source* is IR-bound.

**Q2. What exactly prevents Iraq or Romania from working today?**
Only `OfficialIranPrefixSource.Descriptor.Uri` hardcodes `?resource=IR` (and its
IR-named type/error). `IranPrefixProvider` / `OfficialIranPrefixUpdateChecker`
are IR-named wrappers. `DesiredConfiguration` has no `CountryCode`. The cached
file is named `iran-ipv4-prefixes.txt`. No engine logic blocks other countries.

**Q3. How much production code must change?**
Bounded: (a) parameterize the prefix source with `CountryCode` (~1 new/changed
source class + `PrefixSourceRequest`/descriptor); (b) add `CountryCode` to
`DesiredConfiguration` (+ validator default IR); (c) wire the source from config
in `ServiceCompositionRoot`; (d) country-scoped prefix persistence path; (e)
optional country field in snapshots. The planner/executor/handler/journal:
**zero** changes.

**Q4. Which low-level components require NO change?**
`RuntimeChangeSetPlanner`, `ExecutionPreviewBuilder`, `RuntimeExecutor`,
`WindowsRuntimeExecutionStepHandler`, `ManagedRoute`, route inventory,
`RouteMutationJournal*`, VPN endpoint protection, custom routes, IPC framing,
telemetry. Marked COUNTRY-AGNOSTIC.

**Q5. Can country switching naturally reconcile old routes to new routes?**
**YES** — the planner reconciles from desired state every cycle; switching
CountryCode recomputes desired prefix routes and the executor removes stale +
adds new generically.

**Q6. What persistence migration is required?**
Migrate `iran-ipv4-prefixes.txt` → `prefixes/IR/dataset.json`; legacy config
without `CountryCode` → interpret as IR. All other stores are country-neutral.
See §8.

**Q7. Does IPv6 work generically today?**
**NO.** IPv4-only today (source fetches `ipv4`; `WindowsRouteApi` is IPv4-only).
IPv6 is a separate later capability, not part of the first country-generic slice.

**Q8. Does country generalization require IPC changes?**
**NO.** `CountryCode` is an additive `DesiredConfiguration` field transported by
the existing IPC `SetConfiguration`. No new command/protocol.

**Q9. Can existing Iran users upgrade without behavioral change?**
**YES.** Legacy config without `CountryCode` → IR (backward-compat default).
Prefix cache migrates to `prefixes/IR/`. All other persisted state is
country-neutral.

**Q10. Is the architecture suitable for later SaaS-controlled country policy?**
**PARTIALLY.** The engine is ready; the *source* is currently a fixed RIPEstat
URL. A later SaaS policy would swap the `ICountryPrefixSource` implementation
(country→dataset resolver) without touching the engine. The single-`CountryCode`
model can later become `CountryCodes: collection` (see §24) if multi-country
policy is required — that change is additive and non-breaking if the serializer
keeps `CountryCode` as the primary and treats `CountryCodes` as an optional
collection.

---

## 24. Multi-country future compatibility (§11)

Initial model: **one** `CountryCode` (matches current single-source product;
simplest). Keep the door open: a later `CountryCodes: IReadOnlyList<string>`
collection is a non-breaking additive change if the serializer treats a present
`CountryCode` as the primary and `CountryCodes` as optional. Do NOT implement
multi-country now.

---

## 25. Phase 35 implementation roadmap (§31)

**Phase 35.2 — Country identity / configuration model**
- Goal: add `CountryCode?` to `DesiredConfiguration`; validator default IR when
  absent; normalization + validation (uppercase ISO alpha-2).
- Production: `DesiredConfiguration.cs`, `DesiredConfigurationValidator.cs`.
- Tests: schema round-trip; legacy-absent → IR; invalid code rejected.
- Migration: none yet (default rule defined).
- Commit: `feat(configuration): add CountryCode for direct-country routing`

**Phase 35.3 — Generic country-prefix source + persistence**
- Goal: `ICountryPrefixSource` / parameterized `OfficialCountryPrefixSource`
  building `?resource={code}`; country-scoped dataset persistence
  (`prefixes/{code}/dataset.json`); keep ETag/cache/history.
- Production: new source class(es); `PrefixSourceRequest`; `PrefixFileRepository`
  path parameterization; `ServiceCompositionRoot` wiring from `CountryCode`.
- Tests: IR/IQ/RO fetch + parse; fault injection; no cross-country mixing.
- Migration: `iran-ipv4-prefixes.txt` → `prefixes/IR/dataset.json`.
- Commit: `feat(prefixes): support country-parameterized prefix source`

**Phase 35.4 — Country switching & reconciliation & offline safety**
- Goal: verify switching IR↔IQ reconciles; fail-safe when selected country has
  no cached valid dataset and source unavailable.
- Production: `IranDirectController`/update path uses `CountryCode`; offline
  guard.
- Tests: switching matrix (IR→IQ→IR); offline unknown-country fail-safe.
- Commit: `feat(runtime): reconcile on country switch; fail safe offline`

**Phase 35.5 — CLI / Tray country selection**
- Goal: `country get|set|list` CLI + Tray selector; additive config transport.
- Production: CLI commands; Tray control; no IPC protocol change.
- Tests: CLI parse/render; IPC set/get round-trip.
- Commit: `feat(cli): add country selection commands`

**Phase 35.6 — Backward compatibility / migration validation**
- Goal: prove legacy IR install upgrades with no behavior change; dataset
  migration; legacy-absent→IR.
- Production: migration helper for `iran-ipv4-prefixes.txt` → `prefixes/IR/`.
- Tests: migration integration; full IR/IQ/RO matrix + endpoint/custom-route
  precedence.
- Commit: `fix(migration): migrate legacy Iran prefix state to country layout`

**Phase 35.7 — Multi-country validation matrix / end-to-end acceptance**
- Goal: IR/IQ/RO identical routing semantics; security bounds (size/dedup/
  overlap); benchmark at 50K.
- Tests: end-to-end acceptance across countries; stress.
- Commit: `test(globalization): country-routing end-to-end acceptance`

---

## 26. Phase 36 deferred rename boundary (§32)

Do NOT rename in Phase 35. Phase 36 will choose a new brand/domain, then migrate:
- `IranDirect` (product/brand name), `IranDirect.Core`, `IranDirect.Service`,
  `IranDirect.Cli`, `IranDirect.Tray`, `IranDirect.Benchmarks`, `IranDirect.Testing`
- namespaces `IranDirect.*`
- Windows Service name (`"IranDirect Service"`)
- named pipe `IranDirectPipeNames.Control`
- telemetry `SourceName = "IranDirect.Core"` + `irandirect.*` metric/activity names
- CLI/Tray display strings, support-bundle prefix `"IranDirect-Support"`
- `deployment/observability/*` references
- `docs/architecture-knowledge-base/04-projects/IranDirect/*` and journal/glossary
- repository name (if decided)

(NOTE: doc-comment phrases "IranDirect initiated the operation" / "IranDirect-owned
route" describe route *ownership*, not geography — rename only as part of the
brand pass, not country work.)

---

## 27. Final report items (§35) — condensed

1. Commit/base: `8b4529c` (Phase 34.4) on `development/service-authority`.
2. Build/test baseline: build clean (1 pre-existing test-fake warning CS0649);
   Core 2204 pass; Service 49 pass; Stress 12 pass; Benchmark Release build ok.
3. Engine is geography-neutral; Iran lives only in the prefix source.
4. Coupling inventory: C (source URI/type/error), B (IR type names),
   E (`iran-ipv4-prefixes.txt`); D (business logic): none.
5. Brand inventory: see §3/§26 (Phase 36).
6. Data source: RIPEstat `country-resource-list`, one endpoint covers IR/IQ/RO.
7. Config: no country field today; add optional `CountryCode` (legacy→IR).
8. Persistence: only `iran-ipv4-prefixes.txt` is country-named; migrate to
   `prefixes/IR/`.
9. IPv4: generic, supported. 10. IPv6: NOT supported today.
11. Planner/executor: COUNTRY-AGNOSTIC, no change.
12. VPN endpoint: no coupling. 13. Custom routes: unaffected.
14. Switching: naturally reconciles. 15. Offline: reusable + small fail-safe add.
16. CLI/Tray: brand-only today; add country commands later.
17. IPC: no change (additive config field). 18. Diagnostics: additive field.
19. Telemetry: brand only; no country tag.
20. Backward-compat: legacy→IR; safe upgrade. 21. Security: reuse hash/ETag/dedup;
   add size/overlap bounds in 35.3. 22. Test matrix: IR/IQ/RO required.
23. Q1–Q10: answered in §23. 24–26: roadmap + Phase 36 boundary above.
27. Files changed this phase: docs only (see §34). 28. No source changed.
29. Risks: IPv6 excluded from first slice; RIPEstat RIPE-region scope for global
   catalog; brand/country must stay separated. 30. Commit:
   `docs(globalization): audit country-agnostic routing support`.

---

## 28. No-source-change proof (§34)

Permanent changes in this phase:
- `docs/globalization/phase-35.1-country-routing-audit.md` (new)
- `AI-START-HERE.md` (navigation link added)

No `.cs`, `.csproj`, appsettings, deployment, tests, benchmarks, or generated
files changed. No temporary scripts retained.
