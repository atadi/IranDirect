# Phase 35.7 — Global Country Routing End-to-End Acceptance

Status: COMPLETE. Country generalization is accepted as production-ready for a
single-country (one `DirectCountryCode`) model, with non-blocking operational
limitations noted for large datasets (see §18–§20). No production code changed.

Branch: `development/service-authority` (HEAD after this phase's commit).
Verification: `dotnet test IranDirect.Core.Tests` → 2343 passed, 0 failed
(includes 43 new `GlobalCountryRoutingAcceptanceTests`); Service 50/50;
Benchmark Release clean; focused 43/43; Stress 12/12.

---

## 1. Globalization baseline contract (Steps 2, 21)

| Aspect | Contract |
| --- | --- |
| Country identity | `DirectCountryCode` (normalized ISO-3166-1 alpha-2). Catalog = full ISO set; sample supported: IR, IQ, RO, US, BR, JP, ZA, AU. |
| Source | `ICountryPrefixSource` (RIPEstat country-resource-list in production). Per-country fetch, no global source. |
| Persistence | `CountryPrefixStore`: `prefixes/<CC>/{ipv4-prefixes.txt, metadata.json, update-history.json}`. Fully isolated per country. |
| Switching | `SetConfigurationDirectCountry` → persist → `UpdatePrefixesAsync(cc)` → reconcile (remove old-owned, add new-target). No special-casing. |
| Failure behavior | Source down / no cache → `RuntimeReconciliationStatus.Blocked`, existing routes retained fail-closed, retry possible. No wrong-country dataset used. |
| Migration | Legacy `iran-ipv4-prefixes.txt` (IR only) migrates copy-on-read to `prefixes/IR/`. Idempotent, legacy kept. Non-IR never consumes legacy IR. |
| UX | CLI `country get/set/list`; Tray dropdown from centralized catalog; status shows `RequestedCountryCode`. None imply enable. |
| Routing isolation | Planner/executor neutral; ownership by route identity; custom/external/endpoint routes preserved. No country prefix logic leaks into endpoint protection. |

Requested vs effective semantics (Step 21):
- `RequestedCountryCode` = persisted selection (may be IQ even if unreachable).
- Available dataset = cached/last-known-good for the requested country.
- Reconciliation state = Blocked (no destructive empty reconcile) until a valid
  dataset exists. Requested remains selected; retry possible.

---

## 2. Acceptance results by matrix (Step 29)

### CONFIGURATION
- IR: PASS
- IQ: PASS
- RO: PASS
- legacy no-country (config missing `DirectCountryCode`): PASS — loads as IR, preserves Enabled/VpnProfilePath (35.6).
- invalid country: PASS — CLI rejects; routing layer never receives malformed country (35.6/IPC).
- missing config: PASS — Phase 34.4 unconfigured, zero reconciliation.

### PREFIX SOURCE
- country binding: PASS
- global source: PASS WITH LIMITATION (production uses RIPEstat per-country; synthetic ControllablePrefixSource in tests)
- validation: PASS (malformed lines dropped, never authoritative)
- wrong-country isolation: PASS (IQ never reads IR cache)
- offline cache: PASS (cached target switch safe; no-cache blocks)

### SWITCHING
- IR→IQ, IQ→RO, RO→IR, IR→US, US→BR: PASS (old-owned removed, new-target added, endpoint/custom/external preserved, 2nd cycle no-op)
- large-country switch (US 70K): PASS WITH LIMITATION (see §18)
- failed switch: PASS (blocked, no destructive change)

### PERSISTENCE
- per-country isolation: PASS (`prefixes/<CC>/` each independent)
- legacy migration: PASS (idempotent, legacy kept)
- restart: PASS (requested unchanged, correct scope selected, no cross-country meta/history)
- metadata/history: PASS (per-country; verified in 35.6 `LegacyGlobalizationUpgradeTests`)

### ROUTING
- planner neutrality: PASS
- executor neutrality: PASS
- external-route safety: PASS (route sharing destination with dataset prefix but different gateway/ifindex is never deleted)
- endpoint preservation: PASS (VPN endpoint host route direct; rotation independent of country)
- custom-route preservation: PASS (current precedence unchanged; no redefinition)

### RECOVERY
- journal add: PASS
- journal delete: PASS
- restart after switch: PASS
- journal has NO country field (ownership-proof only) — PASS

### UX
- CLI: PASS (`country get/set/list`, invalid, source-failure, cached; legacy commands preserve `DirectCountryCode`)
- Tray: PASS (catalog selector, current country, legacy→IR, change, disabled-state, unavailable, no implicit enable/reset) — covered by 35.5 `CountryMenuBindingTests`/`CountrySelectionAcceptanceTests`
- IPC: PASS (`GetConfiguration` carries `DirectCountryCode`; `SetConfigurationDirectCountry` works; `RequestedCountryCode` in status; malformed country rejected; unknown command bounded) — covered by 35.6 `LegacyGlobalizationUpgradeTests`

### PERFORMANCE
- small dataset: PASS
- medium dataset: PASS
- large dataset (US 70K): PASS WITH LIMITATION (operational-viability assessment, §20)
- coordinator blocking bound: PASS WITH LIMITATION (synchronous refresh ≤ ~30s via HttpClient.Timeout; acceptable v1, §22)

### COMPATIBILITY
- legacy upgrade: PASS
- old JSON (no `DirectCountryCode`): PASS → IR default
- mixed-version IPC: PASS (OLD client→NEW service works; NEW `country set`→OLD service fails predictably; no protocol redesign) — 35.6

### BOUNDARIES
- IPv4-only documented: PASS (v1 IPv4 only; no silent IPv6) — §24
- single-country documented: PASS (`DirectCountryCode`, not `[]`) — §25
- RIPEstat commercial follow-up documented: PASS (pre-commercial item) — §23

No category omitted.

---

## 3. Large-country findings (Steps 18–20)

- US source can return ~70K RIR-allocated prefixes. `PrefixWorkloadGenerator`
  deterministically produces 70K distinct valid CIDRs; the pipeline parsed,
  validated, persisted, planned, and executed 70K desired routes within bounded
  allocation (< 512 MB working-set delta) in `GlobalCountryRoutingAcceptanceTests
  .LargeCountry_Us70k_*`.
- The harness uses `InMemoryRouteManager` (an in-process stand-in for the native
  route table), so NO real 70K OS routes were installed on the developer
  machine — this validates planning/persistence/ownership at scale only.
- Windows route-table operational reality: installing/removing tens of thousands
  of real per-prefix routes via netsh/PowerShell is slow and fragile; the
  executor's per-route mutation cost dominates at 25K–70K. This is an
  OPERATIONAL concern, not a correctness defect.
- Aggregation audit (Step 19): synthetic input is already distinct valid CIDRs
  (raw == unique). Real RIPEstat data may contain duplicates/overlaps; the
  current parser keeps distinct lines. Prefix aggregation (CIDR minimization) is
  OUT OF SCOPE for v1 and requires no production change to be correct — it is a
  scaling optimization.
- Assessment: GLOBAL COUNTRY DATASET SUPPORT is technically correct and bounded
  in memory, but operationally risky at 70K unaggregated prefixes on Windows.
  RECOMMENDATION: treat large-dataset countries as a known limitation; Phase
  35.7A (if opened) should add optional CIDR aggregation before declaring
  global production readiness for US/BR-scale datasets.

---

## 4. Timing & provider sign-offs (Steps 22, 23)

- Synchronous refresh: `SetConfigurationDirectCountry` → persist → refresh under
  `OperationCoordinator`. Bounded by `HttpClient.Timeout = 30s` plus checker
  `CancelAfter(5s)`. No unbounded stall. Verdict: **PASS WITH LIMITATION**
  (CLI/Tray block up to ~30s on a slow source; acceptable for v1).
- RIPEstat dependency: provider = RIPEstat country-resource-list; semantics =
  RIR allocation/registration (NOT geolocation); interface replaceable;
  commercial-use/service-terms review is a PRE-COMMERCIAL-LAUNCH item, not a
  technical blocker.

---

## 5. Boundaries (Steps 24, 25)

- IPv4-only: confirmed. No IPv6 country routing in v1; user-facing docs/help do
  not claim all-IP routing.
- Single-country: confirmed. One `DirectCountryCode` per configuration. No
  `DirectCountryCode[]`. Architecture remains extensible.

---

## 6. Remaining Iran-semantic references (Step 26)

After Phase 35.7, every remaining "IranDirect"/"Iran" occurrence is
classifiable as:
- **brand/legacy compatibility**: product name `IranDirect`, legacy migration
  fixture paths (`iran-ipv4-prefixes.txt`), legacy config example, and the
  historical `IR` default for migrated installs.
- **historical migration fixture/documentation**: Phase 35.2–35.6 legacy
  artifacts, this document's references to pre-35.3 behavior.

There is NO active routing assumption that means Iran specifically. The default
`IR` on legacy upgrade is a migration/default choice, not a hardcoded Iran route.

---

## 7. Phase 36 entry gate (Step 31)

Decision: **YES WITH EXPLICIT NON-BLOCKING LIMITATIONS.**

Required conditions:
- IR/IQ/RO switching proven — YES
- wrong-country fallback impossible — YES (fail-closed, no cross-country use)
- offline behavior safe — YES
- upgrade compatibility proven — YES (35.6)
- crash recovery proven — YES (journal country-agnostic)
- CLI/Tray/IPC functional — YES
- no active Iran-only routing assumption — YES
- large-country route-count risk understood — YES (documented §3, §20)

Non-blocking limitations to carry into Phase 36:
1. Synchronous refresh blocks CLI/Tray up to ~30s on slow sources.
2. 70K-scale countries are correct but operationally heavy on Windows; optional
   aggregation deferred to a possible 35.7A.
3. RIPEstat commercial-terms review remains a pre-launch item (not a code block).

Phase 36 (IranDirect → PathVeer rename) may proceed once the data-root remap
plan accounts for the `prefixes/<CC>/` layout.

---

## 8. Files changed

- `IranDirect.Core.Tests/Globalization/GlobalCountryRoutingAcceptanceTests.cs`
  (NEW, 43 tests) — clean install, legacy upgrade, country matrix, switch
  matrix, offline matrix, restart matrix, crash recovery, ownership safety,
  persistence isolation, large-country (US 70K), aggregation audit.
- `docs/globalization/phase-35.7-global-routing-acceptance.md` (NEW).
- `AI-START-HERE.md` (nav line added).

No production source modified.
