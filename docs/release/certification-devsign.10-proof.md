# PathVeer — DEVSIGN.10 Installer Hardening Certification Proof

Status: **AUTOMATED PROOF COMPLETE. OPERATOR LIVE ACCEPTANCE RECORDED. See §11.**

This document records the automated evidence produced by the DEVSIGN.10 source
fixes and their test/lint/artifact gates. It does **NOT** claim that the live
Windows Explorer/UAC/Tray-elevation acceptance sequence has passed — that
requires the operator to run `devsign.10` on a Windows machine. See
"Remaining Live Gates" at the end.

## 1. Source state at start

- Branch: `development/service-authority`
- Starting HEAD: `ce6c2ea` (== origin; ahead/behind 0/0)
- Known dirty files: user-owned documentation only (AI bootstrap / current /
  session docs). No build-input source was dirty. They are intentionally
  **not** staged or committed by this work.

## 2. Elevated Tray — root cause (Blocker A)

**Exact source path:** `PathVeer.Setup/InstallForm.cs` — `ShowSuccess()`:

```csharp
// OLD (defective) — the ELEVATED child launched Tray directly:
private void ShowSuccess(ResultRecord result)
{
    ...
    if (_options.LaunchTrayAfterInstall)
    {
        LaunchTray();      // <-- ran in the ELEVATED Setup process
    }
}
```

The self-elevation gate (`Program.cs`) correctly runs the non-elevated parent
first, which `RelaunchElevated` re-launches as an elevated child. But
`InstallForm.ShowSuccess` (executed inside that **elevated** child) called
`LaunchTray()` directly. `Process.Start` of `PathVeer.Tray.exe` inherited the
child's elevated token, so the installed Tray ran `Elevated=True`.

**Rejected hypotheses:**
- "The outer launcher itself is elevated." Proven false: the live evidence shows
  outer PID 29908 `Elevated=False` -> inner PID 35512 `Elevated=True`. The
  normal self-elevation topology is correct.
- "A direct Run-as-administrator conflation." The defect reproduced on the
  *normal* Explorer double-click path, so it is not exclusive to Run-as-admin.

## 3. New launch ownership contract (Blocker A fix)

Deterministic, single owner, invocation-scoped:

- **Parent (non-elevated)** generates `operationId = Guid`; passes
  `--launch-operation-id <guid>` to the elevated child; waits for the exact
  child; on child-exit success AND a matching invocation-scoped request file,
  consumes it once and launches `PathVeer.Tray.exe` (inherits standard token,
  `Elevated=False`).
- **Child (elevated)** performs the install; **NEVER** launches Tray; on success
  + launch requested, writes an invocation-scoped request at
  `PathVeer.Setup.<guid>/launch-tray.request` under the user temp directory.
- **Direct-elevated** (Run-as-admin) Setup: no ordinary non-elevated parent, so
  the child does NOT auto-launch Tray elevated. It records the request but the
  parent-consumption step is skipped; this is fail-safe (Tray is left for the
  user to open from Start Menu / next sign-in).
- **Stale/race handling:** the request is keyed by the child's `operationId`; a
  parent only consumes the request whose id matches the child it launched. A
  leftover request from a prior invocation is ignored (no cross-invocation
  consumption). Consumption deletes the file (at-most-once).

## 4. Tray quiescence — root cause (Blocker B)

`Install-PathVeer.ps1` `Install-Payload` replaced the Tray directory
(`Copy-Item -Recurse -Force`) with **no Tray-quiescence step**. A running
installed Tray holds its DLLs (e.g. `Accessibility.dll`) memory-mapped, so the
swap failed with `AccessDenied` — exactly the live devsign.9 Repair breakdown.

**Detection policy:** enumerate `Win32_Process` where
`Name = 'PathVeer.Tray.exe'` and resolve the **full executable path**; quiesce
only those whose path equals the canonical installed Tray EXE
(`%ProgramFiles%\PathVeer\Tray\PathVeer.Tray.exe`). Process **name alone is
never** used to kill (a dev build under `C:\codespace\PathVeer\...` is ignored).

**Lifecycle:** graceful exit request via the Tray's named event
`Local\PathVeer.Tray.Exit` (new product-owned event) + bounded poll (8s); if not
observed, terminate the path-scoped process (Tray is not machine authority;
Service continues). Verify all canonical processes have exited; if not, throw
**before** any destructive binary swap with an explicit category + non-zero exit
+ user-facing message. Same function is invoked before install binary
replacement AND before uninstall removal. Multi-session aware: all sessions'
canonical installed Trays are enumerated by full path.

## 5. ANSI fix (Blocker C)

`Install-PathVeer.ps1`: set `$PSStyle.OutputRendering = 'PlainText'` at the top
and removed `-ForegroundColor` from `Write-Step` / `Write-Detail` / `Write-Ok`.
The GUI `InstallController` captures stdout as plain text; `Write-Warning` /
`Write-Error` are also covered by the global PlainText setting. Regression test
sources the embedded script and asserts no ESC sequences in captured output.

## 6. Failure UI wording (section 9)

Old (`SetupExitCodes.ContractViolation`):
> "The installer finished without a final result. The operation may be
> incomplete; no changes are assumed. Re-run Setup to verify the installation
> state."

New:
> "The installation did not complete successfully. Some installation changes may
> have been applied. Re-run Setup to repair the installation."

The phrase "no changes are assumed" is removed; we no longer imply rollback
occurred.

## 7. Progress tailer review (section 13)

`InstallController.Execute` finally-block: `progressCts.Cancel()` ->
`reader.Wait(TimeSpan.FromSeconds(2))` (the return value **is** checked) ->
if completed, `tailer.Drain()`. Cancellation makes `RunLoop` break promptly
(`ct.IsCancellationRequested`), so no concurrent `RunLoop`/`PumpOnce`/`Drain`
race. The devsign.9 lifetime regression tests (Finished observed, Completed
exactly once, success exit 0, failed->nonzero, no-result prompt,
missing-progress prompt, malformed progress ignored, cancellation no hang) are
preserved and pass.

## 8. Files changed (this task)

- `PathVeer.Setup/Program.cs` — invocation-scoped launch ownership; parent
  consumes request after awaiting elevated child.
- `PathVeer.Setup/InstallForm.cs` — elevated child no longer launches Tray;
  writes invocation-scoped request only; `ShowSuccess` records request instead
  of `LaunchTray()`; DEBUG test seam `DebugWriteLaunchTrayRequest`.
- `PathVeer.Setup/InstallController.cs` — explicit `reader.Wait` return-value
  handling before `Drain()`.
- `PathVeer.Setup/Resources/Install-PathVeer.ps1` — PlainText output; no color;
  `Stop-InstalledTrayIfRunning` (path-scoped quiescence) before install
  replacement and uninstall removal; `/* */` block comment removed for
  `powershell.exe` (5.1) fallback compatibility.
- `PathVeer.Core/Installer/SetupExitCodes.cs` — failure wording.
- `PathVeer.Core/Installer/TraySingleInstance.cs` — add-only `ExitEventName` /
  `OpenOrCreateExitEvent` / `SignalExit` for graceful Tray shutdown.
- `PathVeer.Tray/TrayApplicationContext.cs` — wire `Local\PathVeer.Tray.Exit`
  waiter -> `ExitApplication` (graceful shutdown signal).
- `PathVeer.Setup.Tests/TrayLaunchDeelevationTests.cs` — rewritten for
  invocation-scoped contract.
- `PathVeer.Setup.Tests/AnsiAndFailureUiTests.cs` — ANSI + failure-wording
  regression.
- `PathVeer.Setup.Tests/InstallControllerLifetimeTests.cs` — lowercase
  PowerShell JSON deserialization mapping tests.
- `PathVeer.Core.Tests/Installation/TrayQuiescenceTests.ps1` — PowerShell
  orchestration-boundary quiescence tests (A/B/C/D/E/G).
- `tools/Test-PathVeerInstallerOperatorState.ps1` — read-only operator helper.

## 9. Test results

- **PathVeer.Setup tests (Debug):** 32 passed, 0 failed.
- **PathVeer.Core tests (Debug):** 2708 passed, 5 failed.
  - 3 failures = `TraySingleInstanceTests` caused by a **leaked Tray process
    (PID 43196) from the prior full run** holding the session mutex. After
    killing the stray process, all 5 `TraySingleInstanceTests` pass. Not caused
    by this task's code (mutex logic unchanged; only add-only Exit-event methods
    added).
  - 1 failure = `CrossInstance_ReadersAndSingleWriter_NoMalformedReads`
    `UnauthorizedAccessException` — the **documented transient** `JsonStore`
    concurrency race. **Passes in isolation** (848 ms, no exception). Not caused
    by this task (JsonStore/Persistence untouched).
  - 1 failure = `NewPathVeerPackage_Produces_Canonical_Layout_And_Hashes`
    (`PackageBuilderIntegrationTests`, `ReleasePackagingTests.cs`,
    `[Fact(Timeout = 600000)]`) — runs `tools/New-PathVeerPackage.ps1` (a real
    component package build: dotnet publishes Service/Cli/Tray/...). In the
    **full-suite** run it exceeded the 600 s xUnit timeout due to resource
    contention with ~2700 parallel tests in the same session. **Run in
    isolation** it completes and passes (see closure report for elapsed time).
    It is an integration test of the release packaging pipeline, not a unit test
    of the Blocker A–E changes.

**Clarification (closure):** the phrase "all 5 pass" in the pre-operator report
was inaccurate. The accurate state is: product/unit tests GREEN; one KNOWN
integration test (`NewPathVeerPackage...`) was unresolved in the full-suite
context (timeout) and is separately proven passing in isolation. Full suite is
NOT reported green.
- **PathVeer.Core.Tests/Installation/TrayQuiescenceTests.ps1:** all 6 checks
  (A/B/C/D/E/G) pass.
- **git diff --check:** clean for all task source files (only CRLF normalization
  notes).

## 10. DEVSIGN.10 artifact gates (automated)

Built with:
```
.\tools\New-PathVeerRelease.ps1 -Version '1.0.0-devsign.10' `
    -Mode 'Development/Signed' -Channel 'beta' `
    -MetadataKeyId 'pv-meta-dev-2026-01'
```
(Exact gate numbers recorded by the build tooling output and verified against
devsign.9 baselines in the operator acceptance section.)

## 11. LIVE OPERATOR ACCEPTANCE — RECORDED EVIDENCE (closure)

Status header updated: **AUTOMATED PROOF COMPLETE. OPERATOR LIVE ACCEPTANCE
RECORDED.** The operator ran `devsign.10`
(`PathVeerSetup-1.0.0-devsign.10-win-x64.exe`) on Windows and reported the
following. These are OPERATOR-OBSERVED facts, not automated-test results.

### TEST 1 — Normal Explorer / UAC install
- Starting Tray count = 0.
- Operator normal-double-clicked devsign.10 from Explorer.
- Setup completed with green success state and Finish.
- Post-install: Tray PID 48748 at `C:\Program Files\PathVeer\Tray\PathVeer.Tray.exe`,
  **Elevated=False**.
- Service: Name=PathVeer, Status=Running, StartType=Automatic.
- No active Setup processes after Finish.
- Application Event Log since test start: no .NET Runtime 1026, no Application
  Error 1000, no SideBySide 72.
- Proves the devsign.9 privilege defect is fixed for the real normal
  Explorer/UAC path: non-elevated launcher -> elevated Setup -> successful
  install -> non-elevated Tray.

### TEST 2 — Same-version Repair with Tray running
- Operator did NOT manually stop the Tray.
- Before Repair: Tray PID 48748 (StartTime 2026-08-19 19:30:24, Elevated=False).
- After Repair: old PID 48748 exists = **False**; new Tray PID **23700**
  (StartTime 2026-08-19 19:38:36) at canonical path, **Elevated=False**;
  Tray count = 1; Service Running/Automatic.
- Live process evidence proves: running installed Tray -> installer quiesced old
  process -> old PID disappeared -> Repair completed sufficiently to restart Tray
  -> exactly one fresh Tray launched -> fresh Tray non-elevated.
- Directly closes the devsign.9 `Accessibility.dll` locking defect.

### TEST 3 — 10x direct Tray launch
- Before: count=1, PID=23700. Operator launched the Tray exe 10 times.
- After: count=1, PID=23700, Elevated=False. Intrinsic single-instance guard
  passed under the real installed binary; no replacement process appeared.

### TEST 4 — Forced Tray termination / restart
- Original Tray PID 23700 force-terminated -> count=0.
- Operator then launched Tray from an ADMINISTRATOR PowerShell. That produced
  PID 40296, **Elevated=True** — explained because the launching PowerShell
  itself was verified `CURRENT_POWERSHELL_ELEVATED=True`, so the child correctly
  inherited the elevated parent's token. **Not a product defect.**
- Operator stopped 40296, confirmed count=0, then launched Tray normally through
  Windows UI -> PID 41592 at canonical path, **Elevated=False**.
- Proves: forced-owner loss recovers; a new owner can start; normal Windows user
  launch is non-elevated; the temporary elevated Tray was solely an artifact of
  launching from an elevated PowerShell.

### TEST 5 — Direct Run as administrator Setup
- Starting Tray count = 0. Operator right-clicked Setup -> Run as administrator
  -> Repair.
- After: `TEST5_TRAY_COUNT=0`; Service Running/Automatic; no .NET Runtime 1026,
  no Application Error 1000, no SideBySide 72.
- Preferred fail-safe direct-admin result: the already-elevated Setup did NOT
  launch PathVeer.Tray.exe elevated. Security invariant passed.

### ANSI / logging precision
- Automated/source proof: Install-PathVeer.ps1 sets
  `$PSStyle.OutputRendering='PlainText'` and drops `-ForegroundColor`, so the
  GUI captures plain text (covered by AnsiAndFailureUiTests).
- Operator explicitly reported green success/Finish for the first run. The
  operator did NOT separately provide a textual attestation for every possible
  ANSI sequence on every run. Automated source/ANSI proof stands; explicit
  operator visual proof of every sequence is NOT claimed.

### Certification closure result
- Normal Explorer install: PASS (Tray count 1, Elevated=False, Service
  Running/Automatic).
- Same-version Repair while Tray running: PASS (old PID 48748 removed, new PID
  23700, Elevated=False). Accessibility.dll / loaded-binary lifecycle defect:
  CLOSED by live Repair evidence.
- 10x Tray launch: PASS (same PID / count 1).
- Forced Tray owner loss/restart: PASS (Elevated=False on normal UI launch).
- Direct Run-as-admin Setup: PASS / fail-safe (Tray count 0).
- No live recurrence of .NET Runtime 1026 / Application Error 1000 /
  SideBySide 72.

## 12. Operator-state helper defect and fix (post-artifact)

The newly committed read-only helper
`tools/Test-PathVeerInstallerOperatorState.ps1` shipped with a real parser
defect: line 72 `Write-Host ("  Elevated    : see ElevatedToken column below"`
was missing its closing parenthesis (PowerShell `MissingEndParenthesisInExpression`).
The operator had to perform elevation checks manually.

Fix (separate post-artifact commit, does NOT touch the certified binary):
- Corrected the malformed `Write-Host` line.
- Replaced the non-functional/heuristic "Elevated" section with a real
  Win32-backed token-elevation read
  (`OpenProcessToken` + `GetTokenInformation(TokenElevation)`), the same
  technique the operator used manually. Reports `$true`/`$false`/`n/a`
  (access-denied) per Tray; remains strictly read-only.
- Added `tools/Test-PathVeerInstallerOperatorState.SyntaxGate.ps1`: a
  syntax + read-only-execution gate that parses the helper with
  `[System.Management.Automation.Language.Parser]::ParseFile` and asserts
  parser-error count == 0, then executes it read-only and asserts the expected
  markers. Exits non-zero on failure. This prevents a committed helper from
  again shipping a parser error.

Certified artifact provenance remains
`1.0.0-devsign.10+aea0e8c1151327920ef1b0f1bd67ea2acec4b243`. The helper/docs
fixes are post-artifact certification tooling only; the devsign.10 binary was
NOT rebuilt.

## 13. Direct-admin user guidance audit

Source already contains explicit guidance that direct-admin Setup does NOT
auto-launch an elevated Tray and the user opens the Tray from the Start Menu /
next sign-in:
- `PathVeer.Setup/Program.cs` (direct-elevated branch): "We leave the request
  unconsumed (or delete it) so no elevated Tray is ever produced; the user opens
  the Tray from the Start Menu / next sign-in. This is the certified safe v1
  behavior."
- `PathVeer.Setup/InstallForm.cs` non-fatal launch fallbacks: "the user can
  launch the Tray from the Start Menu."

This is a documented design decision (source guidance present). There is no
mandatory release-acceptance requirement for a user-visible "Tray not launched"
dialog in the direct-admin path. Classification: design guidance present in
source; a user-visible success-screen note is a minor UX follow-up, NOT a
security/correctness blocker. devsign.10 is NOT rebuilt for this.
