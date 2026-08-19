# PathVeer — DEVSIGN.10 Installer Hardening Certification Proof

Status: **AUTOMATED PROOF COMPLETE. OPERATOR LIVE ACCEPTANCE PENDING.**

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
    integration test (runs `tools/New-PathVeerPackage.ps1`, full package build)
    times out at 600 s in this environment. Integration test; not a unit test
    for these changes.
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

## 11. Remaining live gates (operator-only, NOT claimed as passed)

1. Normal Explorer Repair/Upgrade -> Service Running/Automatic, Tray count 1,
   `Elevated=False`, no ANSI, Finish.
2. Same-version Repair while Tray running -> installer quiesces Tray, no
   `Accessibility.dll` AccessDenied, binary swap succeeds, Tray count 1
   `Elevated=False`.
3. 10x Tray launches -> count stays 1.
4. Tray restart (kill -> 0, launch -> 1, `Elevated=False`).
5. Direct Run-as-admin Setup -> Tray NOT launched elevated (acceptable: Tray
   count 0 with Start-Menu guidance, or count 1 `Elevated=False` via reviewed
   mechanism). `Elevated=True` is UNACCEPTABLE.

These require the operator to run `devsign.10` on Windows and inspect with
`tools/Test-PathVeerInstallerOperatorState.ps1`.
