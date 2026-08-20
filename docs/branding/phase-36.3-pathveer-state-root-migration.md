# Phase 36.3 — PathVeer Persistent State-Root Migration

**Branch:** `development/service-authority`
**Base:** `9af6967` (Phase 36.2 committed)
**Repository root (unchanged):** `C:\codespace\irandirect`
**Solution (unchanged):** `PathVeer.slnx`

Goal: migrate the active persistent state root from `%ProgramData%\IranDirect`
to `%ProgramData%\PathVeer` using a crash-safe, idempotent, single-authority
**copy → verify → publish** procedure, preserving every byte of existing
installation state. No other compatibility identity changes.

---

## 1. Old / new state root
- OLD (legacy): `%ProgramData%\IranDirect`  (frozen legacy identifier)
- NEW (authoritative): `%ProgramData%\PathVeer`

## 2. Repository / workspace root — unchanged
The agent workspace and Git checkout remain `C:\codespace\irandirect`.
Phase 36.3 changes **only** the application runtime persistent state under
`%ProgramData%`. The repository was not renamed, moved, cloned, or copied.

## 3. Production components added
- `PathVeer.Core/State/StateRootResolver.cs`
  - Single source of truth for the three root concepts:
    - `LegacyRoot` = `%ProgramData%\IranDirect`
    - `CurrentRoot` = `%ProgramData%\PathVeer`
    - `CreateTempMigrationRoot()` = `%ProgramData%\PathVeer.migrating-<guid>`
  - `ListTempMigrationRoots()` enumerates abandoned `.migrating-*` dirs.
- `PathVeer.Core/State/StateRootMigrator.cs`
  - `EnsureCurrentRoot()` implements the decision table.
  - `Migrate()` = copy → verify (length + SHA-256 content hash) → publish
    (atomic `Directory.Move` of the completed temp dir onto the final root).
  - `IStateRootMigrationProbe` seam for failure injection + minimal observability.
  - On failure: legacy preserved, partial temp discarded, nothing published.

## 4. Wiring changes (narrow, production code)
- `PathVeer.Service/Program.cs`: now resolves the root via
  `StateRootResolver` + runs `StateRootMigrator.EnsureCurrentRoot()` **before**
  any store is constructed, so the composition root only ever sees the single
  authoritative PathVeer root. (Replaces the hardcoded
  `Path.Combine(CommonApplicationData, "IranDirect")`.)
- `PathVeer.Core/Runtime/Profiling/RuntimePerfReportStore.cs`:
  `DefaultDirectory` now resolves through `StateRootResolver.ResolveCurrentRoot()`
  (`%ProgramData%\PathVeer\perf`) instead of the hardcoded `"IranDirect"`.

No routing / planner / executor / journal / IPC / telemetry / service-identity
change.

## 5. Migration decision table (implemented in `EnsureCurrentRoot`)
| Case | Legacy | Current | Behavior |
|------|--------|---------|----------|
| 1 | absent | absent | Fresh PathVeer install — no migration; missing config stays unconfigured (Phase 34.4) |
| 2 | exists | absent | Migrate legacy → verify → publish PathVeer; retain legacy |
| 3 | exists | valid | **PathVeer wins** — no overwrite, no re-migrate |
| 4 | absent | exists | Normal PathVeer startup |
| 5 | exists | temp only | Temp = incomplete → discarded; migrate fresh from legacy |
| 6 | exists | final + temp | Final wins; temp non-authoritative; legacy non-authoritative |
| 7 | (migration fails) | — | Legacy preserved; partial state never published; reconciliation blocked |

## 6. Copy algorithm
Recursive, structural copy that preserves the directory tree exactly (including
`prefixes/IR`, `prefixes/IQ`, `prefixes/RO`, `prefixes/US`, `prefixes/BR`, …).
No file content is rewritten; `File.Copy(overwrite:false)`. Empty legacy roots
are valid (fresh-like) migration targets.

## 7. Verification algorithm
- File count of source == destination.
- Every source file present in destination.
- Per-file length equality.
- Per-file SHA-256 content-hash equality (no rewrites).
- Expected authoritative top-level files (`desired-configuration.json`,
  `route-inventory.json`, `route-mutation-journal.json`, `state.json`,
  `custom-routes.json`, `custom-route-dns-cache.json`, `vpn-profile.ovpn`) that
  existed in the source must exist in the copy.

## 8. Publication boundary
The **only** moment the new root becomes authoritative is the atomic
`Directory.Move(tempRoot, finalRoot)`. Before that, only `tempRoot`
(non-authoritative) exists. On crash before the move, `tempRoot` is never
treated as the final root; a restart re-detects legacy and re-migrates into a
fresh temp. After the move succeeds, `CurrentExists == true` and
`EnsureCurrentRoot()` short-circuits to "current already authoritative".

## 9. Partial-copy crash behavior
A partially copied `PathVeer.migrating-<guid>` directory is **never** read as
state. It is either completed-and-published (becoming the final root) or, on any
failure, deleted by `TryDeleteTemp`. On restart, stale `.migrating-*` dirs are
discarded at the start of `Migrate()` before a new attempt.

## 10. Restart behavior (crash matrix)
- A. before temp dir: `Migrate` starts normally; legacy intact.
- B. temp created, copy not started: temp discarded/overwritten by fresh attempt.
- C. partial copy: temp not published; discarded; re-migrate.
- D. copied, verify not done: temp not published; discarded; re-migrate.
- E. verified, publish not done: temp not published; re-migrate (creates new temp).
- F. after successful publish, legacy still present: `CurrentExists` → current wins.

All restart paths converge safely; legacy is never mutated or deleted.

## 11. Existing-PathVeer-wins rule
If `%ProgramData%\PathVeer` exists (valid or even empty), `EnsureCurrentRoot`
returns `CurrentAlreadyAuthoritative` and never copies legacy over it. Covers:
migration already done, restart, upgrade re-run, stale legacy, rollback
artifacts. Verified by `ExistingPathVeerRoot_Wins_LegacyNotOverwritten` and
`ExistingNewRoot_StaleLegacy_Ignored`.

## 12. Single-authority proof
Post-migration, every store is constructed from `resolver.CurrentRoot` only.
No code path reads or writes `%ProgramData%\IranDirect` after migration. The
legacy root is migration **input**, never runtime fallback storage. Verified by
`AfterMigration_NoWritesToLegacyRoot` (legacy LastWriteTime unchanged across
re-runs) and `FailureAtBoundary` tests (no partial publish).

## 13. Legacy-root retention
The legacy `%ProgramData%\IranDirect` is deliberately **not** deleted in this
phase (rollback evidence). `TryDeleteTemp` only removes non-authoritative temp
dirs.

## 14–24. Behavioral guarantees (per spec)
- **DesiredConfiguration**: migrated verbatim; missing config stays unconfigured;
  corrupt config is not silently "repaired" (copy preserves bytes; store
  validation at load fails closed as before).
- **RouteInventory / VpnEndpointInventory**: copied byte-for-byte; ownership
  preserved; no reinterpretation as external.
- **RouteMutationJournal**: copied; recovery reads the migrated root before
  reconciliation (proven by `JournalRecovery_ResolvesFromCurrentRoot`).
- **Country prefix caches**: `prefixes/{ISO2}` preserved & isolated per country;
  no IR special-casing; metadata/history per-country isolated.
- **Legacy `iran-ipv4-prefixes.txt`**: copied intact; `CountryPrefixStore`
  legacy migration logic unchanged.
- **Custom routes / DNS cache**: copied verbatim.
- **Enabled installation**: Enabled + DirectCountryCode preserved (IR/IQ/RO).
- **Disabled installation**: remains disabled; never implicitly enabled.
- **Offline migration**: pure filesystem, zero network calls (proven by
  `OfflineMigration_NoNetworkRequired`).
- **Corrupt state**: copy preserves bytes; the existing store contracts fail
  closed on load; migration does not rewrite/zero corruption.
- **ACL/permissions**: new `%ProgramData%\PathVeer` is created by the same
  process/Windows Service identity (`IranDirect` service) that created
  `%ProgramData%\IranDirect`; `Directory.CreateDirectory` inherits the parent
  `%ProgramData%` ACL. No permission broadening; no Everyone/Users write added.

## 25. Frozen identifiers — UNCHANGED (proven by `FrozenIdentifiers_Unchanged`)
| Identifier | Value |
|------------|-------|
| Named pipe | `IranDirect.Control.v1` |
| ServiceName | `IranDirect` |
| DisplayName | `IranDirect Service` |
| Telemetry source/meter | `IranDirect.Core` |
| service.name | `IranDirect.Service` |
| Metrics | `irandirect_*` (deployment YAML, untouched) |
| Legacy prefix file | `iran-ipv4-prefixes.txt` |

The `StateRootResolver` constants themselves make the legacy name explicit and
frozen: `LegacyStateDirectoryName = "IranDirect"`,
`CurrentStateDirectoryName = "PathVeer"`.

## 26. Testing
`PathVeer.Core.Tests/State/StateRootMigrationTests.cs` — 25 tests covering spec
§31 A–Z: fresh install, legacy-only, existing-wins, idempotency, partial-temp
never-authoritative, interrupted restart, enabled IR/IQ/RO, disabled non-IR,
multi-country caches, metadata/history isolation, legacy prefix file, inventory
preservation, pending ADD/DELETE journal, recovery-from-current-root,
external-route safety, custom-route preservation, offline, missing config,
corrupt handling path, existing-new+stale-legacy, no-legacy-writes, frozen
identifiers, plus failure-injection at before-copy / before-verify /
before-publish boundaries.

## 27. Regression results (fresh, Phase 36.3 tree)
- `dotnet build PathVeer.slnx -c Debug`: succeeded, 104 warnings, 0 errors.
- Full Core: 2365 passed (baseline 2365).
- Service: 50 passed.
- Stress (`Category=Stress`): 12 passed.
- Benchmark Release: clean (2 warnings).
- Focused IPC/DI/Config/RouteMutation/Country/Globalization/Observability: 599 passed.

## 28. Exact files changed
- ADDED `PathVeer.Core/State/StateRootResolver.cs`
- ADDED `PathVeer.Core/State/StateRootMigrator.cs`
- ADDED `PathVeer.Core.Tests/State/StateRootMigrationTests.cs`
- MODIFIED `PathVeer.Service/Program.cs`
- MODIFIED `PathVeer.Core/Runtime/Profiling/RuntimePerfReportStore.cs`
- REMOVED empty leftover `PathVeer.Core/IranDirect.Core/` (untracked artifact from 36.2)
- ADDED `docs/branding/phase-36.3-pathveer-state-root-migration.md`
- MODIFIED `AI-START-HERE.md` (+1 nav line)

## 29. Scope proof
Only the persistent state-root resolution + migration + minimal wiring changed.
No routing/planner/executor/journal/IPC/telemetry/service-identity change.
`git diff --check` clean. Frozen identifiers asserted by tests.

## 30. Remaining risks
- (a) A **concurrent** legacy IranDirect Service still running during upgrade
  could hold handles on `%ProgramData%\IranDirect`; migration only copies
  (read-only) so it is safe, but the legacy service should be stopped before the
  PathVeer service installs (Phase 36.7 installer concern).
- (b) Migration runs at every startup when legacy exists & current absent;
  since it is idempotent and only copies when current is absent, repeated runs
  are cheap and safe.

## 31. Rollback boundary
Legacy `%ProgramData%\IranDirect` is retained. Minimum safe rollback target
remains the final globalized IranDirect build (post-Phase 35.7). Rolling back
the binary to that build reads the legacy root directly. Compatibility with
pre-35.7 binaries is not guaranteed.

## 32. Recommended Phase 36.4 entry point
External runtime identity migration: Windows Service identity
(`IranDirect`/`IranDirect Service` → `PathVeer`/`PathVeer Service`),
`PathVeer.Control.v1` named pipe with temporary `IranDirect.Control.v1`
compatibility, new-client→old-service + old-client→new-service fallback, and
single-service-authority upgrade behavior. State root stays `%ProgramData%\PathVeer`.

## 33. Commit message
`feat(branding): migrate persistent state root to PathVeer`
