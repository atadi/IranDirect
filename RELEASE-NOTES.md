# IranDirect Release Candidate — v0.12.0-rc

## Implemented Features

### Phase 12 — Operation Status and Progress

- **RuntimeOperationStatus**: In-memory transient operation state (Idle/Enabling/Disabling/Repairing/Failed) exposed through status IPC. Set before execution begins, updated as steps complete, cleared on completion.
- **Progress Propagation**: `RuntimeExecutor` accepts optional `IProgress<RuntimeExecutionProgress>` and reports per-step totals (total, processed, succeeded, failed, cancelled, skipped). Thread-safe under bounded parallel execution.
- **Desired/Applied Separation**: CLI displays `Desired` (from configuration) and `Applied` (from state.json) separately, plus the current `Operation` state and `Progress`.
- **Performance Instrumentation**: `RuntimeExecutor` logs aggregate metrics per execution (total steps, succeeded, failed, cancelled, total duration, avg per step).

### Phase 11 (completed)

- Bounded parallel prefix execution (max 8 concurrent)
- Inventory thread safety (SemaphoreSlim per store)
- Stale inventory convergence on remove
- Orphan-route compensation on inventory persistence failure

### Earlier phases

- Windows Service authority, route ownership, VPN safety, control plane, execution domain, decision contract, decision builder, coordinator migration, controller integration, service cycle + periodic repair.

## Tested Scenarios

- 266 automated tests: 0 failures (249 pre-existing + 17 new)
- Operation status lifecycle: Begin, Reset, Report, Complete, Fail
- Controller integration: Enable/Disable/Repair set correct operation state
- Progress reporting through `IProgress<RuntimeExecutionProgress>`
- Thread-safe progress under parallel execution
- Cancellation handling in operation status
- Status includes DesiredEnabled and Operation fields
- All existing 249 tests remain passing unchanged

## Known Limitations

- **OpenVPN endpoint validation**: Not validated with ZoogVPN active. Endpoint protection for non-OpenVPN providers is untested.
- **Inventory batching**: Loading/saving inventory per individual step is the expected dominant cost at 1,944 prefixes. Batching (load once per group, persist once per group) is deferred to a follow-up phase after profiling confirms the hypothesis.
- **RouteInventoryStore.ClearAsync** during disable duplicates pipeline inventory cleanup.

## Installation

Requires Administrator PowerShell:

```powershell
.\tools\Install-IranDirectService.ps1 -Action install
```

This publishes the service, creates/updates the Windows Service, and starts it.

## Rollback / Uninstall

```powershell
.\tools\Install-IranDirectService.ps1 -Action uninstall
```

## Version

Release candidate: `v0.12.0-rc`
Commit: current HEAD on `development/service-authority`

Recommended tag: `v0.12.0-rc` (do not create automatically)
