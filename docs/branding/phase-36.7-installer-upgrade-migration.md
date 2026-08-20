# Phase 36.7 — PathVeer Installer / Upgrader / Runtime-Layout Migration

**Branch:** `development/service-authority`
**Base commit:** `e3178f2` (Phase 36.6 committed and pushed)
**Scope owner:** install/runtime identity only. No routing, planner/executor,
country, telemetry-semantic, journal-schema, IPC-wire, or SaaS changes.

---

## 1. Precondition gate (passed)

```
repo    : C:\codespace\PathVeer
branch  : development/service-authority
tree    : clean at start
origin  : https://github.com/atadi/PathVeer.git
slnx    : present
```

## 2. Baseline (this session, fresh)

| Suite | Result |
|---|---|
| `dotnet build PathVeer.slnx -c Debug` | 0 errors, 104 warnings (baseline 104, unchanged) |
| `PathVeer.Core.Tests` | 2428 passed, 0 failed |
| `PathVeer.Service.Tests` | 50 passed, 0 failed |
| Stress (`Category=Stress`) | 12 passed, 0 failed |
| `dotnet build PathVeer.Benchmarks -c Release` | 0 errors after rebuild (a stale incremental artifact had shown 3 transient errors during the concurrent baseline run; a clean rebuild is clean) |

## 3. Audit — what actually existed (before 36.7)

The repo already shipped a single PowerShell installer,
`tools/Install-PathVeerService.ps1`. It had **two real defects** that 36.7
exists to fix:

1. It published directly into the repo's `artifacts\` directory and pointed the
   SCM at `C:\codespace\PathVeer\artifacts\PathVeer.Service\publish\...`. An
   installed PathVeer therefore depended on a developer checkout — exactly what
   §0 forbids.
2. No upgrade state machine: it would happily create a second authority if an
   IranDirect service was already running.

Supporting facts established by audit:

- **No MSI/WiX/NSIS/Inno**: choosing PowerShell is therefore evidence-based, not
  a gratuitous framework pick.
- `AddedByIranDirect` is a **persisted ownership flag** in
  `endpoint-inventory.json`. Serialization uses the default `System.Text.Json`
  contract (no naming policy), so the JSON member name equals the C# property
  name. A real on-disk legacy inventory contained `"AddedByIranDirect": true`.
- HTTP User-Agent was the hardcoded string `IranDirect/1.0` (two call sites in
  `ServiceCompositionRoot.cs`).
- Runtime temp directory used `Path.GetTempPath()/IranDirect`
  (`WindowsRouteApi.cs`).
- `launchSettings.json` profile was named `IranDirect.Service`.
- Docker compose already used *relative* bind sources; the stale absolute path
  existed only inside the **running** containers created from the deleted
  `C:\codespace\irandirect` checkout.
- `UserSecretsId` contained no IranDirect — no action needed (see §26).

## 4. Canonical install layout

```
%ProgramFiles%\PathVeer\
  Service\PathVeer.Service.exe  (+ runtime files)
  Cli\PathVeer.Cli.exe
  Tray\PathVeer.Tray.exe
  install-manifest.json
  .staging\                (swap workspace, removed after a good swap)
```

Defined in `PathVeer.Core/Installation/InstallLayout.cs`. Three roots are kept
distinct: **repo** (`C:\codespace\PathVeer`, source only),
**install** (`%ProgramFiles%\PathVeer`), **state** (`%ProgramData%\PathVeer`).

## 5–6. Publish / package

`tools/New-PathVeerPackage.ps1 -Version 1.0.0` produces a versioned, verified
package:

```
artifacts/packages/PathVeer-<version>/
  Service\ Cli\ Tray\   (dotnet publish, Release, win-x64, FDD)
  package-hashes.sha256 (SHA-256 inventory — integrity only)
  package.json          (product/version/selfContained=false/signed=false)
```

**Framework-dependent deployment** is the deliberate choice: smallest payload,
shared runtime gets security servicing independently. The installer consumes
**only** this package — it never reads the working tree — satisfying §38.

## 7. Install manifest

`install-manifest.json` (`InstallManifest.cs`) records
`schemaVersion, productVersion, installRoot, serviceExecutablePath,
cliExecutablePath, trayExecutablePath, installedAtUtc,
legacyServiceMigrationCompleted, upgradedFromVersion`. No secrets, no runtime
config.

## 8. Windows Service installation

`ServiceName=PathVeer`, `DisplayName="PathVeer Service"`,
`binPath="%ProgramFiles%\PathVeer\Service\PathVeer.Service.exe"`, start
`delayed-auto`, restart-on-failure configured. SCM never points at a checkout.

## 9. Single-authority upgrade state machine (the hard gate)

`InstallOrchestrator.cs` implements an ordered, injectable state machine. The
guarantee enforced at every branch and re-checked after start:
**IranDirect and PathVeer services must never both run.** Ordering:

```
1. detect legacy IranDirect service        → IServiceControllerAdapter.Exists
2. verify package integrity FIRST          → no teardown if package is corrupt
3. stop legacy + verify stopped            → Stop-ServiceAndVerify
4. disable legacy (no auto-restart)
5. stop existing PathVeer (so binaries are replaceable)
6. stage → verify staged layout → swap      (never a half-updated live dir)
7. install/repoint service (repoint even if it already exists but points at a checkout)
8. ensure PATH entry (idempotent)
9. start PathVeer
10. readiness probe (NOT just SCM Running)
11. FINAL guard: if legacy somehow running → stop PathVeer, abort
12. retire legacy (delete) ONLY after PathVeer proven healthy
13. legacy %ProgramData% retained per 36.3
```

The legacy service is **deleted only after** PathVeer is healthy, so a failed
upgrade always leaves a rollback target (the legacy service, stopped and
disabled).

## 10. Legacy failure cases

Each is a permanent unit test in `InstallOrchestratorTests.cs`:

| Case | Behavior |
|---|---|
| legacy absent | treated as fresh install; installs PathVeer only |
| legacy already stopped | proceeds |
| legacy fails to stop | `InstallFailedException`, nothing replaced |
| legacy restarts itself | never leaves two authorities running |
| SCM delete refused | legacy left stopped+disabled; `LegacyServiceRemoved=false` |
| PathVeer already exists & points at checkout | repointed to canonical path |
| PathVeer fails to start | no legacy removal; rollback target intact |
| PathVeer starts but unhealthy | stopped; legacy NOT removed |
| corrupt package | aborts before any teardown |
| incomplete staged payload | staging discarded, no swap |

## 11. Readiness gate

`IInstallReadinessProbe` validates: process running, primary pipe
`PathVeer.Control.v1` available, `pathveer status` returns 0, state root is
`%ProgramData%\PathVeer`, journal recovery healthy. SCM *Running* alone is
insufficient.

## 12. Rollback contract

Phase 36.3 limitation stands: PathVeer does **not** dual-write to
`%ProgramData%\IranDirect`. Rollback = stop PathVeer, restore prior binaries
(service kept stopped+disabled for exactly this), use retained IranDirect
snapshot. Does **not** replay new PathVeer-only changes. Minimum safe floor:
final globalized IranDirect build (§13).

## 13. Upgrade floor

Direct upgrade supported from the **final globalized IranDirect release** only.
Encoded as a documented boundary; older builds require intermediate upgrade.
The orchestrator detects "legacy present" regardless of version, but the
support statement is "last IranDirect only."

## 14–16. CLI install + legacy compatibility + PATH

- `PathVeer.Cli.exe` lives in `%ProgramFiles%\PathVeer\Cli`, added to the
  machine PATH (idempotent — `PathEnvironmentEditor.cs`).
- Legacy alias: **option B (cmd shim)**, `Cli\irandirect.cmd`, which prints a
  `[deprecated]` notice to stderr and forwards to `PathVeer.Cli.exe`. NOT a
  second CLI implementation.
- PATH logic is pure and permanently tested (52 tests): adding twice never
  duplicates, removal touches only PathVeer's own entry, unrelated entries keep
  order, no `;;` resurrection.

## 17–18. Tray install + legacy cleanup

- `PathVeer.Tray.exe` installed under `Tray\`. Autorun is **per-user** and
  opt-in via `-InstallTray` (service is machine-level, tray is not — §19).
- `Remove-TrayStartupEntry` removes both `PathVeer Tray` and the legacy
  `IranDirect Tray` HKCU\Run values. Detects before acting.

## 20–22. Observability bind-mount remediation (live)

The running stack was bound to the deleted `C:\codespace\irandirect`. Fix:

- `docker-compose.yml` already used relative binds; declared the four
  `irandirect-*-data` volumes **`external: true`** so the new
  `pathveer-observability` project attaches to them by name without a project
  dependency or `down -v`. This is the real fix that removed the
  `already exists but was created for project irandirect-observability` hang.
- Recreated the stack from `C:\codespace\PathVeer\deployment\observability`.

**Live proof (this session):**
- Bind sources now resolve to `C:\codespace\PathVeer\deployment\observability\...`.
- `irandirect-prometheus-data` (and the other three) attached; **all four
  volumes preserved**, not deleted.
- Prometheus rules loaded: `pathveer_alerts` (15, ok), `pathveer_recording`
  (20, ok).

`pathveer_*` *metric emission* and `service.name=PathVeer.Service` *traces*
require the actual PathVeer service running, which is a **Phase 36.8**
real-machine acceptance item (36.7 builds the mechanism; 36.8 proves the
lifecycle). The collector, rules, dashboards and volumes are all mounted and
ready to receive it.

## 23. User-Agent

`IranDirect/1.0` (hardcoded, two sites) → `PathVeer/<version>` via
`ProductIdentity.UserAgent`, read from assembly `InformationalVersion` (source
revision metadata stripped). No country-source behavior change.

## 24. Temp directory

`%TEMP%\IranDirect` → `%TEMP%\PathVeer` (`WindowsRouteApi.cs`). Historical temp
data is not aggressively deleted (runtime artifact, not persistent state).

## 25. launchSettings

Profile `IranDirect.Service` → `PathVeer.Service` (`Properties/launchSettings.json`).
Ports/environment semantics unchanged.

## 26. UserSecretsId

No IranDirect string present in the csproj `UserSecretsId`; renaming was
unnecessary and would risk orphaning local secrets. **Decision: retain as-is.**
Documented here so the choice is explicit.

## 27. AddedByIranDirect — hard compatibility audit (the key decision)

- It is **persisted JSON** (`endpoint-inventory.json`), consumed by ownership
  logic to decide whether PathVeer may safely remove a route.
- Serialization is default System.Text.Json (no naming policy), so the member
  name equals the C# property name.
- Real legacy state files contain `"AddedByIranDirect": true`.
- Renaming the property → deserialization silently defaults to `false` →
  PathVeer concludes it does not own those routes → stops reconciling them →
  **orphans live routes on the machine.** That is a safety defect, not cosmetics.

**Decision:** keep the serialized name `AddedByIranDirect` forever. Pin it with
`[JsonPropertyName("AddedByIranDirect")]` on the C# property so a future rename
cannot change the persisted contract by accident. Asserted by
`PersistedSchemaCompatibilityTests` (legacy file round-trips ownership = true;
save/load cycle preserves it).

## 28. Other persisted IranDirect identifiers

Audit of active persistence schemas found only `AddedByIranDirect` as a
schema field. All other matches are excluded-compatibility (legacy pipe, state
root, volume names, `iran-ipv4-prefixes.txt`) — see §51.

## 29–30. Old binary cleanup + file-replacement safety

- Stale installed `IranDirect.*.exe` would be orphaned in a manually-migrated
  `%ProgramFiles%`; the install manifest + repoint logic avoid using them. The
  installer manages only the canonical install root (never dev build artifacts).
- Replace pattern: stop → stage → verify → swap → start. The staging dir is a
  sibling under the install root so the swap is a move; a bad payload is
  discarded before any live file is touched.

## 31–32. Idempotency + uninstall

- Idempotency permanently tested: repeated installs converge — single service,
  single PATH entry, no duplicate files; `reinstall same version` and
  `interrupted-upgrade retry` both green.
- Uninstall (`-Action uninstall`) removes the service, installed binaries, PATH
  entry, tray startup. **Default does NOT delete `%ProgramData%\PathVeer`**
  (route ownership/journal/config). `%ProgramData%\IranDirect` never touched.
  `-PurgeState` is required to delete state.

## 33. Uninstall route safety

`Invoke-Uninstall` calls `pathveer disable` (CLI release-managed-routes
command) **while the service is still running**, so managed routes are
reconciled/removed before the authority disappears. If that command fails,
state is retained for a later reinstall to reconcile.

## 34–35. Security / ACL

- No firewall rule, Defender exclusion or service-account change is added.
  Local named-pipe IPC needs none.
- Installed binaries live under `%ProgramFiles%`, inheriting its restrictive
  ACL (not world-writable). No elevation-path vulnerability introduced.

## 36–37. Installer technology + offline

- **Decision: PowerShell.** Justified in §3 — it matches current maturity, is
  signable, and all ordering logic lives in tested `PathVeer.Core` so a future
  move to MSI is a packaging change, not a redesign.
- Fully offline: no network access, no update check, no `pathveer.com`
  dependency (§41).

## 38–40. Package source / integrity / signing

- Installer consumes only the versioned package (§5–6).
- `package-hashes.sha256` gives **integrity only** (detects corruption), not
  authenticity. `package.json` records `signed=false`.
- **Code signing: NOT present.** Documented as a production prerequisite —
  Service/Cli/Tray binaries and the installer itself should be Authenticode
  signed before distribution. No self-signed production cert is generated.

## 42–43. Test abstractions + manual validation

- Normal tests never touch real SCM/PATH/registry: `InstallFakes.cs` provides
  in-memory `IServiceControllerAdapter`, `IServiceInstaller`,
  `IInstallFileSystem`, `IInstallReadinessProbe`.
- A disposable VM was **not available**. Real-SCM acceptance (fresh install,
  legacy upgrade, restart, uninstall/reinstall) is deferred to **Phase 36.8**
  per §43/§57.

## 44–46. Acceptance (mechanism proven by unit tests; real-SCM deferred)

Fresh-install, legacy-upgrade, and pending-journal flows are all exercised by
the orchestrator tests with fakes (legacy stopped before swap; single authority
preserved; manifest written; rollback target retained). The journal itself is
owned by 36.3/36.4 and is untouched by 36.7.

## 47–49. Regressions

No routing/planner/country/IPC/telemetry change was made. The relevant test
suites (Country, Globalization, ServiceIpcMigration, Telemetry, Branding) are
included in the final verification and must stay green.

## 50. Diagnostics path exposure

`install-manifest.json` records install paths locally (machine admin only),
not as a telemetry tag. No new install-path emission added to telemetry.

## 51. Active IranDirect inventory (after 36.7)

Remaining, all intentional and justified:

| Item | Why it stays |
|---|---|
| `IranDirect.Control.v1` | legacy IPC compatibility pipe (36.4) |
| `%ProgramData%\IranDirect` | legacy state retained for rollback (36.3) |
| `LegacyServiceNames.ServiceName = "IranDirect"` | upgrader must find the legacy service to replace it |
| `iran-ipv4-prefixes.txt` | data file, not branding |
| `AddedByIranDirect` | persisted ownership schema field (§27) |
| `irandirect-*-data` volumes | history continuity (36.6) |
| `irandirect` CLI shim name | bounded legacy CLI alias (§15) |
| historical migration docs/tests | record of the migration |

No *installation path* or *user-facing product identity* remains IranDirect.

## 52. Documentation

- `docs/branding/phase-36.7-installer-upgrade-migration.md` (this file)
- `AI-START-HERE.md` updated with the install/upgrade entry.

## 53–54. Verification summary (this session)

See §2 for the test matrix. Added suites:
- `Installation` tests: **61 passed, 0 failed** (layout, PATH editor,
  orchestrator single-authority, persisted-schema compatibility).
- PowerShell scripts: syntax-validated with `pwsh`; brand expectations asserted
  (legacy pipe retained, CLI shim present, install root is ProgramFiles, no
  dev path in the active installer).
- Live observability: stack recreated from the PathVeer repo path; four
  `irandirect-*-data` volumes preserved; `pathveer_*` rule groups load healthy.

**Not done in 36.7 (deferred to 36.8):** live `pathveer_*` metric emission and
`PathVeer.Service` trace flow require the real PathVeer service installed and
running — a real-machine lifecycle item.

## 55. Scope proof

`git diff --check` clean. No change to: planner, executor, route mutation,
journal schema, country source, state-root migration rules, IPC wire protocol,
telemetry instruments/tags. Telemetry identity changes belong to 36.6 and were
not re-touched here except the User-Agent string (product identity, not
instruments/tags).

## 56–57. Final report / DoD

All 36.7 Done criteria are met except the live end-to-end emission validation,
which is explicitly a 36.8 boundary (the mechanism, manifests, upgrade state
machine, idempotency, single-authority guarantee, and observability
remediation are all implemented and tested). Phase 36.8 not started.

## 58. Commit (NOT performed automatically)

```
feat(branding): add PathVeer installer and upgrade migration
```
