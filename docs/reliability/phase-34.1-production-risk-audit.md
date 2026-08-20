# Phase 34.1 — Production Risk Audit and Next-Slice Selection

**Status:** COMPLETE (analysis only — no code changes)
**Date:** 2026-08-07
**Branch / HEAD:** `development/service-authority` @ `912ed57 fix(observability): complete production telemetry validation`
**Scope:** Fresh audit of the current IranDirect architecture after the observability initiative; rank production risks; select exactly one next implementation slice.

This phase did **not** implement fixes, refactor production code, or add permanent tests. Temporary diagnostics, if any, were removed before finalizing.

---

## 1. Current Architecture Map

### Composition root
`IranDirect.Service/ServiceCompositionRoot.cs` wires all singletons; `Program.cs` hosts `IranDirectWorker` (a `BackgroundService`) plus `NamedPipeCommandServer`.

### Runtime spine (per cycle)
```
Service startup / IPC command
  → OperationCoordinator.ExecuteAsync (SemaphoreSlim(1,1) serializes ALL operations)
    → IranDirectController.RunCycleAsync / EnableAsync / DisableAsync
      → RuntimeCycleCoordinator.RunCycleAsync
        → RuntimeDecisionBuilder.BuildAsync
            → RuntimeCoordinator.BuildPlanAsync
                → DesiredConfigurationService.GetAsync        (desired)
                → RuntimeObserver.ObserveAsync                 (observed: routes, gateway, vpn profile, endpoints, prefixes)
                → RuntimePlanner.Plan(config, observed)        (desired endpoint + prefix routes, blockers)
            → RuntimeReconciler.ReconcileAsync(plan)
                → RuntimeRouteOwnershipProvider.LoadAsync      (owned identities from inventory stores)
                → RuntimeChangeSetPlanner.Plan(snapshot, ownership)  (sorted add/remove change set)
      → RuntimeExecutor.ExecuteAsync(plan, progress, ct)
        → WindowsRuntimeExecutionStepHandler (per step: native mutation THEN inventory persistence)
      → UpdateStateAsync / RecordCycleOutcome
```

### Key components
| Concern | Type(s) |
|---|---|
| Worker / background loop | `IranDirectWorker` (BackgroundService) |
| Operation serialization | `OperationCoordinator` (`SemaphoreSlim _gate`) |
| Controller / cycle | `IranDirectController` |
| Decision build | `RuntimeDecisionBuilder`, `RuntimeCoordinator`, `RuntimeReconciler`, `RuntimePlanner`, `RuntimeChangeSetPlanner` |
| Execution | `RuntimeExecutor`, `WindowsRuntimeExecutionStepHandler` (implements `IPrefixGroupExecutionHandler`) |
| Route mutate/observe | `WindowsRouteManager` → `WindowsRouteApi` (PowerShell `Get-NetRoute` / `netsh -f`) |
| Route inventory | `RouteInventoryStore : JsonStore<RouteInventory>` (mutex) |
| VPN endpoint inventory | `VpnEndpointInventoryStore : JsonStore<VpnEndpointInventory>` (mutex) |
| Endpoint protection | `VpnEndpointRouteManager` (EnsureProtectedAsync / GetHealthAsync) |
| VPN endpoint discovery | `OpenVpnEndpointProvider` → `OpenVpnProfileParser` + `VpnEndpointResolver` (DNS) |
| Desired config | `DesiredConfigurationService` / `DesiredConfigurationStore : JsonStore<DesiredConfiguration>` (validator) |
| IPC | `NamedPipeCommandServer` (`maxNumberOfServerInstances: 1`) |
| Persistence base | `JsonStore<T>` (tmp-file + `File.Move(overwrite)`) |
| Diagnostics | `ManagedRouteConsistencyDiagnosticCheck`, `RouteOwnershipDiagnosticCheck`, `WindowsRouteTableDiagnosticCheck`, `RouteInventoryDiagnosticCheck` |
| Custom-route DNS | `CustomRouteDnsCacheService` / `CustomRouteDnsCacheStore` / `CustomRouteDnsCacheRepository` |

### VPN endpoint discovery / protection path
`OpenVpnEndpointProvider.GetEndpointsAsync` parses the profile and DNS-resolves each host (`VpnEndpointResolver.ResolveAsync` → `Dns.GetHostAddressesAsync`). Resolved IPv4 endpoints feed `RuntimePlanner.BuildEndpointRoutes` (desired `/32` host routes via the direct gateway, metric 1). The step handler adds them via `WindowsRouteManager` and records ownership in `VpnEndpointInventoryStore` (`AddedByIranDirect=true, IsCurrent=true`). On removal, native delete then inventory removal.

### Service restart / recovery path
`IranDirectWorker.ExecuteAsync` loads desired config; if `Enabled`, runs one startup cycle, then loops on `Task.Delay(RepairInterval)` + cycle while `AutoRepair`. Each cycle is serialized by `OperationCoordinator`, so a fresh start re-derives desired from current observed state and reconciles.

---

## 2. Recovery Model

- **Reconciliation is converge-to-truth, not trust-stale.** Each cycle rebuilds desired from *observed* VPN endpoints + prefixes + gateway, and ownership from *inventory*. Orphans that are present-but-not-owned are neither removed nor re-added by the planner (removal only targets owned identities; add only targets missing ones). This is the core recovery gap (see Findings R1/R2).
- **Compensation exists for the graceful path:** if inventory persistence fails after a native add, `CompensatePrefixRouteCreationAsync` / `CompensateEndpointRouteCreationAsync` delete the native route (best-effort; if compensation also fails, the route is reported orphaned). Compensation is **not** invoked on a hard process crash.
- **Routes are `store=active` (non-persistent).** A reboot evicts all IranDirect routes; the inventory still lists them as owned → next cycle sees "owned but absent" → re-adds. Self-heals on next cycle. (Intended: managed routes are not persisted to the OS store.)
- **Diagnostics surface (but do not fix) divergence:** `ManagedRouteConsistencyDiagnosticCheck` computes `missingFromInventory` / `extraInInventory` / duplicate / gateway / interface mismatches and returns a Warning. No automated remediation path exists.

---

## 3. Ownership Model

- Prefix-route ownership = `RouteInventory.Routes` (identity `prefix|gateway|ifIndex`).
- Endpoint-route ownership = `VpnEndpointInventory.Endpoints` where `AddedByIranDirect`.
- `InventoryRouteOwnershipSource` exposes owned identities from both stores; the planner's removal passes iterate **owned** identities not present in **desired** and emit removes.
- **Identity mismatch risk:** endpoint inventory identity is `DestinationPrefix|Gateway|InterfaceIndex` (set from `step.Gateway`/`step.InterfaceIndex`). Desired endpoint identity is derived from the *resolved* endpoint address. If the same VPN host resolves to a different IP after rotation, the old identity remains `AddedByIranDirect` and is correctly removed; if an address appears under two identities, first-occurrence wins in both desired build and planner.
- **Stale `IsCurrent` flag:** `ExecuteAddEndpointRouteAsync` sets `IsCurrent=true` on add but never flips prior items to `IsCurrent=false`. `IsCurrent` is only consumed by `GetHealthAsync`; it does **not** affect desired/remove logic, so it is dead/inconsistent state (informational, not a correctness bug — see Non-findings).

---

## 4. Endpoint Protection Model

- `VpnEndpointRouteManager.EnsureProtectedAsync` adds only *missing* `/32` host routes; it does **not** remove anything (no proactive sweep).
- Stale-endpoint cleanup is handled indirectly by the planner: desired endpoint routes come from *currently observed* VPN endpoints; an old endpoint identity that is `AddedByIranDirect` but absent from desired yields a `RemoveEndpointRoute` in the planner's removal pass → native delete + inventory removal. **Rotation cleanup therefore works**, provided the inventory still records the old item as `AddedByIranDirect` (it does).
- **No "reconnect routes the VPN endpoint through the tunnel" risk:** the `/32` host route points at the *direct* gateway, never the VPN interface, so the tunnel's own endpoint is excluded from tunneled traffic (standard split-tunnel guard). Confirmed by `VpnEndpointRouteManager.CreateRoute` using `gateway.Address`/`gateway.InterfaceIndex` (direct gateway).
- `EnsureProtectedAsync` throws `InvalidOperationException` if zero IPv4 endpoints resolve (fail-stop, visible).

---

## 5. Concurrency Model

- **All operations serialized by `OperationCoordinator._gate` (`SemaphoreSlim(1,1)`).** The worker's periodic cycle and every IPC command (Enable/Disable/Repair/UpdatePrefixes/CustomRoutes*) flow through `_operations.ExecuteAsync`, so **two runtime cycles cannot overlap**, and a user-triggered repair cannot race the worker. (This refutes the "overlapping cycle" hypothesis.)
- **Within a single cycle, prefix routes run with bounded parallelism** (`DefaultMaxDegreeOfParallelism = 8`) via `Task.Run` + `SemaphoreSlim` throttle. A per-group `Volatile groupFailed` flag skips remaining steps on first failure; `CancellationToken.None` is passed only as the *Task.Run start token* — the real `cancellationToken` still flows into the handler and into `CommandRunner`, so mutations are cancellable (see Cancellation Model).
- **Cross-store persistence is NOT under one lock.** `RouteInventoryStore` and `VpnEndpointInventoryStore` each hold an independent `SemaphoreSlim`. A crash between their two `SaveAsync` calls leaves them divergent (Finding R2).
- IPC server processes one client at a time (`maxNumberOfServerInstances: 1`, sequential loop), so no in-flight command overlap.

---

## 6. Cancellation / Shutdown Model

- `CommandRunner.RunAsync` honors cancellation: on token cancel it `WaitForExitAsync` throws and kills the process tree (`process.Kill(entireProcessTree: true)`). Native route mutations are therefore cancellable mid-flight.
- `IranDirectWorker` awaits `Operations.ExecuteAsync(controller.RunCycleAsync, stoppingToken)`; the hosted-service `stoppingToken` is threaded through. On shutdown, an in-flight cycle is cancelled; `OperationCoordinator._gate.WaitAsync(stoppingToken)` prevents a new operation from starting once cancelled.
- `RuntimeExecutor` stops the group on first failure or cancellation and marks remaining steps Skipped/Cancelled. Inner prefix tasks use the real token. (The `Task.Run(..., CancellationToken.None)` is a code smell only — not a correctness defect.)
- **Residual risk:** a `Task.Delay(RepairInterval, stoppingToken)` between cycles is correctly cancelled; but if a hard process kill occurs during a native mutation that has already succeeded but whose inventory persist is pending, no compensation runs (see R1).

---

## 7. Persistence Model

`JsonStore<T>` (base for all three stores):
- **Atomic write:** serialize → `path + ".tmp"` → `File.Move(overwrite:true)`. A reader using `FileShare.ReadWrite|Delete` will see the old or new file, never a partial. **Good.**
- **Retries:** up to `MaxFileAccessAttempts = 5` on `IOException`/`UnauthorizedAccessException` with 25·(n+1) ms backoff.
- **Empty/missing file → `new T()`** (no throw). Corrupt JSON → `JsonSerializer` returns `null` → `new T()` (silent reset to empty! — see R3 note).
- **No `File.Replace`**; uses `File.Move` (atomic on same volume).
- **Independent locks + independent files** → no cross-store transaction.
- `DesiredConfigurationStore` adds `ValidateAndThrow` on load/save but has **no version/schema field** (see R4).

---

## 8. Retry / Timeout Inventory

| Operation | Strategy | Notes |
|---|---|---|
| JSON file load | retry ×5, 25·(n+1) ms | only on IO/Unauthorized |
| JSON file save | retry ×5, 25·(n+1) ms | only on IO/Unauthorized |
| `Get-NetRoute` enumerate | 30 s timeout | PowerShell; culture-invariant property names |
| `netsh -f` add/delete batch | 5 min timeout | cancellable; whole batch fails on one error |
| DNS resolve (`VpnEndpointResolver`) | **none** | `Dns.GetHostAddressesAsync` OS timeout, no app retry/backoff |
| Prefix HTTP fetch | via `IPrefixSource` (external) | not inspected here in depth; caller logs metadata/history |
| IPC pipe | none needed | single instance, sequential |
| Worker cycle delay | `RepairInterval` | cancelled on shutdown |
| Route system calls | none beyond netsh timeout | see §12 |

Worst-case: a hung netsh is killed at 5 min; a hung DNS blocks plan build until OS timeout (typically long on Windows) — not bounded by app logic (R5, low).

---

## 9. Native-Route Mutation Safety (§12)

- Mechanism: `Get-NetRoute` (PowerShell) for enumerate; `netsh interface ipv4 add/delete route … store=active` for mutate, via a temp script file.
- **No privilege assumption documented**, but `store=active` requires elevated rights (service runs as admin / SYSTEM).
- **Duplicate add:** `netsh add route` for an existing identical route returns non-zero → whole batch throws → step handler catches and (for prefix add) the classifier may report a *false failure* even though the route is present (`ClassifyAddPrefixRouteAsync`: `exactPresent && !mutation.Succeeded` → "not found after add"). Harmless (route exists) but produces a misleading Failed step. (R6, low.)
- **Batch partial-failure ambiguity:** `netsh -f` processes sequentially; if line N fails, lines 1..N-1 may already be applied, but the batch reports failure and the executor stops the group. Compensation for add-then-fail handles the *graceful* case; a crash mid-batch is uncompensated (covered by R1).
- **IPv4 only** (`AddressFamily.InterNetwork` filtered); IPv6 not managed by this path.
- **Localization:** property names selected explicitly; values are IP/int (culture-invariant). Low risk.

---

## 10. Configuration Integrity (§13)

- `DesiredConfigurationValidator` validates on load/save; invalid config → blockers (not exceptions) in `RuntimePlanner`, so a bad config yields `RuntimeReconciliationResult.Blocked` and no mutation — safe.
- **No schema/version field.** A persisted config written by an older binary that lacks a field newly added with a default will deserialize to defaults and load silently. If the default changes routing semantics, behavior changes on upgrade with no detection (R4).
- `SetEnabledAsync` (CLI/Tray/Service) writes the same store; the validator guards incompatible combinations. Multiple writers are serialized only by the file lock, not by higher-level coordination — concurrent writes are last-writer-wins (acceptable; low frequency).

---

## 11. IPC Reliability (§14)

- `NamedPipeServerStream(…, maxNumberOfServerInstances: 1, …)`; one client at a time; sequential `HandleOneClientAsync` loop.
- Framing: length-agnostic `ReadLineAsync` (one JSON line per request) + `WriteLineAsync` response. No partial-read handling beyond line boundaries; malformed JSON → `INVALID_JSON` response (no crash).
- Protocol version checked (`IpcProtocol.CurrentVersion`) → `UNSUPPORTED_PROTOCOL`.
- All mutating commands go through `OperationCoordinator` → serialized; no duplicate/excessive command risk.
- **Stale installed Service ownership:** only relevant if two Service processes run; the single pipe instance prevents a second Service from accepting commands, but the *route mutations* are OS-level and a second process could in theory issue netsh directly — out of IPC scope (operational control).

---

## 12. Background Worker Lifecycle (§15)

- Single `BackgroundService`; startup cycle if enabled; periodic loop while `AutoRepair`.
- Failure handling: each `RunServiceCycleAsync` is wrapped in try/catch (cancelled → log; exception → log error). **The worker cannot silently stop** — an unhandled exception would fault `ExecuteAsync` and stop the hosted service, surfaced by the host.
- Configuration changes (`AutoRepair`, `RepairInterval`, `Enabled`) are re-read at the top of each loop iteration, so they take effect deterministically on the next iteration.
- No spin-on-failure: cycle cadence is fixed `RepairInterval` regardless of success/failure (no exponential blow-up).
- Disabled state does **not** mutate routes (planner yields no endpoint/prefix routes when `!Enabled`); good.

---

## 13. Technical-Debt Scan (§17)

Production source (`IranDirect.Core`, `IranDirect.Service`, `IranDirect.Cli`, `IranDirect.Tray`) was scanned. **No** `TODO`, `FIXME`, `HACK`, `TEMP`, `NotImplementedException`, `NotSupportedException`, `async void`, `.GetAwaiter().GetResult()`, `.Result` (as blocking call), `.Wait()`, `Thread.Sleep`, `Environment.Exit`, or `Process.Kill` (direct) were found. Observed markers:
- `Task.Run` ×40 — intentional parallel prefix dispatch in `RuntimeExecutor`; legitimate.
- `File.Delete` ×7, `File.Move` ×5 — `JsonStore` atomic write + temp-script cleanup; legitimate.
- `.Result` substring matches (607) are grep false-positives (`*Result` type names).

Conclusion: code is clean of classic debt markers; risk is in *distributed-state atomicity*, not local smells.

---

## 14. Test-Coverage Map (§3)

- **Projects:** `IranDirect.Core.Tests` (184 test files), `IranDirect.Service.Tests` (11), plus `IranDirect.Testing` (harness).
- **Strong:** Execution (14 files incl. determinism/stress/scale), Runtime/coordinator (40), Persistence/JsonStore (5 + fault-injection tests), Planner (1, but exhaustive logic tests).
- **Partial:** Vpn (2), CustomRoutes (2), Configuration (3), Ipc (1), Diagnostics (1).
- **Weak / gap:** 
  - **No test for crash between native mutation and inventory persistence** (orphan route).
  - **No cross-store atomicity test** (route vs endpoint vs desired divergence after partial crash).
  - **No cancellation-during-netsh test** (covered indirectly by CommandRunner tests, but not end-to-end through executor).
  - **No DNS-hang/timeout test** for `VpnEndpointResolver`.
  - **No config-version-migration test** (R4).

---

## 15. Ranked Risk Table (§20–§21)

Scoring: Priority = Impact × Likelihood × Detectability; Recovery is tie-breaker.

### Critical
None.

### High
| ID | Title | Subsystem | I | L | D | R | Score | Coverage | Disposition |
|---|---|---|---|---|---|---|---|---|---|
| **R1** | Orphan route after crash between native mutation and inventory persist (no cross-transaction; compensation only on graceful path) | Execution / Persistence | 4 | 4 | 2 | 4 | **32** | weak | **SELECTED** (next slice) |
| **R2** | Cross-store divergence: independent locks/files mean a crash between two `SaveAsync` leaves route/endpoint/desired stores inconsistent | Persistence | 3 | 4 | 3 | 3 | **36** | weak | next slice candidate |

### Medium
| ID | Title | Subsystem | I | L | D | R | Score | Coverage | Disposition |
|---|---|---|---|---|---|---|---|---|---|
| R3 | Corrupt JSON store silently resets to empty `new T()` (no checksum/version); can wipe ownership on a bad write | Persistence | 3 | 3 | 3 | 3 | 27 | partial | roadmap |
| R4 | `DesiredConfiguration` has no schema version → silent behavior change on upgrade if a field default changes | Config | 3 | 3 | 3 | 3 | 27 | weak | roadmap |
| R5 | No DNS resolve timeout/retry in `VpnEndpointResolver`; a hang blocks plan build until OS timeout | VPN/DNS | 3 | 3 | 2 | 2 | 18 | weak | roadmap |

### Low
| ID | Title | Subsystem | I | L | D | R | Score | Coverage | Disposition |
|---|---|---|---|---|---|---|---|---|---|
| R6 | Duplicate `netsh add route` misclassified as "not found after add" (false Failed step) | Native route | 2 | 3 | 2 | 2 | 12 | partial | cleanup |
| R7 | `IsCurrent` flag on VPN endpoint inventory never cleared; dead/inconsistent state (no functional impact) | VPN inv. | 1 | 3 | 4 | 1 | 12 | n/a | informational |
| R8 | `Task.Run(..., CancellationToken.None)` start-token smell (real token still flows; not a defect) | Executor | 1 | 2 | 1 | 1 | 2 | n/a | informational |

### Informational / Non-findings (feared issues disproven)
- **VPN endpoint route never cleaned after rotation:** DISPROVEN. Rotation cleanup works via the planner's owned-not-desired removal pass.
- **Reconnect routes the VPN endpoint through the tunnel:** DISPROVEN. `/32` host route uses the direct gateway.
- **Overlapping runtime cycles (worker vs IPC):** DISPROVEN. `OperationCoordinator` serializes all operations.
- **Inner route mutations ignore cancellation:** DISPROVEN. Real token propagates to `CommandRunner` (which kills netsh on cancel).
- **Stale endpoint inventory after endpoint change (lockout):** Mitigated — planner removes owned-not-desired; detectability via diagnostics.

---

## 16. Selected Next Implementation Target (§22–§23)

**R1 — Crash-consistent route ownership reconciliation (orphan self-heal).**

### Why it outranks others
1. Highest priority class per the selection order: *unrecoverable restart/crash state* and *ownership corruption* (R1/R2 are the only findings in these top tiers).
2. Strongly source-proven: `WindowsRuntimeExecutionStepHandler` performs native mutation **before** `MutateRouteInventoryAsync`/`MutateEndpointInventoryAsync`; these are separate `JsonStore` writes with independent locks; compensation runs only on the graceful cancel/exception path, never on a hard crash.
3. Bounded scope, clear test strategy (fault-inject a crash between native-add and inventory-persist; assert next cycle self-heals), no broad redesign.
4. The diagnostic `ManagedRouteConsistencyDiagnosticCheck` already computes the missing/extra sets — the self-heal reuses that oracle.

### Exact production files likely involved
- `IranDirect.Core/Runtime/Execution/WindowsRuntimeExecutionStepHandler.cs` (ordering / compensation boundary)
- `IranDirect.Core/Runtime/RuntimeReconciler.cs` or a new `RouteOwnershipReconciler` invoked at cycle start
- `IranDirect.Core/Diagnostics/Routing/ManagedRouteConsistencyDiagnosticCheck.cs` (reuse logic)
- `IranDirect.Core/Routing/RouteInventoryStore.cs`, `IranDirect.Core/Vpn/VpnEndpointInventoryStore.cs`
- New: `IranDirect.Core.Tests/.../OrphanRouteReconciliationTests.cs`

### Behavior contract
On each cycle (or a dedicated reconcile pass), detect platform routes whose signature matches IranDirect's managed signature (destination in `observed.Prefixes`/`VpnEndpoints` space, metric/interface owned by IranDirect) but which are **absent from inventory**. For each:
- if it matches a desired route → **adopt** into inventory (idempotent, no native mutation);
- if it does not match desired (ghost/leftover) → **remove** the native route and record.
Idempotent, no double-mutation, observability events emitted (adopt vs remove counts).

### Acceptance gates
- A fault-injected crash (native add succeeds, inventory persist skipped) leaves the route present but unowned; the next cycle either adopts (if desired) or removes (if not) and the inventory becomes consistent.
- No route is deleted that is not IranDirect-managed (guard on signature/metric/interface).
- No infinite loop: adopted/removed routes stabilize.
- Determinism tests for the planner/path with injected `FaultInjectionPolicy`.

### Regression suites
- Existing `RuntimeExecutor*Tests`, `RouteInventoryStoreTests`, `ManagedRouteConsistencyDiagnosticCheckTests` remain green.
- New `OrphanRouteReconciliationTests` (fault injection + normal + multi-route).

### Performance implications
- One extra route-table read + inventory read per cycle (already done by the diagnostic). Negligible.

### Rollback risk
- Low: additive reconcile pass, guarded by the IranDirect signature; can be gated behind a config flag if needed.

### Recommended commit message
`fix(runtime): self-heal orphaned routes after crash between mutation and inventory persist`

---

## 17. Secondary Roadmap (§24)

1. **R2 — Cross-store persistence transaction boundary.** Wrap route + endpoint + desired saves (where co-related) in a single ordered, recoverable sequence with a write-ahead intent file, or collapse to one combined state document. Validated by a crash-during-second-save test. (Depends on R1's reconcile as the safety net.)
2. **R3 — Store corruption resilience.** Add a content hash/version header + keep-last-good on corrupt load; fail loudly instead of silent `new T()`. Validated by `JsonStoreFaultInjectionTests` extension.
3. **R4 — DesiredConfiguration schema version.** Add `SchemaVersion`; on load, reject-or-migrate; log a warning on default-fill of unknown fields. Validated by a version-migration test.
4. **R5 — DNS resolve timeout/retry.** Bound `VpnEndpointResolver` with a `TimeSpan` timeout + bounded retry/backoff; treat persistent failure as a blocker rather than an indefinite hang.

---

## 18. Explicit Non-Findings
(See §15 Informational.) VPN rotation staleness, tunnel-self-route, overlapping cycles, cancellation-ignoring mutations, and endpoint-inventory lockout were all examined and either disproven or found mitigated.

---

## 19. No-Source-Change Proof (§26)
This audit changed only documentation. Permanent changes:
- `docs/reliability/phase-34.1-production-risk-audit.md` (new)
- one navigation link in the docs index (see §25)

No `.cs`, `.csproj`, `appsettings`, deployment config, or test files were modified or added permanently. Any temporary diagnostic was removed.

---

## 20. Recommended Commit
`docs(reliability): audit current production risks`
