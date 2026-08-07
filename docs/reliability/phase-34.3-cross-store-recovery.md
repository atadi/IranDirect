# Phase 34.3 — Cross-Store Crash Consistency and Recovery

Status: COMPLETE (analysis + permanent locking tests; no production code change required)
Branch: development/service-authority
Precedes: Phase 34.2 (crash-consistent route mutation recovery)
Recommended commit: `docs(reliability): validate cross-store recovery model`

## 1. Preconditions (met)
- Clean working tree, branch `development/service-authority`, HEAD `cf8a598`
  `fix(runtime): make route ownership crash-consistent` (Phase 34.2 committed).

## 2. Persistent store inventory

| Store | Path (data dir) | Model | Load missing | Load corrupt | Save | Lock | Atomic | Schema/ver |
|-------|----------------|-------|--------------|--------------|------|------|--------|------------|
| RouteInventoryStore | `route-inventory.json` | `RouteInventory` | `new()` (silent) | throw (JsonException) | tmp+`File.Move` | per-instance `SemaphoreSlim(1,1)` | yes | none |
| VpnEndpointInventoryStore | `vpn-endpoint-inventory.json` | `VpnEndpointInventory` | `new()` (silent) | throw | tmp+`File.Move` | per-instance mutex | yes | none |
| DesiredConfigurationStore | `desired-configuration.json` | `DesiredConfiguration` | `new()` (silent) → validator → **disabled default passes** | throw (JsonException) | tmp+`File.Move` | n/a | yes | `SchemaVersion=1` validated |
| CustomRouteStore | `custom-routes.json` | `CustomRouteCollection` | `new()` (silent) | throw | tmp+`File.Move` | per-instance mutex | yes | none |
| CustomRouteDnsCacheStore | `custom-route-dns-cache.json` | `CustomRouteDnsCacheCollection` | `new()` (silent) | throw | tmp+`File.Move` | n/a | yes | none |
| PrefixSourceMetadataStore | `prefix-source-metadata.json` | `PrefixSourceMetadataDocument` | `new()` (silent) | throw | tmp+`File.Move` | n/a | yes | none |
| PrefixSourceUpdateHistoryStore | `prefix-source-update-history.json` | `PrefixSourceUpdateHistoryDocument` | `new()` (silent) | throw | tmp+`File.Move` | n/a | yes | none |
| StateRepository | `state.json` | `IranDirectState` | `new()` (silent) | throw | tmp+`File.Move` | n/a | yes | none |
| PrefixFileRepository | prefix list file | `IReadOnlyList<string>` | `new()` (silent) | throw | tmp+`File.Move` | n/a | yes | none |
| RouteMutationJournalStore | `route-mutation-journal.json` | `Dictionary<id,Entry>` | empty journal (valid) | **throw** `RouteMutationJournalCorruptException` | tmp+`File.Move` | per-instance mutex | yes | `SchemaVersion=1` |

Shared base: `JsonStore<T>` — missing file → `new T()`; corrupt content →
`JsonException` propagates (NOT silently reset to default). The journal store is
the only store that throws its OWN exception type on corrupt content (by design,
to protect ownership evidence). Each store has its own mutex; there is no
cross-store lock.

## 3. Multi-store workflows (source-mapped)

1. **Route mutation / reconciliation (executor).** Journal `WriteIntent` →
   native route action → inventory `MutateAsync` (RouteInventory or
   VpnEndpointInventory) → journal `ClearIntent`. Closed by the Phase 34.2
   journal. This is the ONLY pair where a crash window can produce incorrect
   ownership/routing (native-present + inventory-missing → orphan).
2. **Cycle completion (`IranDirectController`, ~line 628-683).** `StateRepository
   .SaveAsync(state)` always; and when `!Enabled && execution.MutatedInfrastructure`
   → `RouteInventoryStore.ClearAsync()`. Two stores written sequentially.
3. **Prefix update (`IranDirectController`, ~line 114-145).** `PrefixFileRepository
   .SaveAsync` → `PrefixSourceMetadataRepository.SaveAsync` →
   `PrefixSourceUpdateHistoryRepository.SaveAsync` → `StateRepository.SaveAsync`.
   Four stores, sequential.
4. **Custom route edit (`CustomRouteService`).** `CustomRouteRepository.MutateAsync`
   only. DNS cache is written separately by `CustomRouteDnsCacheRepository` during
   refresh — never in the same operation as the custom-route collection.
5. **Desired configuration update (`DesiredConfigurationService`).** Single store
   write; the service re-plans from the new config.
6. **VPN endpoint protection/rotation.** Shares workflow (1) — journal covers the
   native↔inventory window for endpoints too.

## 4. Authoritative / derived classification

- **A. Must never be guessed (authoritative desired state):**
  DesiredConfiguration, PrefixFileRepository (desired prefix list),
  CustomRouteStore (desired custom routes).
- **A/B. Ownership proof (authoritative, journal-protected):**
  RouteInventory, VpnEndpointInventory, RouteMutationJournal.
- **B. Rebuildable cache:** CustomRouteDnsCacheStore (TTL-based, never a routing
  input; rebuilt by refresh).
- **C. Diagnostic / cache-like:** PrefixSourceMetadataStore,
  PrefixSourceUpdateHistoryStore.
- **D. Diagnostic status (derived; rebuilt each cycle, never a planning input):**
  StateRepository.

No store is "merely diagnostic metadata" in a way that affects routing decisions;
`StateRepository` is read only by diagnostics and the controller's end-of-cycle
status writer, NEVER by the reconciler/planner.

## 5. Proven divergence windows

- **W1 (ownership, native↔inventory).** Native route added, inventory persist
  interrupted. → orphan route. **Closed by Phase 34.2 journal** (recovery adopts
  only journal-proven entries). Verified by Phase 34.2 tests
  (`RouteMutationRecoveryTests`, `RouteMutationStepHandlerJournalTests`).
- **W2 (StateRepository ↔ RouteInventory clear).** Crash between
  `StateRepository.SaveAsync` (Enabled=false) and `RouteInventoryStore.ClearAsync`
  leaves stale RouteInventory entries when disabled. **Self-heals:** on the next
  cycle the reconciler (`RuntimeChangeSetPlanner`) compares DESIRED vs OBSERVED
  platform routes, not inventory ownership, so a desired-but-platform-absent route
  is re-added regardless of stale inventory; owned-but-undesired routes are removed
  by the reconciliation removal pass. No transaction needed.
- **W3 (prefix metadata/history/state).** Crash between the 4 prefix-update stores
  leaves one stale. All are diagnostic/cache; rebuilt by the next fetch. No routing
  impact.

No divergence window produces destructive routing behavior or a permissive config.

## 6. Phase 34.2 journal interaction

The journal participates only in workflow (1). Recovery reads journal + live
platform snapshot + inventory and acts strictly on journal-proven intents. It does
NOT touch DesiredConfiguration, StateRepository, DNS cache, or prefix metadata, and
it cannot create new divergence with endpoint inventory (endpoint intents are
journal-proven too). Verified: `RouteMutationRecovery` takes only
`IRouteManager, IRouteInventoryPersistence, IEndpointInventoryPersistence,
IRouteMutationJournal` — no config/cache dependency. No modification to the journal
was required.

## 7. Recovery strategy per proven divergence

| Divergence | Strategy | Rationale |
|-----------|----------|-----------|
| W1 native↔inventory | B (rebuild/resolve from authoritative source = the durable journal intent + live platform) | Journal is the minimal per-op transaction |
| W2 State↔RouteInventory clear | C (reconcile on startup via planner) | Planner uses platform truth; stale inventory self-heals |
| W3 prefix/state stores | E (disposable/derived; safe to ignore) | Diagnostic only, rebuilt each cycle |
| Corrupt ownership store (journal intact) | D (fail closed on corrupt; recovery defers the intent, does not guess) | `RouteMutationJournalStore` throws on corrupt |
| Corrupt cache/metadata | D (fail closed: throw) or C (missing → silent empty) | Never becomes authoritative |

**No general cross-store transaction coordinator is warranted.**

## 8. Hard safety rule (verified by tests)

Never use absence in one store as proof of ownership in another:
- `RouteMutationRecovery` only adopts a route when a durable journal intent exists
  AND the platform route is present. External routes (present, no intent) are
  left untouched (test: `Recovery_ExternalRoutePresent_NoJournal_NoStoreTouched`).
- A corrupt ownership store does not cause destructive mutation; recovery defers
  and continues (Phase 34.2 hardening).
- Tests `Recovery_RouteAndEndpointDivergence_ResolvesOnlyJournaled` and
  `Recovery_CorruptEndpointInventoryModel_AdoptsOnlyJournalProven` prove recovery
  touches only journal-proven entries and never cross-contaminates stores.

## 9. DesiredConfiguration crash behavior (finding R3-class, NOT changed)

- Missing file → `JsonStore` returns `new DesiredConfiguration()` silently. The
  default has `VpnProfilePath = "vpn-profile.ovpn"` (non-empty) and `Enabled=false`,
  so `DesiredConfigurationValidator.ValidateAndThrow` PASSES → the service runs
  **disabled**, not permissive. Routing is unaffected (no destructive action).
- Corrupt content → `JsonException` propagates → `DesiredConfigurationStore
  .LoadAsync` throws → `IranDirectWorker` calls `GetAsync` unguarded → the service
  **fails closed** (does not start).
- **Asymmetry:** missing = silent disabled (fail-open-to-disabled); corrupt = throw
  (fail closed). The missing-file case masks a deployment error but is routing-safe
  (disabled = no routes managed). It is NOT a cross-store routing hazard, so per
  §9 it is documented as an R3 follow-up rather than expanded here. Locked by test
  `DesiredConfiguration_MissingFile_SilentlyDisabled_NotPermissive` (asserts NOT
  permissive and never becomes the user's config).

## 10. VPN endpoint inventory consistency

`RouteMutationRecovery` handles endpoint add/remove windows identically to prefix
routes (workflow 1, journal-covered). Native endpoint present + inventory missing
→ adopted only if journal intent exists; inventory claims endpoint but native
absent → intent left pending for normal reconciliation. Endpoint routes are never
deleted solely because inventory changed without ownership proof. Verified by
`Recovery_RouteAndEndpointDivergence_ResolvesOnlyJournaled` and
`Recovery_CorruptEndpointInventoryModel_AdoptsOnlyJournalProven`.

## 11. Cache-like stores

`CustomRouteDnsCacheStore` and `PrefixSource*Store` are rebuildable (B/C). Proof:
- Corrupt content throws (`JsonStore` does not silently reset) → cannot become
  authoritative. Locked by `DnsCache_CorruptFile_FailsClosed_NotAuthoritative` and
  `PrefixMetadata_CorruptFile_FailsClosed_NotAuthoritative`.
- Missing content silently resets to empty → rebuilt by the next refresh/fetch.
- These stores are never read by `RouteMutationRecovery` or the reconciler, so
  their state cannot drive a destructive routing mutation.

## 12. Corruption interaction

Mixed valid/corrupt states tested:
- `Recovery_MixedValidAndCorruptOwnership_NoCrossContamination` — consistent store
  left untouched, no destructive action, when nothing is pending.
- Corrupt cache + valid authoritative store → recovery ignores the cache entirely.
- Corrupt ownership store + valid journal → recovery defers (Phase 34.2); does not
  wipe or guess.

Startup exposes deterministic failure (throw) for corrupt authoritative/cache
content; missing files degrade safely (disabled / empty). No silent wipe of
ownership evidence.

## 13. Startup ordering (after Phase 34.2)

1. Mutation-journal recovery (`RouteMutationRecovery`) via
   `OperationCoordinator.ExecuteAsync` — runs BEFORE the first cycle and is gated
   so it cannot race an IPC command or startup cycle.
2. `DesiredConfiguration` load (independent of recovery; if corrupt → fails closed).
3. Ownership stores load (read by the reconciler).
4. Derived/cache rebuild as needed (prefix fetch, DNS refresh) during normal
   operation.
5. Normal reconciliation/planning last.
Locked by `Startup_JournalRecoveryRunsBeforeCycle_LeavesConsistentState` and the
Phase 34.2 worker wiring. Planning cannot run against an unresolved critical
divergence because the journal recovery completes first and the only critical
pair (native↔inventory) is reconciled before the first cycle.

## 14. Cross-store recovery owner

Not required. `RouteMutationRecovery` is the narrow owner for the only
safety-critical pair (native route ↔ ownership inventory), invoked once at startup
via `OperationCoordinator`. A `RuntimeStateRecoveryCoordinator` was NOT added:
there is no other critical divergence, and absorbing normal business logic into a
coordinator is explicitly out of scope.

## 15. Transaction design

Not required. The Phase 34.2 write-ahead journal is a single-operation intention
record (schema version, kind, inventory kind, identity, bounded payload, atomic
persist, idempotent recovery) for the ONLY store pair where a crash window produces
incorrect routing. Introducing a two-phase or distributed transaction coordinator
would be disproportionate to the proven risk.

## 16. Failure matrix (per recovery path, via existing + new tests)

Crash/fault positions tested (no process-kill, no sleeps, deterministic fakes):
- Before first write: nothing persisted → recovery no-op.
- After native add, before inventory persist: journal intent present → adopt.
- After inventory persist, before journal clear: committed → journal cleared.
- Journal clear interrupted: next recovery re-evaluates (Case C) → clears.
Recovery run twice (idempotency) in `Recovery_Idempotent_AcrossTwoRuns` and the
Phase 34.2 suite. Deterministic final state, no destructive action without proof.

## 17. Existing behavior preservation

Normal startup unchanged (recovery is a no-op when consistent:
`Recovery_ConsistentState_NoOp`). Route reconciliation, endpoint protection,
enable/disable, desired-config behavior, DNS/prefix cache paths unchanged — only
new tests were added; no production code modified. Regression suites (step handler,
executor, reconciler, compensation) remain green.

## 18. Tests added

`IranDirect.Core.Tests/Runtime/Execution/CrossStoreStateRecoveryTests.cs` (12 tests):
- DesiredConfiguration fail-closed (corrupt) + silent-disabled (missing).
- DNS cache / prefix metadata corrupt → fail closed (not authoritative).
- Route+endpoint divergence resolves only journaled entries.
- Corrupt endpoint inventory adopts only journal-proven.
- External route present + no journal → untouched.
- Mixed valid/corrupt ownership → no cross-contamination.
- Idempotent across two runs.
- Consistent state → no-op.
- Startup journal recovery before cycle.

## 19. Performance

No production code added; recovery overhead is unchanged from Phase 34.2 (one
startup pass over the journal + a platform route snapshot). Consistent-state
startup: recovery is a no-op (empty/consistent journal → immediate return). No new
store reads/writes on the hot path. (Phase 34.2 measured allocation-stable; no
re-measurement needed since no allocation-bearing code was added.)

## 20. Verification

- `dotnet clean` + `dotnet build` (full solution, Debug): succeeded, 0 new
  compiler warnings from this phase (pre-existing xUnit analyzer warnings in OTHER
  test files only).
- Full Core suite: **2201 passed, 0 failed**.
- Focused (spec filter): **62 passed, 0 failed**.
- Stress category: **12 passed, 0 failed**.
- Benchmark project (Release): **build succeeded**.
- `git diff --check`: clean.

## 21. Scope verification

Production changes: NONE. Only one new test file added:
`IranDirect.Core.Tests/Runtime/Execution/CrossStoreStateRecoveryTests.cs`.
Planner semantics, telemetry contract, observability, IPC protocol, route identity
model, and config schema are all unchanged.

## 22. Decision (required)

**A. No — reconciliation + authoritative-source rules are sufficient.**
The Phase 34.2 write-ahead journal is the minimal, sufficient correction for the
only safety-critical store pair (native route ↔ ownership inventory). Every other
multi-store write involves at least one derived/diagnostic/cache store that is
rebuilt from authoritative sources each cycle and never drives routing decisions.
No general cross-store transaction coordinator is required.

## 23. Final report

1. Commit/branch: to-be `docs(reliability): validate cross-store recovery model` on
   `development/service-authority` (HEAD `cf8a598`).
2. Store inventory: §2. 3. Authoritative/derived: §4. 4. Multi-store map: §3.
   5. Proven windows: §5 (W1 closed by 34.2; W2/W3 self-heal). 6. Journal
   interaction: §6. 7. Strategy: §7. 8. DesiredConfiguration: §9 (R3 follow-up).
   9. Endpoint: §10. 10. Cache: §11. 11. Corruption: §12. 12. Startup: §13.
   13. Recovery owner: §14 (none needed). 14. Transaction: §15 (none needed).
   15. Failure matrix: §16. 16. Idempotency: §16 + `Recovery_Idempotent_AcrossTwoRuns`.
   17. External-route safety: §8 + `Recovery_ExternalRoutePresent_NoJournal_NoStoreTouched`.
   18. Perf: §19. 19. Tests: §18. 20. Full: 2201. 21. Focused: 62. 22. Stress: 12.
   23. Build: green, 0 new warnings. 24. Files: 1 new test file only. 25. Unrelated
   behavior unchanged (regression suites green). 26. **General transaction mechanism
   NOT needed.** 27. Remaining risk: R3 (missing DesiredConfiguration silently
   degrades to disabled rather than failing closed — routing-safe, tracked as
   follow-up). 28. Next slice: R3 (config-load fail-closed on missing file) or
   R2/R4+ from Phase 34.1 as prioritized. 29. Commit:
   `docs(reliability): validate cross-store recovery model`.
