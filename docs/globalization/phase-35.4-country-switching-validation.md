# Phase 35.4 — Country Switching & Offline-State Acceptance

Status: COMPLETE (validation + tests + documentation; no production code change)
Branch: `development/service-authority`
Base: `feat(prefixes): support country-scoped routing datasets` (Phase 35.3)

## 1. Goal

Prove and harden real country-policy transitions using the generic country
prefix pipeline introduced in Phase 35.3. This phase is about **behavior and
acceptance**, not another abstraction rewrite. No production defect was found;
the Phase 35.3 implementation already satisfies the switching contract, so the
deliverable is validation, permanent acceptance tests, and this document.

## 2. Requested/effective country model

- `DesiredConfiguration.DirectCountryCode` is the **requested** country. It
  becomes effective immediately on config write (no deferred activation).
- The **effective prefix dataset** is whatever the per-country cache currently
  holds for the requested country, observed each cycle via the country
  resolver (`ObservePrefixesAsync` reads `prefixes/<CC>/ipv4-prefixes.txt`).
- No second persisted `EffectiveCountryCode` exists. There is no hidden state:
  when the requested country's cache is empty and the source is unavailable,
  prefixes are unobservable and reconciliation is **blocked** (fail-closed),
  leaving the current platform routes untouched.
- `OperationCoordinator` serializes country writes, prefix updates, and runtime
  cycles through a single `SemaphoreSlim(1,1)`, so no two can overlap.

## 3. Online switching

| Transition | Result |
|---|---|
| IR → IQ | IQ prefixes become desired; IR-only owned routes removed via the ownership-aware plan; endpoint / custom / external routes preserved; second cycle is a no-op. |
| IQ → RO | Symmetric; proves no IR-specific migration path (baseline seeded from IQ, not IR). |
| RO → IR | Reuses the existing IR-scoped cache when fresh; legacy migration does not re-run; RO cache remains on disk; metadata/history stay country-isolated. |

## 4. Offline / failed switching

- **Offline switch with valid target-country cache (last-known-good):** the
  switch completes from the validated target-country last-known-good dataset.
  No IR-cache fallback, no cross-country fallback. Metadata/history remain
  target-country scoped.
- **Failed switch, no target cache (hardest case):** config = IQ, source
  unavailable, no IQ cache → `EnsurePrefixesAsync`/`UpdatePrefixesAsync` fails
  before enabling; the periodic cycle observes empty prefixes → `PrefixesUnavailable`
  blocker → reconciliation **Blocked** → **zero** remove/add steps → existing IR
  routes, endpoint, custom and external routes all remain. Host stays alive;
  repeated cycles are idempotently blocked.
- **Wrong-country-only cache:** selecting IQ while only IR and RO caches exist
  (source down) → no IR/RO dataset is consumed; reconciliation blocked; no route
  mutation from wrong-country data. Hard acceptance gate satisfied.
- **Empty / invalid target cache:** a cache file that exists but is empty is not
  treated as authoritative; prefixes unavailable → blocked; existing routes not
  destructively emptied. "File exists" never implies "dataset valid."

## 5. Persistence / cache isolation

`prefixes/IR/`, `prefixes/IQ/`, `prefixes/RO/` each hold `ipv4-prefixes.txt`,
`metadata.json`, `update-history.json` independently. Verified:

- IR metadata does **not** suppress an IQ fetch (per-country `ETag`/`hash`
  compared only within the same country's document).
- IQ metadata does **not** suppress an RO fetch.
- Returning to IR uses IR metadata only.
- No "latest country" global metadata.

## 6. Restart behavior (Step 12)

Simulated by constructing a brand-new controller/inventory/journal over the
**same** persisted directory (a faithful process restart; native OS routes
survive a reboot, modeled by restoring the in-memory route table from the
persisted ownership inventories before the first cycle):

- **A.** config IR→IQ + IQ cache present, before reconciliation → after restart
  the IQ routes remain and the first cycle is `NoChangesRequired` (idempotent,
  no ownership loss, no destructive replay).
- **B.** config IR→IQ + IQ acquisition failed (no IQ cache, source down) →
  after restart reconciliation is `Blocked`; IR routes preserved.
- Startup ordering proven: DesiredConfiguration load → country-scoped prefix
  selection → normal reconciliation.

## 7. Crash-consistent mutation (Step 13)

Using the Phase 34.2 journal against the real `RouteMutationRecovery`, with
pending intents written directly:

- **Crash during new-route add (IR→IQ):** native IQ route present, inventory
  not yet committed → pending Add intent → recovery **adopts** the native route
  into ownership inventory and clears the intent. Geography-neutral: the intent
  carries only bounded identity fields, never a country name.
- **Crash during old-route removal (IR→IQ):** native IR route already removed,
  inventory still lists it as owned → pending Delete intent → recovery
  **completes the delete** and clears the intent.

Both converge to a consistent state and leave an empty journal. This proves
Phase 34.2 remains geography-neutral.

## 8. Endpoint / custom / external preservation

- **Endpoint (VPN host) route:** stays direct; ownership unaffected by country
  switching; rotation around a switch reconciles normally (no country branching
  in endpoint code).
- **Custom route:** preserved with existing precedence; country switching does
  not absorb or replace endpoint ownership semantics.
- **External route (identical to old/current/new country, different metric or
  gateway/interface):** IranDirect only removes routes it owns/proves through
  the ownership mechanism. Country switching is not an excuse to broadly delete
  matching native routes.

## 9. Enable/disable interaction

- `Disabled` + `CountryCode=IQ` → no reconciliation of prefix routes; standard
  teardown still removes owned routes normally (country change did **not**
  implicitly toggle `Enabled`).
- `Enable` with valid IQ data → IQ reconciliation.
- `Enable` with unavailable IQ data / no cache → enable fails or stays safely
  blocked (fail-closed); no destructive route changes.
- Changing country does **not** implicitly toggle `Enabled`.

## 10. Refresh / concurrency (Step 20)

`OperationCoordinator` serializes country write, prefix update, and runtime
cycle. Proven by a two-country fetch test that records each result to its own
scope; no cross-contamination is possible because the coordinator admits one
mutating operation at a time.

## 11. Source race / stale-response (Steps 21–22)

- Every `ICountryPrefixSource.FetchAsync(country)` carries the explicit
  `CountryCode`; persistence (`CountryPrefixStore.SavePrefixesAsync(country, …)`)
  uses that same country, **never** "current config at save time." Therefore a
  fetch requested for IR that completes after config has become IQ still lands
  in `prefixes/IR`, never IQ. Same for IQ→RO.
- Because `OperationCoordinator` prevents two fetches being in flight
  concurrently, the stale-response scenario (IR completes last after IQ was
  requested) cannot strand global metadata: each result persists only to its
  own country scope.

## 12. Cache validity / last-known-good (Step 23)

A valid cached dataset is: same country, successfully parsed, non-empty, all
accepted entries valid IPv4 CIDRs, and written only after successful validation.
No age/TTL is applied — the existing system has no expiration; this phase does
not invent one.

## 13. Error semantics (Step 24)

Bounded, no raw response bodies, no route/prefix lists in logs/errors, no new
telemetry dimensions. Country dataset unavailable/invalid/target-cache-absent/
source-timeout all resolve to the fail-closed `Blocked` reconciliation with the
existing error surface.

## 14. Diagnostics / support (Step 25)

`RuntimeSnapshot.PrefixSource` is pulled for
`configuration.DirectCountryCode ?? IR`, so when config says IQ the support
output shows **IQ metadata**, never stale IR metadata. Verified by a test that
records IR and IQ metadata then requests IQ and asserts the snapshot reflects IQ.

## 15. Idempotency

Every transition is followed by a second cycle that produces no further changes
(`NoChangesRequired`), exercised across IR→IQ, IQ→RO, RO→IR, offline-with-cache,
and post-restart.

## 16. Performance (Step 28)

No new benchmark was required: the planner/executor are country-neutral and the
existing planner benchmark capacity covers switch-like workloads. No regression
was observed (Core.Tests 2254, Service.Tests 50, Stress 12 all green; Benchmark
Release builds clean).

## 17. Architectural isolation (Step 29)

No geography branching appears in: `RuntimeChangeSetPlanner`,
`RuntimeExecutor`, `WindowsRuntimeExecutionStepHandler`, `RouteMutationJournal`,
`RouteMutationRecovery`, `ManagedRoute`, endpoint protection, or the custom-route
subsystem. Country-dependent behavior stays in configuration, prefix
acquisition/cache, and desired-prefix construction — exactly where Phase 35.3
placed it.

## 18. Legacy IR migration interaction (Step 18)

- **A.** Selected IR, legacy `iran-ipv4-prefixes.txt` present, no new IR cache →
  legacy migration succeeds into `prefixes/IR/`.
- **B.** Selected IQ, legacy IR cache present → legacy IR cache is ignored;
  IQ builds from its own scoped cache; legacy file remains on disk (not deleted,
  not migrated into IQ scope).
- **C.** Switch IQ→IR later → migration can occur then if needed.
- The legacy file is never deleted before a successful migration.

## 19. RIPEstat commercial-use note (Step 31)

`ICountryPrefixSource` keeps replacement bounded. RIPEstat is currently the
generic provider; commercial use requires a service-terms review with RIPE NCC.
This is a follow-up, not in scope for 35.4. No source redesign.

## 20. Remaining limitations / risks

- The production path has **no automated periodic prefix refresh** in the worker:
  `UpdatePrefixesAsync` is triggered by `EnableAsync` (ensure) and the IPC
  `update-prefixes` command. An online IR→IQ switch therefore requires a prefix
  fetch to be triggered (enable or IPC) before the next cycle observes the new
  country. This is existing behavior, not a defect introduced here, and the
  fail-closed blocker protects the gap. Recommended for Phase 35.5 UX or a
  config-change → prefix-refresh trigger.
- Tests are deterministic against in-memory fakes; no real Windows route mutation
  and no real RIPEstat egress (per Phase 35.4 constraints).

## 21. Phase 35.5 scope (recommended)

- Country selection UX: CLI get/set, Tray dropdown, IPC `country get`/`country
  set` (currently only test helpers manipulate `DesiredConfiguration`).
- Optionally a config-change → prefix-refresh trigger to close the online-switch
  gap noted above.
- SaaS/cloud provider abstraction behind `ICountryPrefixSource` (bounded; RIPEstat
  commercial-terms review first).
- IPv6, multi-country policy, and brand rename (PathVeer) remain explicitly out
  of scope.

## 22. Verification evidence

- `dotnet test IranDirect.Core.Tests` → **2254 passed** (includes 19 new
  `CountrySwitchingAcceptanceTests`).
- `dotnet test IranDirect.Service.Tests` → **50 passed**.
- `Category=Stress` (Core) → **12 passed**.
- `dotnet build IranDirect.Benchmarks -c Release` → clean (1 pre-existing
  unrelated nullability warning in `DirectCountryCode.cs:51`).
- New test class: `IranDirect.Core.Tests/Globalization/CountrySwitchingAcceptanceTests.cs`
  covers Steps 5–13, 17–22, 25, and the repeat/idempotency matrix.
