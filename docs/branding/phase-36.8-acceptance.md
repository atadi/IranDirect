# Phase 36.8 — PathVeer Final Installation / Upgrade / Recovery / Rebrand Acceptance

Branch: `development/service-authority`
Base: `e9c651e` (Phase 36.7 — installer/upgrade migration)
Scope: final acceptance and closure of the 36.x rename/migration program. No
broad reimplementation. Production changes are limited to narrowly scoped
defect fixes; none were required by this acceptance pass.

---

## 1. Repository reconciliation

| Item | Value |
| --- | --- |
| Workspace | `C:\codespace\PathVeer` |
| Branch | `development/service-authority` |
| Origin | `https://github.com/atadi/PathVeer.git` |
| Starting HEAD | `e9c651e` (matches handoff reference) |
| Initial working tree | clean (0 changed files) |
| Initial unpushed commits | none |
| Prior work committed before 36.8 | 36.1–36.7 already committed and pushed; nothing uncommitted |

Discrepancy from handoff: none. The handoff reference commits
(`bf99185`, `e3178f2`, `e9c651e`) match the current history. The previously
assumed "stale bind-mount from `C:\codespace\irandirect`" does NOT hold for the
live observability stack: it binds config from `C:\codespace\PathVeer`, i.e.
this branch (verified during the 36.6/36.7 live validation).

---

## 2. Phase 36.8 changes

No production code changes were required. Acceptance found no blocking
defect in the completed 36.x work. This document is the only new artifact.

The 36.6/36.7 live observability gap was independently CLOSED prior to 36.8
(real `pathveer_*` metrics, `service.name=PathVeer.Service` traces, Grafana
dashboards, rule groups, collector-outage isolation, preserved legacy
Docker volumes). That evidence is consumed here and not regenerated.

---

## 3. Acceptance matrix

Evidence classes:
- D1 = deterministic automated test (fake/controlled infra)
- RM = real-machine / real-process (bounded, reversible)
- VM = disposable VM required (not executed locally)
- NA = not applicable
- CODE = source inspection establishing the contract

| # | Scenario | Result | Evidence |
| --- | --- | --- | --- |
| A | Fresh PathVeer install (layout/Service/start/readiness) | PASS | D1: `InstallLayoutTests` (118 lines), `InstallOrchestratorTests` single-authority harness; CODE: `Install-PathVeer.ps1` staging→swap, `delayed-auto`, readiness gate |
| B | Upgrade from supported globalized IranDirect | PASS (harness) | D1: `InstallOrchestratorTests` (legacy stop→disable→swap→start→readiness→retire); `LegacyGlobalizationUpgradeTests` |
| C | Real Windows SCM identity transition | PASS (harness) | D1: `ServiceIpcMigrationTests`; CODE: `PathVeerServiceNames`, `Install-PathVeer.ps1` sc.exe create/rename, single-authority assertions |
| D | ProgramData `%ProgramData%\IranDirect` → `%ProgramData%\PathVeer` | PASS | D1: `StateRootMigrationTests` (497 lines): copy→verify→publish, temp non-authority, existing wins, legacy retained |
| E | Pending ADD journal recovery across upgrade | PASS | D1: `RouteMutationRecoveryTests`, `CrossStoreStateRecoveryTests`; `StateRootMigrationTests` migrate journal verbatim |
| F | Pending DELETE journal recovery across upgrade | PASS | D1: same as E; ownership inventory consistency asserted |
| G | Restart/reboot persistence | PASS (Service restart, harness) | D1: config/country/Enabled survive in `DesiredConfigurationStoreTests`; recovery runs at startup (`PathVeerWorker`). True OS reboot NOT EXECUTED locally → classified real-SCM/reboot but covered by restart semantics |
| H | PathVeer CLI (status/enable/disable/country/diagnostics) | PASS | D1: `CountryCliRunnerTests`, `CustomRouteCli*`, `SupportBundleCli*`; CODE: `Program.cs` uses `PathVeerServiceClient` |
| I | `irandirect.cmd` compatibility launcher | PASS | CODE: `Install-PathVeer.ps1` `New-CliCompatibilityShim` writes shim, deprecation notice to stderr, forwards to `PathVeer.Cli.exe`; single implementation, no second CLI |
| J | Tray startup (optional/per-user) | PASS | CODE: `Set-TrayStartupEntry` HKCU Run, off by default; `Remove-TrayStartupEntry` clears `PathVeer Tray` + legacy `IranDirect Tray` |
| K | Tray enable/disable controls policy via Service | PASS | CODE: `TrayApplicationContext` Enable/Disable → `PathVeerCommand.Enable/Disable` over pipe; Tray is not routing authority |
| L | Tray Exit does not stop Service / change startup / change Enabled | PASS | CODE: `ExitApplication()` disposes tray UI + `ExitThread()` only; no Stop/StartType/Enabled mutation |
| M | `PathVeer.Control.v1` primary IPC | PASS | D1: `ServiceIpcMigrationTests`; CODE: dual-listen server |
| N | `IranDirect.Control.v1` compatibility IPC, single authority | PASS | CODE: `PathVeerPipeNames.LegacyPipeName`, `NamedPipeCommandServer` dual-listen → same `OperationCoordinator` |
| O | Mixed-version IPC + no ambiguous-send replay | PASS | CODE: `PathVeerNamedPipeClientFactory` connect-time-only fallback; send/read failure does NOT replay |
| P | Country switching after upgrade (IR/IQ/RO) | PASS | D1: `CountrySwitchingAcceptanceTests`; `DirectCountryCode` single-selection, prefix isolation |
| Q | Offline selected-country cache / wrong-country rejection | PASS | D1: `CountryPrefixUpdateChecker*`, `PrefixSource*FaultInjection`; fail-closed on unavailable data |
| R | Endpoint (VPN) protection | PASS | D1: `VpnEndpointRouteManagerTests`, `OpenVpnProfileParserTests`; ownership semantics preserved |
| S | Custom-route preservation | PASS | D1: `CustomRouteCli*`, `CustomRouteCommandHandler` tests; contract unchanged |
| T | External-route safety | PASS | CODE: `WindowsRuntimeExecutionStepHandler` only claims `AddedByIranDirect`/owned routes; external routes untouched (tests assert non-owned routes skipped) |
| U | Route ownership persistence (`AddedByIranDirect`) | PASS | D1: `PersistedSchemaCompatibilityTests`, `InventoryRouteOwnershipSourceTests`; field NOT renamed |
| V | Observability identity current | PASS | Consumed live evidence + light recheck: running stack serves `pathveer_*` rule groups; config unchanged; legacy Docker volumes untouched |
| W | Default uninstall preserves state | PASS | CODE: `Invoke-Uninstall` releases routes, removes Service/binaries/PATH/startup, preserves `%ProgramData%\PathVeer`; `-PurgeState` off by default |
| X | Reinstall over preserved state | PASS (harness) | CODE: `Write-InstallManifest` + `Get-InstalledVersion`; migration `ExistingPathVeerRoot_Wins` ensures reinstall converges |
| Y | Explicit purge uninstall | PASS (harness) | CODE: `-PurgeState` only path that deletes `%ProgramData%\PathVeer`; `%ProgramData%\IranDirect` never deleted. Not executed against real valuable state |
| Z | Rollback boundary | PASS (documented) | CODE: upgrade floor = final globalized IranDirect; legacy state retained; rollback possible until PathVeer-only mutations; arbitrary historical rollback NOT claimed |
| AA | Final IranDirect inventory classification | PASS | See §10. Zero unexpected active product identities |
| AB | Release-readiness decision | CONDITIONAL PASS | Code/harness acceptance complete; destructive real legacy→PathVeer upgrade against a live legacy SCM install + OS reboot test are VM-required (not executed). See §15 |

---

## 4. Lifecycle acceptance

| Contract | Satisfied | Evidence |
| --- | --- | --- |
| Service persistent (boot → auto-start → recover → reconcile/idle) | Yes | CODE: `AddWindowsService` `delayed-auto`; `PathVeerWorker` recovery→cycle loop |
| Tray optional/per-user | Yes | CODE: HKCU Run, `-InstallTray` opt-in |
| Tray Exit does not stop Service | Yes | CODE: `ExitApplication()` = `ExitThread()` only |
| Tray Exit does not alter Service startup type | Yes | CODE: no `sc.exe`/StartType call in exit path |
| Tray Exit does not implicitly change Enabled | Yes | CODE: exit path never sends Enable/Disable |
| Disable changes policy while Service remains available | Yes | CODE: Disable → `PathVeerCommand.Disable` → Service-controlled reconciliation; Service stays running |

---

## 5. State migration / recovery

- **IranDirect → PathVeer state migration**: `StateRootResolver`/`StateRootMigrator`
  copy→verify (file count + per-file length + SHA-256 + top-level files)→atomic
  `Directory.Move`. Temp roots `%ProgramData%\PathVeer.migrating-*` never
  authoritative. Existing PathVeer root wins. Legacy retained for rollback.
- **Pending ADD recovery**: `RouteMutationRecovery` + `CrossStoreStateRecovery`
  replay journal intent; `AddedByIranDirect` preserved so ownership survives.
- **Pending DELETE recovery**: same path; ownership inventory stays consistent.
- **Restart persistence**: `DesiredConfigurationStore` round-trips Enabled/Country;
  recovery runs at worker startup (`PathVeerWorker`).
- **Route ownership**: `AddedByIranDirect` persisted schema field, NOT renamed.

---

## 6. Installation / upgrade / uninstall

| Concern | Result | Evidence |
| --- | --- | --- |
| Fresh install | PASS | D1 harness + CODE |
| Legacy upgrade | CONDITIONAL (VM for full real run) | D1 harness proves ordering + single authority |
| SCM transition | PASS (harness) | `ServiceIpcMigrationTests` |
| Default uninstall | PASS | CODE `Invoke-Uninstall` non-destructive |
| State preservation | PASS | CODE: `%ProgramData%\PathVeer` retained unless `-PurgeState` |
| Reinstall | PASS (harness) | CODE manifest + migration idempotence |
| Explicit purge | PASS (harness) | CODE `-PurgeState` only deletion path |
| Rollback boundary | PASS (documented) | CODE + §Z |

Harness evidence = `InstallOrchestratorTests`, `InstallLayoutTests`,
`ServiceIpcMigrationTests`, `StateRootMigrationTests`,
`RouteMutationRecoveryTests`, `CrossStoreStateRecoveryTests`.
Real-SCM/VM evidence = NOT executed (would require a real legacy IranDirect
service install + destructive upgrade + reboot; see §15).

---

## 7. Country / routing acceptance

- **Country switch (IR/IQ/RO)**: `CountrySwitchingAcceptanceTests` — requested
  country persisted, selected-country prefix isolation, no wrong-country cache.
- **Offline selected-country cache**: `CountryPrefixUpdateChecker*` + fault
  injection prove cached selected-country data works offline; unavailable data
  → Blocked/fail-closed; existing routes not destructively emptied.
- **Wrong-country cache rejection**: explicit; never substitutes another
  country's cache.
- **Endpoint protection**: `VpnEndpointRouteManagerTests` confirm protected
  endpoint routes remain owned/protected.
- **Custom-route preservation**: contract unchanged; tests cover migration/
  reconciliation/restart/enable-disable/country-switch.
- **External-route safety**: `WindowsRuntimeExecutionStepHandler` only claims
  owned/`AddedByIranDirect` routes; external routes skipped.

---

## 8. IPC compatibility

- **`PathVeer.Control.v1`**: primary pipe, served by dual-listen server.
- **`IranDirect.Control.v1`**: legacy pipe, same `OperationCoordinator` → one
  authority (two front doors).
- **Mixed-version**: new client prefers primary, falls back to legacy ONLY on
  connect failure (no bytes sent → unambiguous). Legacy-compatible client on
  legacy pipe reaches PathVeer authority.
- **Ambiguous-send no replay**: connect-time-only fallback; send/read failure
  does NOT replay on the other pipe (exactly-once guarantee documented in
  `PathVeerNamedPipeClientFactory`).

---

## 9. Observability

- **Consumed live evidence**: 36.6/36.7 live validation — real `pathveer_*`
  metrics carrying `service_name="PathVeer.Service"`, real Tempo traces with
  `service.name=PathVeer.Service` (root span `PathVeer.RuntimeCycle`), Grafana
  PathVeer dashboards + datasources exercised, collector-outage isolation,
  four legacy-named Docker volumes preserved.
- **Freshly checked in 36.8**: live stack still up 8h, still serves
  `pathveer_alerts` + `pathveer_recording` rule groups; config unchanged from
  this branch.
- **Legacy telemetry identity returned?** No. No `irandirect_*` application
  series emitted; source identity is `PathVeer.Core`.
- **Legacy Docker volumes**: `irandirect-{prometheus,tempo,grafana,alertmanager}-data`
  still attached, untouched.

---

## 10. Final IranDirect inventory (AA)

Repo-wide search (excl `bin/`, `obj/`, generated `graphify-out/`):

Production code (`*.cs`, non-test) IranDirect/irandirect references, all classified:

| Identifier | Category | Occurrences |
| --- | --- | --- |
| `AddedByIranDirect` (persisted route-ownership schema) | PERSISTED SCHEMA (must NOT rename) | 15 |
| `IranDirect.Control.v1` (legacy compat pipe) | REQUIRED COMPATIBILITY | 8 |
| `LegacyServiceNames.ServiceName = "IranDirect"` (legacy service detection) | MIGRATION INPUT | 1 |
| `iran-ipv4-prefixes.txt` (legacy prefix filename) | REQUIRED COMPATIBILITY | 2 |
| `irandirect-*-data` Docker volumes | OPAQUE PERSISTED INFRASTRUCTURE ID | deployment only |
| `IranDirect Tray` autorun value (stale-entry cleanup) | REQUIRED COMPATIBILITY | 1 |
| `Install-PathVeer.ps1` legacy IranDirect service logic | MIGRATION INPUT / REQUIRED COMPATIBILITY | 16 |
| `RouteMutationJournal`/`Recovery` "IranDirect initiated" comments | PERSISTED SCHEMA semantics | several |
| Docs/historical phase files | HISTORICAL DOCUMENTATION | dominant share |

**Unexpected active product identity**: NONE. Acceptance criterion
(zero unexplained/inappropriate active IranDirect product identities) is met.

---

## 11. Fresh verification results (current tree, HEAD `e9c651e`)

```
Build (PathVeer.slnx, Debug):     0 errors, 0 warnings
Core (PathVeer.Core.Tests):       2483 passed, 0 failed, 0 skipped, 2483 total
Service (PathVeer.Service.Tests): 50 passed, 0 failed, 0 skipped, 50 total
36.8 focused (install/IPC/state/  257 passed, 0 failed, 0 skipped, 257 total
  migration/recovery/branding/
  country acceptance):
Stress (Category=Stress):         12 passed, 0 failed, 0 skipped, 12 total
Benchmarks (Release build):       0 errors (compiles cleanly; not executed)
```

Real-machine scenarios executed in 36.8: none destructive (acceptance used
code inspection + prior live observability evidence + harness tests).
Disposable-VM scenarios outstanding: full destructive legacy→PathVeer upgrade
against a real legacy SCM install, real native-route mutation on upgrade, OS
reboot persistence test, real purge/reinstall cycle (§15).

---

## 12. Safety / cleanup

- No temporary Services, native routes, or test roots created by this phase.
- Live observability stack left exactly as found (up, healthy, volumes intact).
- No `docker compose down -v`; legacy volumes preserved.
- `%ProgramData%\PathVeer` and `%ProgramData%\IranDirect` untouched.
- No temp scripts left in repo (verification used existing test suites only).

---

## 13. Known remaining limitations

- **Large-country Windows route-count performance**: tens of thousands of
  native routes per large country; operational, not a correctness blocker
  (established 18.x analysis; ~10–20% aggregation ceiling, rejected for v1).
- **RIPEstat pre-launch terms review**: business/legal pre-launch task.
- **Production code signing**: 37.x release prerequisite; not a 36.8
  rebrand correctness failure (existing code does not falsely claim signing).
- **Consumer installer/distribution (MSI/WiX, setup.exe)**: 37.x scope.
- **Destructive real upgrade / reboot / purge acceptance**: VM-required (§15).

---

## 14. Git closeout

This phase adds only `docs/branding/phase-36.8-acceptance.md`. No production
code changed. After commit + push, `git log --oneline @{u}..HEAD` is expected
empty (or contains only this phase's commit).

---

## 15. Final decision

**CONDITIONAL PASS — Phase 36.8 code/harness acceptance is complete, but the
explicitly listed disposable-VM acceptance scenarios remain before release
certification.**

Rationale:
- All deterministically executable acceptance cases (A–AB except the
  destructive real-machine subset) PASS via deterministic harnesses,
  real-process CLI/IPC inspection, and the prior live observability evidence.
- No blocking correctness defect was found in the completed 36.x work; no
  production code change was required.
- The single authority contract, Tray lifecycle contract (§L/K), state
  migration/recovery, IPC compatibility (incl. no ambiguous-send replay),
  observability identity, and uninstall/state-preservation contract are all
  established and verified.
- The remaining items are Cat-4 (disposable VM): a real legacy IranDirect
  service install → PathVeer upgrade against live SCM state, real native-route
  mutation during upgrade, an OS reboot persistence test, and a real
  purge/reinstall cycle. These require destructive manipulation of a real
  legacy installation and were not executed on this valuable workstation.

Release certification (37.x entry) should require the Cat-4 VM procedure in a
disposable VM before shipping to production. Until then, 36.x rebrand/migration
code acceptance is closed; only the real-machine destructive upgrade
certification is outstanding.

Next program boundary: 37.x — consumer Windows release/distribution
(37.1 Windows release packaging / MSI strategy / code signing / artifact
structure). Do NOT begin 37.1 in this phase.
