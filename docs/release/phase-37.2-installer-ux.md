# PathVeer Phase 37.2 — Consumer Installer UX / Windows Shell Integration

Branch: `development/service-authority`
Base: `76457cc` (Phase 37.1 release architecture)
Outcome: **CONDITIONAL PASS** — implementation complete; interactive GUI
click-through (VM-GATE-5/6/7/8) and real install/uninstall/upgrade flows
(VM-GATE-1/2/3/4) remain VM-required, not executed on the dev workstation.

## 1. Bootstrapper audit (start of 37.2)

`PathVeer.Setup` (Phase 37.1) was a **console-only** self-contained .NET 10
wrapper that:
- requested UAC elevation (runas relaunch) when not admin;
- located the co-located `PathVeer-<version>` package directory;
- extracted the embedded `Install-PathVeer.ps1` to a temp file;
- invoked it via `pwsh.exe`, forwarding only known-safe verbs;
- streamed stdout/stderr and returned the script's process exit code.

It had **no UI, no install-state detection, no structured progress, no Start
Menu / Apps&Features registration, and no runtime-prerequisite check** — the
last of which was a hard gap because the Service/CLI/Tray components are
framework-dependent .NET 10 and would fail at Service start on a runtime-less
consumer machine.

## 2. UI technology decision

**WinForms** (`net10.0-windows` + `<UseWindowsForms>true>`).

Considered: WinForms, WPF, WinUI 3, minimal native dialogs.
- WPF / WinUI 3 add heavier dependency footprints and, for WinUI 3, an external
  Windows App SDK that breaks the single-file self-contained model.
- WinForms ships inside the .NET Windows Desktop pack already present in the
  SDK, needs no extra workload, supports per-monitor DPI (manifest
  `dpiAwareness=permonitorv2`), standard accessibility, and keeps the bundle
  truly single-file.

The bootstrapper remains **self-contained single-file** (no .NET install
needed to *launch* setup). Adding the UI grew the EXE from ~83 MB to ~132 MB
(WinForms runtime included) — acceptable for a signed consumer installer.

## 3. Architecture: UI never implements install logic

The entire install/migration/state/upgrade/purge/rollback authority stays in
`Install-PathVeer.ps1` + `PathVeer.Core.Installation`. The new WinForms layer
(`InstallForm`, `InstallController`, `Program`) only:

- collects user intent (Tray startup, launch-after, purge);
- detects current state via the script's new `statejson` action;
- classifies the scenario (pure `InstallStateClassifier`);
- invokes the embedded script with structured output (`-ProgressFile`,
  `-ResultFile`);
- renders progress/result and maps failures to stable exit codes.

No install semantics were duplicated. (`none` duplicated.)

## 4. Structured progress / result contract

The script now emits:
- **progress records** — one JSON line per stage to `-ProgressFile`:
  `VerifyingPackage, StoppingLegacy, StoppingService, Installing,
   StartingService, CreatingShortcuts, RemovingShortcuts, ReleasingRoutes,
   Finished` plus failure stages `ServiceFailed, ReadinessFailed,
   DowngradeBlocked, PackageVerificationFailed, PurgeFailed`.
- **result record** — JSON to `-ResultFile`:
  `{ success, category, message, version, timestamp }`, where `category`
  ∈ {Success, UserCancelled, DowngradeBlocked, PackageVerificationFail,
  LegacyUnsupported, ServiceFailed, ReadinessFailed, UninstallFailed,
  PurgeFailed}.
- **stable exit codes** (mirrored in `PathVeer.Core.Installer.SetupExitCodes`):
  0 success; 100 user cancelled; 101 elevation denied; 102 invalid args;
  103 downgrade blocked; 104 package verification failed; 105 legacy
  unsupported; 106 service failed; 107 readiness failed; 108 uninstall failed;
  109 purge failed; 110 runtime prerequisite missing.

`InstallController` streams the progress file and reads the result file, so
the UI never scrapes human-readable host text.

## 5. Install-state detection

`Install-PathVeer.ps1 -Action statejson` returns a machine-readable object:
`productInstalled, installedVersion, serviceInstalled,
legacyIranDirectInstalled, legacyStatePresent, pathVeerStatePresent,
installRootPresent, manifestPresent, partialInstallation`. `InstallController
.DetectState()` parses it. `InstallStateClassifier.Classify` maps it + the
target package version to one of: `NotInstalled, SameVersion, Upgrade,
Downgrade, SupportedLegacyMigration, PartialOrBroken, ConflictingAuthority`.

## 6. Windows shell integration

- **Start Menu** (per-machine Common Programs): `PathVeer\PathVeer.lnk`
  (launches the **Tray**, not the Service binary), `PathVeer\PathVeer Command
  Line.lnk` (opens a terminal with the CLI), `PathVeer\Uninstall PathVeer.lnk`
  (→ `PathVeerSetup.exe --uninstall`). Created during install when
  `-RegisterShell` is passed; removed on uninstall.
- **Apps & Features**: `HKLM\Software\Microsoft\Windows\CurrentVersion
  \Uninstall\PathVeer` with `DisplayName=PathVeer`, `DisplayVersion`,
  `Publisher=PathVeer`, `UninstallString="<InstallRoot>\PathVeerSetup.exe"
  --uninstall` (quoted). Registered on install, removed on uninstall.
- **Tray startup**: user-facing "Start PathVeer Tray when I sign in"
  checkbox → passes `-InstallTray` to the authoritative `Set-TrayStartupEntry`.
  UI wording makes clear this controls the **Tray**, not the Service authority.
- **Launch after install**: optional "Launch PathVeer Tray after setup".
- **Product identity**: all consumer surfaces say "PathVeer" (no internal
  project names). Legacy IranDirect shortcuts/state are not re-created.

## 7. Runtime prerequisite strategy (mandatory, §43)

The hosted Service/CLI/Tray are framework-dependent .NET 10 apps. Before any
mutation, `Program.Main` calls `RuntimePrerequisite.Check()`, which runs
`dotnet --list-runtimes` and looks for `Microsoft.NETCore.App 10.x`. If absent:
- interactive: a warning MessageBox explains the requirement and points to
  dotnet.microsoft.com/download;
- unattended: returns exit code **110** with the same message.

The bootstrapper itself is self-contained, so it runs this check without any
runtime present. **Decision**: shift the components to self-contained is NOT
done this phase — the runtime check + clear guidance closes the consumer
prerequisite gap without silently downloading binaries. Offline installers
(that bundle the runtime) remain a documented future option; the current
release layout is a **fully offline-capable** package (all payloads embedded
in the zip / co-located package), requiring only the runtime to be present.

## 8. Flows

| Scenario | UX |
|---|---|
| Fresh | Welcome → options → Install → progress → readiness → completion (launch Tray) |
| Same version | "Already installed" → Repair / Uninstall |
| Upgrade (older→newer) | "Upgrade <old> to <new>" → preserves state |
| Downgrade (newer→older) | Blocked: "A newer version is already installed." |
| Supported IranDirect | "IranDirect detected — will migrate config/state" |
| Unsupported legacy | Fail safe, no changes, diagnostic message |
| Uninstall | Preserves `%ProgramData%\PathVeer`; optional explicit purge checkbox |
| Partial/broken | "Repair" reinstalls current version, preserves state |

Unattended (`/quiet`) reuses the same `InstallController` operations with no
window; supports `--uninstall`, `--purge-state`, `--install-tray`, `--no-tray`.

## 9. Security

- Elevation: detects non-admin and relaunches with `runas` before any work;
  declined → exit 101, no mutation.
- Temp extraction: unique `PathVeer.Setup.<random>` dir; script is extracted
  from the embedded resource (integrity-bounded by the package hash check the
  script itself performs before teardown).
- Argument safety: only known-safe verbs/flags are forwarded; no user string is
  concatenated into a PowerShell command. `InstallController` uses
  `ProcessStartInfo.ArgumentList` (structured, no shell).
- Logging: script/controller output is surfaced in the UI log box; final
  failure points the user to `%ProgramData%\PathVeer\Logs\Setup` (the existing
  PathVeer diagnostics convention). No secrets (credentials/tokens) are logged.
- Single-instance: a named `Global\PathVeer.Setup.SingleInstance` mutex prevents
  two setup processes acting on SCM/Program Files/ProgramData simultaneously.

## 10. Tests

`PathVeer.Core.Tests/Installation/InstallerControllerTests.cs` (26 tests):
state classification for all 7 scenarios, version comparison (prerelease-
insensitive), runtime-parse (highest-matching 10.x), exit-code mapping
(mirrors PowerShell contract), Start Menu path generation, uninstall
registration quoting, and state-JSON round-trip.

## 11. VM certification matrix

Inherited from 36.8 / 37.1:
- VM-GATE-1: supported IranDirect→PathVeer real SCM upgrade
- VM-GATE-2: real native-route mutation/recovery during upgrade
- VM-GATE-3: Windows reboot persistence
- VM-GATE-4: real purge → reinstall lifecycle

New for 37.2 installer UX (NOT executed on dev workstation):
- VM-GATE-5: interactive consumer installer fresh install
- VM-GATE-6: interactive PathVeer in-place upgrade
- VM-GATE-7: interactive supported IranDirect migration UI
- VM-GATE-8: Apps & Features uninstall + preserved-state reinstall

## 12. Known deferred work

- 37.3 release manifest / update architecture (channels, signed update
  metadata, latest-version, update discovery, safe handoff to installer).
- 37.4 pathveer.com distribution.
- Production Authenticode signing certificate (still not provisioned —
  signing pipeline fail-closed and validated).
- VM release certification (GATE-1..8).
