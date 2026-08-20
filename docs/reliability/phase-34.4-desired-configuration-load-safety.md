# Phase 34.4 — DesiredConfiguration Missing/Corrupt Load Contract

Status: implemented (not yet committed)
Branch: `development/service-authority`
Base: `522296b docs(reliability): validate cross-store recovery model` (Phase 34.3)

## 1. Problem

`DesiredConfiguration` is the authoritative desired state for the VPN/routing
service. The underlying `JsonStore<T>` had an asymmetric load contract:

- MISSING file → `JsonStore` silently returned `new T()` (a `DesiredConfiguration`
  with `VpnProfilePath = "vpn-profile.ovpn"`, `Enabled = false`). The validator
  passed it, so a missing authoritative file was indistinguishable from an
  intentionally saved *disabled* configuration. The service would silently run
  disabled and mask the fact that configuration was absent.
- CORRUPT file → `JsonStore` propagated `JsonException`, crashing the
  `BackgroundService` worker (non-deterministic crash-loop).

Neither state was explicit: missing was conflated with disabled, and the
distinction the runtime needs (`missing ≠ disabled`) did not exist.

## 2. Authoritative load contract (chosen)

| State | Behavior |
|-------|----------|
| valid file | load normally (unchanged) |
| valid disabled (saved) | load normally; `Enabled = false`; indistinguishable only by intent, not by error |
| missing file | **fail closed** — throw `DesiredConfigurationMissingException` |
| corrupt file | **fail closed** — throw `DesiredConfigurationCorruptException` |

Invariants preserved:

- No silent `new()` substitution on load.
- No default file is created during load.
- A corrupt file is never overwritten or reset to defaults (the original bytes
  are preserved as evidence).
- No route mutation occurs based on an ambiguous or silently defaulted config.

## 3. Typed exceptions

New file `IranDirect.Core/Configuration/DesiredConfigurationExceptions.cs`:

- `DesiredConfigurationException` — base, caught by the startup/control layer.
- `DesiredConfigurationMissingException` — file absent / uninitialized.
- `DesiredConfigurationCorruptException` — unparseable or validator-rejected
  content. Carries the original exception as inner.

Messages are deliberately free of the file path and raw content (§17): they
state the condition without exposing sensitive material.

## 4. Store override

`DesiredConfigurationStore.LoadAsync` now:

1. Checks `File.Exists(_path)` first. If absent → throw
   `DesiredConfigurationMissingException`.
2. Otherwise calls the base load, then validates. A `JsonException` or
   `InvalidOperationException` (validator) is wrapped as
   `DesiredConfigurationCorruptException`. Any other exception propagates
   unchanged (still fail closed, just not the typed corrupt case).
3. Never writes — so a corrupt file is preserved.

`SaveAsync` is unchanged in effect (validate-then-persist). The CLI write
commands (`SetEnabledAsync` / `SetProfilePathAsync`) now treat a MISSING file as
"start from a valid default and apply the change" via `LoadOrDefaultAsync`. This
is the legitimate first-run bootstrap: the `enable`/`disable`/profile commands
are how the authoritative configuration is first created. A CORRUPT file is
still rejected (the typed corrupt exception propagates, the original file is
left untouched).

## 5. Worker gating (startup / runtime)

`IranDirectWorker.ExecuteAsync` now:

- Completes route-mutation journal recovery first (Phase 34.2), because an
  in-flight mutation is a previously-authoritative intent that must settle
  before normal planning reads ownership state.
- Loads the desired configuration defensively. On
  `DesiredConfigurationException` it logs an operational error, sets
  `desired = null`, and **skips reconciliation** — no route mutation.
- Keeps the host alive (no crash-loop). It polls at a calm `UnconfiguredRetryInterval`
  (30 s) while unconfigured, so a valid configuration supplied via a command
  (or dropped in) is picked up **without a restart**.
- A valid enabled config runs the startup cycle, then (if `AutoRepair`) periodic
  cycles. A valid disabled config runs no cycle (existing disabled behavior).

Runtime config transition (§9): because the worker reloads the configuration
every cycle, Missing → valid saved via CLI is reconciled on the next poll with
no restart. Deletion after a valid load (§10) is likewise re-read every cycle,
so a deleted file flips the worker to the unconfigured (no-mutation) state on
the next poll; it is never reinterpreted as "disabled".

Last-known-good (§11): not retained. On a load failure the worker goes to the
explicit unconfigured state and polls; this is safer than silently continuing
with a stale configuration, and the missing/corrupt distinction is preserved for
diagnostics.

## 6. IPC / status / diagnostics (§12)

Consumer degradation is explicit and adds no wire-schema change:

- `IranDirectController.GetStatusAsync` catches `DesiredConfigurationException`
  and reports `DesiredEnabled = false` (runtime is still observable, intent is
  unknown) instead of throwing.
- `RuntimeSnapshotProvider` catches it and surfaces `Configuration = null`
  (the snapshot field is already nullable) — truthfully "unconfigured".
- `SupportSnapshotProvider` catches it and surfaces `Configuration = null`.
- `NamedPipeCommandServer.GetConfigurationAsync` catches it and returns a
  bounded `Failure("CONFIGURATION_UNAVAILABLE", ...)` response.
- `DesiredConfigurationDiagnosticCheck` already caught all `Exception`s and
  reports a `Failed` health state; a missing file now correctly reports `Failed`
  (previously it reported `Passed` because a fabricated disabled config passed
  validation).
- The pipe server's message-loop `catch (Exception)` remains the backstop so a
  config load failure can never take down the IPC server.

## 7. Interaction with Phase 34.2 / 34.3 (§14)

- Journal recovery runs **before** the config gate. A pending route-mutation
  journal is resolved (adopt/complete/clear stale) regardless of desired-config
  availability, because it represents a previously-committed intent. The
  `RouteMutationRecoveryTests` and `CrossStoreStateRecoveryTests` remain green.
- Cross-store tests updated: the old "missing silently becomes disabled" test is
  replaced by one asserting `DesiredConfigurationMissingException` is thrown.
- No general `JsonStore` redesign; only `DesiredConfigurationStore` overrides
  its own load. No schema, planner, VPN, DNS, prefix, IPC framing, or telemetry
  change.

## 8. Tests added / changed

Production:
- `IranDirect.Core/Configuration/DesiredConfigurationExceptions.cs` (new)

Modified:
- `IranDirect.Core/Configuration/DesiredConfigurationStore.cs` (load contract)
- `IranDirect.Core/Configuration/DesiredConfigurationService.cs` (missing→default on write commands only)
- `IranDirect.Core/IranDirectController.cs` (GetStatusAsync resilient)
- `IranDirect.Core/Observability/RuntimeSnapshotProvider.cs` (null on unavailable)
- `IranDirect.Core/Support/SupportSnapshotProvider.cs` (null on unavailable)
- `IranDirect.Service/Ipc/NamedPipeCommandServer.cs` (Failure response; `sealed` removed / `RunAsync` made `virtual` to allow a test double)
- `IranDirect.Service/IranDirectWorker.cs` (startup/runtime gating)

Tests:
- `IranDirect.Core.Tests/Configuration/DesiredConfigurationStoreTests.cs`
  - `LoadAsync_WhenFileDoesNotExist_ThrowsMissing` (flipped from the old
    `ReturnsDefaults` characterization test — this pins the baseline change)
  - `LoadAsync_WhenFileIsCorrupt_ThrowsCorrupt` (proves original file preserved)
  - `LoadAsync_DoesNotCreateFileWhenMissing`
  - `LoadAsync_ValidDisabled_IsDistinguishableFromMissing`
  - existing round-trip / invalid-save tests kept
- `IranDirect.Core.Tests/Runtime/Execution/CrossStoreStateRecoveryTests.cs`
  - `DesiredConfiguration_MissingFile_FailsClosed_NotPermissive`
- `IranDirect.Core.Tests/Runtime/RuntimeCoordinatorTests.cs` — disabled-case test
  now saves an explicit disabled config.
- `IranDirect.Core.Tests/Diagnostics/Configuration/DesiredConfigurationDiagnosticCheckTests.cs`
  — missing file now asserts `Failed`.
- `IranDirect.Core.Tests/Observability/RuntimeSnapshotProviderTests.cs` — missing
  config surfaced as `null` (truthful).
- `IranDirect.Service.Tests/IranDirectWorkerTests.cs` (new) — proves the gating:
  - missing → 0 reconcile, no throw
  - corrupt → 0 reconcile, no throw
  - valid enabled → ≥1 reconcile
  - valid disabled → 0 reconcile

## 9. Verification (§19)

- Full Core suite: 2204 passed, 0 failed.
- Focused (DesiredConfiguration / StateRecovery / RouteMutation / IranDirectWorker): green.
- Service suite: 49 passed, 0 failed.
- Stress (Category=Stress): 12 passed.
- Benchmark Release build: succeeded.
- Full solution `dotnet build`: succeeded, 0 warnings, 0 errors.

## 10. Risks / limitations

- `NamedPipeCommandServer` had `sealed` removed and `RunAsync` made `virtual`
  solely to enable the worker startup test double. This is behavior-preserving;
  no external code extended it.
- The 30 s unconfigured poll interval is a constant; it avoids exception-driven
  control flow on every cycle while keeping the host responsive to a later valid
  config. First-reconcile latency after a config appears is bounded by this
  interval (acceptable for a configuration-change event, not a hot path).
- `DesiredConfiguration` schema is unchanged.

## 11. Recommended commit

`fix(configuration): fail closed when desired configuration is missing`
