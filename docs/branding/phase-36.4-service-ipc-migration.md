# Phase 36.4 — PathVeer Windows Service & IPC Identity Migration

**Branch:** `development/service-authority`
**Base:** `4d8797c` (Phase 36.3 committed & pushed)
**Date:** 2026-08-08
**Status:** IMPLEMENTED — staged, NOT committed (per user instruction)

---

## 1. Goal

Migrate the **external runtime identity** from `IranDirect` to `PathVeer` for:

1. **Windows Service identity** — `IranDirect` / `IranDirect Service` → `PathVeer` / `PathVeer Service`
2. **Named-pipe IPC identity** — primary `PathVeer.Control.v1`, with legacy `IranDirect.Control.v1` retained for a compatibility window

While preserving **one route-mutation authority**, old-client→new-service and new-client→old-service compatibility, IPC protocol compatibility, the `%ProgramData%\PathVeer` state root, route ownership, mutation-journal recovery, country routing, and telemetry compatibility (`IranDirect.Core`, `service.name=IranDirect.Service`, `irandirect_*`).

Explicitly **out of scope** for 36.4: telemetry rename (36.6), CLI/Tray UX redesign (36.5), installer packaging beyond the minimal testable service-migration abstraction (36.7), SaaS/cloud, IPv6.

---

## 2. Preconditions (verified)

- Branch `development/service-authority`, HEAD `4d8797c`, clean tree ✓
- `PathVeer.slnx` canonical ✓
- Active state-root implementation = `%ProgramData%\PathVeer` (Phase 36.3) ✓

---

## 3. Old vs New External Identity

| Aspect | Old (legacy, frozen as migration input) | New (active) |
|---|---|---|
| Windows Service name | `IranDirect` (`LegacyServiceNames.ServiceName`) | `PathVeer` (`PathVeerServiceNames.ServiceName`) |
| Display name | `IranDirect Service` | `PathVeer Service` |
| Host `ServiceName` (AddWindowsService) | `IranDirect Service` | `PathVeer Service` |
| Primary pipe | — | `PathVeer.Control.v1` (`PathVeerPipeNames.PrimaryPipeName`) |
| Legacy pipe | `IranDirect.Control.v1` | `IranDirect.Control.v1` (`PathVeerPipeNames.LegacyPipeName`, retained) |
| Telemetry `SourceName` | `IranDirect.Core` | `IranDirect.Core` (UNCHANGED — 36.6) |
| `service.name` | `IranDirect.Service` | `IranDirect.Service` (UNCHANGED — 36.6) |
| Metrics | `irandirect_*` | `irandirect_*` (UNCHANGED — 36.6) |
| State root | `%ProgramData%\IranDirect` (retained) | `%ProgramData%\PathVeer` (authoritative) |

---

## 4. Files Created / Renamed / Modified

### New files (production)
- `PathVeer.Core/Ipc/PathVeerPipeNames.cs` — `PrimaryPipeName`, `LegacyPipeName`, `AllListenNames`. Replaces `IranDirectPipeNames`.
- `PathVeer.Core/ServiceLifecycle/PathVeerServiceNames.cs` — `ServiceName`, `DisplayName`, `Description` (PathVeer-appropriate wording). Plus `LegacyServiceNames` (migration input only).
- `PathVeer.Core/Ipc/PathVeerNamedPipeClientFactory.cs` — client connect with **primary→legacy fallback at connect time** (safe). Replaces `NamedPipeClientFactory`.
- `PathVeer.Core/ServiceLifecycle/ServiceIdentityMigration.cs` — single-authority migration state machine (stop legacy → verify stopped → start PathVeer → guard) + rollback. Fully unit-testable via `IServiceControllerAdapter` fakes.
- `tools/Install-PathVeerService.ps1` — replaces `Install-IranDirectService.ps1`; stops legacy `IranDirect` during install (single-authority), publishes/starts `PathVeer.Service.exe`.

### Deleted files
- `PathVeer.Core/Ipc/IranDirectPipeNames.cs` (renamed → `PathVeerPipeNames`)
- `PathVeer.Core/Ipc/NamedPipeClientFactory.cs` (renamed → `PathVeerNamedPipeClientFactory`)
- `PathVeer.Core/ServiceLifecycle/IranDirectServiceNames.cs` (renamed → `PathVeerServiceNames`)
- `tools/Install-IranDirectService.ps1` (renamed → `Install-PathVeerService.ps1`)

### Modified files (production)
- `PathVeer.Service/Ipc/NamedPipeCommandServer.cs` — `RunAsync` now dual-listens on **both** `PathVeerPipeNames.AllListenNames`, each loop funnelling into the SAME `HandleOneClientAsync` → SAME command dispatch → SAME `OperationCoordinator`. One authority, two front doors.
- `PathVeer.Core/Ipc/IranDirectServiceClient.cs` — default factory now `PathVeerNamedPipeClientFactory` (primary→legacy connect fallback).
- `PathVeer.Core/ServiceLifecycle/WindowsServiceLifecycle.cs` — `sc create`/`description` use `PathVeerServiceNames.DisplayName`/`Description`; install-script reference updated.
- `PathVeer.Tray/TrayApplicationContext.cs` — service discovery uses `PathVeerServiceNames.ServiceName`.
- `PathVeer.Service/Program.cs` — `options.ServiceName = "PathVeer Service"`.
- `PathVeer.Cli/Program.cs` — usage strings reference `PathVeer.Cli` (binary identity now matches).

### Test files
- NEW `PathVeer.Core.Tests/ServiceLifecycle/ServiceIpcMigrationTests.cs` — 28 tests: service-identity constants, legacy-name retention, pipe identity, dual-listen-to-one-coordinator contract, client fallback (primary-preferred / legacy-fallback / no-send-on-either / no-fallback-after-write-fail / no-fallback-after-read-fail / valid-failure-no-fallback / both-unavailable), single-authority migration state machine (legacy-present / no-legacy / legacy-refuses-stop→throw-no-concurrency / already-running / rollback).
- MOD `PathVeer.Core.Tests/State/StateRootMigrationTests.cs` — freeze asserts now read `LegacyServiceNames.ServiceName` and `PathVeerPipeNames.LegacyPipeName` (still `"IranDirect"` / `"IranDirect.Control.v1"`).
- MOD `PathVeer.Core.Tests/ServiceLifecycle/WindowsServiceLifecycleTests.cs` — lifecycle tests now use `PathVeerServiceNames.ServiceName`.

### Docs
- NEW `docs/branding/phase-36.4-service-ipc-migration.md` (this file)
- MOD `AI-START-HERE.md` — +1 navigation entry

---

## 5. Single-Authority Rule (CRITICAL)

`IranDirect Service` and `PathVeer Service` MUST NEVER concurrently operate as the route-mutation authority.

`ServiceIdentityMigration.MigrateAsync` enforces this sequence:
1. Detect existing legacy service (`Exists()`).
2. If present and not stopped → `Stop(timeout)`.
3. **Verify stopped** — if still running, throw (abort; do NOT start PathVeer → no concurrency).
4. (Legacy stopped = prevented from restart for the transition window; installer must not re-enable it.)
5. If PathVeer not already running → `Start(timeout)`.
6. **Final guard** — if legacy is now running again, throw (single-authority violated; rollback recommended).

`Install-PathVeerService.ps1` mirrors this: `Stop-ServiceByName -Name IranDirect` before publish/configure/start of PathVeer.

No overlap is ever created; the contract is unit-tested with `DualAdapter` fakes (no real SCM touched).

---

## 6. Dual-Listen Design

```
PathVeer Service
      ├── PathVeer.Control.v1        (primary)
      └── IranDirect.Control.v1      (legacy compat)
                │
                ▼  both loops call HandleOneClientAsync
      same NamedPipeCommandServer dispatch
                │
                ▼
      same OperationCoordinator
                │
                ▼
      same controller / runtime authority
```

No duplicated command-processing logic, no second worker, no second reconciliation loop. The two pipes are transport aliases into one process.

---

## 7. Pipe ACL / Security

No pipe-ACL code was present before or after — `NamedPipeServerStream` is created with default platform ACLs (the same construction as the prior single-pipe implementation). The security model is therefore **unchanged**, not weakened. The legacy pipe retains its previous ACL; the new pipe receives the identical default. No permission broadening was introduced.

---

## 8. Client Fallback & Ambiguous-Send Protection

`PathVeerNamedPipeClientFactory.ConnectAsync` tries `AllListenNames` in order:
- **Primary** `PathVeer.Control.v1` first.
- If primary **cannot be opened** (connect throws) → try **legacy** `IranDirect.Control.v1`.

Fallback is performed **ONLY at connect time**. The decision boundary (spec §14–15):

| Phase | Condition | Fallback safe? | Behavior |
|---|---|---|---|
| A | Primary pipe cannot be opened | **YES** | Connect to legacy; no bytes were sent, so replay is exactly-once-safe. |
| B | Connected, command not sent (write throws before any response) | MAY be safe | Surface the failure; do NOT silently retry on the other pipe. |
| C | Command bytes sent, response unavailable | **NO** | Ambiguous outcome — do NOT replay a mutation command on the other pipe. |
| D | Valid failure response received | **NO** | Definitive outcome — do not fall back. |

Because a failed connect means nothing was transmitted, fallback at connect time can never cause a command to execute twice. This is pinned by tests `Client_PrimaryConnectFailure_DoesNotSendOnEither`, `Client_ConnectedButWriteFails_DoesNotFallback`, `Client_ConnectedButReadFails_DoesNotFallback`, `Client_ValidFailureResponse_DoesNotFallback`.

---

## 9. Old Service → New State-Root Interaction (mixed-version boundary)

Phase 36.3 made `%ProgramData%\PathVeer` authoritative and does **NOT** dual-write back to `%ProgramData%\IranDirect`. A historical `IranDirect.Service.exe` binary knows only `%ProgramData%\IranDirect`, so it **cannot** see PathVeer's latest state. Therefore:

- A running legacy service during the PathVeer window is **forbidden** (single-authority).
- Rollback to the legacy binary is only meaningful **before** any PathVeer-state-mutating command has executed. After PathVeer has written desired-config/country state, the legacy binary cannot observe it — rollback restores *service availability*, not *state parity*.
- `ServiceIdentityMigration.RollbackAsync` stops PathVeer and restarts legacy only if it exists; it does not reconstruct legacy state.

This boundary is documented (not hidden) and is a deliberate compatibility limitation of the staged upgrade.

---

## 10. Service Startup / Recovery Ordering

Unchanged from prior phases (verified in `IranDirectWorker`):
`StateRootResolver/StateRootMigrator` → `RouteMutationRecovery` → `DesiredConfiguration` gate → runtime worker → IPC availability.

The IPC server (`NamedPipeCommandServer`) is started as part of the worker and dual-listens both pipes. A half-initialized service is protected by the existing `DesiredConfiguration` gate: commands that require configuration are rejected with `CONFIGURATION_UNAVAILABLE` until recovery completes. No new broad readiness subsystem was invented.

---

## 11. Telemetry Freeze (PROOF)

Unchanged in 36.4 (renamed only cosmetically where a type name appeared, never the emitted identity):
- `IranDirect.Core` `SourceName` — `PathVeer.Core/Observability/Telemetry/IranDirectTelemetry.cs` untouched.
- `service.name=IranDirect.Service` — `ObservabilityResourceBuilder.cs` untouched.
- `irandirect_*` metrics — no change.

Freeze asserted in `StateRootMigrationTests.FrozenIdentifiers_Unchanged` (still green).

---

## 12. Mixed-Version Compatibility Matrix

| Client | Service | Result |
|---|---|---|
| legacy (`IranDirect.Control.v1`) | new PathVeer (dual-listen) | ✅ works on legacy pipe → same coordinator |
| new PathVeer (primary) | new PathVeer | ✅ works on primary pipe |
| new PathVeer (primary, unavailable) | legacy `IranDirect` | ✅ connect-time fallback to `IranDirect.Control.v1` |
| new PathVeer | legacy `IranDirect` (primary down, fallback) | ✅ safe (no bytes sent before fallback) |

Wire protocol (`IranDirectCommand` enum numeric values, `ServiceRequest`/`ServiceResponse` JSON, request envelope, serialization, protocol version) is **frozen** — pipe identity changed, contract did not.

---

## 13. Manual Windows Validation

Per spec §28–29, **no real installed service was modified** by automated tests (all lifecycle/migration logic is exercised via `IServiceControllerAdapter` fakes).

Optional manual inspection (not performed destructively here):
```
Get-Service IranDirect
Get-Service PathVeer
```
The developer's installed `IranDirect` Windows Service was left untouched. The actual SCM transition (stop legacy → create/start PathVeer → remove legacy) is owned by the Phase 36.7 installer acceptance on a disposable/test machine.

---

## 14. Limitations / Remaining Risks

1. **Legacy-state invisibility on rollback** — see §9. Rollback restores availability, not state parity, after PathVeer mutations.
2. **Legacy service not auto-deleted** — `Install-PathVeerService.ps1` stops but does not delete the `IranDirect` service; deletion is the Phase 36.7 contract once PathVeer is confirmed operational.
3. **Pipe ACL** — relies on platform defaults (unchanged from prior single-pipe implementation); no weakening, but no explicit hardening either.
4. **Transient test flake** — the known environmental `PersistenceEnduranceConcurrencyTests.CrossInstance_ReadersAndSingleWriter_NoMalformedReads` file-lock failure (recurs across 36.2/36.3/36.4) is environmental, not a branding defect; re-passes in isolation.

---

## 15. Phase 36.5 Entry Point

Phase 36.5 owns **CLI/Tray UX surface branding** (user-facing strings, icons, menu labels). What 36.4 deliberately deferred there: the Tray/CLI still instantiate `IranDirectServiceClient` (cosmetic historical type name — wire/client logic is identical and already PathVeer-aware via `PathVeerNamedPipeClientFactory`); only the *operational* service identity lookup was migrated in 36.4. 36.5 may rename the `IranDirectServiceClient`/`IranDirectCommand` historical type names and refresh user-facing copy without touching the now-frozen pipe/service/telemetry identifiers.

---

## 16. Verification Summary (fresh, this session)

| Suite | Result |
|---|---|
| Full solution build (`PathVeer.slnx` Debug) | 116 warnings, 0 errors |
| New 36.4 tests (`ServiceIpcMigrationTests` + lifecycle/IPC/telemetry focused) | 68 passed |
| Full `PathVeer.Core.Tests` | 2390 (1 transient environmental flake, re-passes isolated) |
| `PathVeer.Service.Tests` | 50 passed |
| Globalization / Country / StateRoot / Coordinator / Recovery / Journal regression | 245 passed |
| Stress (`Category=Stress`) | 12 passed |
| Benchmark (`PathVeer.Benchmarks` Release) | clean (2 warnings) |

Scope proof (`git diff --stat`, `git diff --check`, freeze grep) confirms: no state-migration semantics changed, telemetry/metrics frozen, planner/executor/journal schema unchanged, no deployment dashboards/rules changed, no SaaS code, repository path unmoved (`C:\codespace\irandirect`).

---

## 17. Recommended Commit Message

```
feat(branding): migrate service and IPC identity to PathVeer
```
