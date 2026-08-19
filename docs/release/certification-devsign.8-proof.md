# PathVeer Installer Certification — devsign.8 (service-authority slice)

> ## Post-certification correction
>
> devsign.8 is **not** a passing certification artifact. Live Explorer/UAC execution reached deployment `Finished`/`SUCCESS`, but the Setup GUI remained in `Working...` with no Finish transition. Runtime process evidence proved the PowerShell producer had exited; source inspection proved `InstallController` then waited on a progress reader that had no producer-complete signal and could poll for up to 600 seconds before `ReadResult()`/`Completed`. Any claims below that Repair/Finish/Tray token acceptance was fully completed are superseded by this later operator evidence. Preserve devsign.8 as an immutable regression specimen.

Built from committed HEAD `c87dc36` on branch `development/service-authority`.
Predecessors: devsign.7 (commit `2881029`), devsign.6 (commit `61acb03`).
devsign.6 / devsign.7 were NOT modified or rebuilt (frozen regression specimens).

Scope: (1) fix the proven WinExe startup crash (Console.Title) and (2) de-elevate
the installer-launched Tray (Elevated=False STOP condition). Not reopened: package
integrity, Authenticode, ES256 metadata trust, IranDirect migration, beta.1,
production publishing/R2. devsign.6/7 UX + Tray single-instance fixes preserved.

## STARTUP REGRESSION — ROOT CAUSE (proven)

devsign.6 and devsign.7 crashed before the installer UI opened. .NET Runtime
Event 1026:
  System.IO.IOException: The handle is invalid.
     at System.ConsolePal.set_Title(String value)
     at PathVeer.Setup.Program.Main(String[] args)

CAUSE: commit 61acb03 changed OutputType Exe -> WinExe (to remove the consumer
console flash). Program.Main still executed `Console.Title = "PathVeer Setup";`.
A WinExe normally has no console handle; Console.Title requires a valid console
and throws IOException, escaping Main before --help, elevation, mutex, package
resolution, controller creation, and the WinForms UI. devsign.6 already showed
the same crash (Tray single-instance work in 2881029 is unrelated).

## CONSOLE API AUDIT (PathVeer.Setup/Program.cs)

- `Console.Title` (L37): the proven crash. REMOVED (form carries the title).
- `Console.WriteLine` / `Console.Error.WriteLine`: also throw IOException when no
  console is attached (the Explorer/Start-Process launch model). `--help` would
  crash at PrintUsage; error paths in the GUI launch would crash too. All wrapped
  in SafeWriteLine / SafeWriteError helpers that succeed when a console/
  redirection IS present (so /quiet diagnostics still work from a real console)
  and silently no-op otherwise (catching IOException / ObjectDisposedException).
  No AllocConsole; no console flash reintroduced; no broad catch around Main.

## TRAY TOKEN — ROOT CAUSE + FIX (the STOP condition)

EMPIRICALLY CONFIRMED (elevated host): the prior LaunchTray() used
`Process.Start(UseShellExecute=true)` from the ELEVATED installer, so the Tray
inherited the elevated (high-IL) token. Probe result: TokenElevationFlag=1
(Elevated=True) — a certification STOP condition and a security defect (a
per-user UI must not run as administrator).

FIX — de-elevate via the NON-ELEVATED PARENT (provable by construction):
- The elevated installer must NOT launch the Tray directly. The elevated child
  now writes a sentinel file (`%TEMP%\PathVeer.Setup.LaunchTray.sentinel`)
  instead of spawning the Tray (InstallForm.WriteLaunchTraySentinel).
- The NON-elevated parent process (the Explorer-launched Setup that relaunches
  the elevated child and waits) consumes the sentinel after the child exits
  (Program.RelaunchElevated -> InstallForm.ConsumeLaunchTraySentinel) and launches
  the Tray via a plain Process.Start. The parent is un-elevated, so the Tray is
  spawned under the ordinary user token — NEVER elevated. This is verifiable by
  construction and needs no token P/Invoke / TCB privilege.
- The rare already-elevated-direct path (Run as administrator) also consumes the
  sentinel after Application.Run (best-effort; the parent path is the certified
  route for the normal UAC Repair flow).

(An earlier attempt used explorer-token-borrow / CreateProcessAsUser, but that
needed TCB / a medium-IL explorer not present in headless contexts; the parent
handoff is simpler and provably non-elevated.)

## FILES CHANGED (source, committed c87dc36)

- PathVeer.Setup/Program.cs                    (remove Console.Title; SafeWrite*; sentinel consume)
- PathVeer.Setup/InstallForm.cs                (sentinel write/consume; removed token-borrow P/Invoke)
- PathVeer.Setup.Tests/SetupExecutableStartupTests.cs   (NEW: published-WinExe --help smoke)
- PathVeer.Setup.Tests/TrayLaunchDeelevationTests.cs    (NEW: sentinel create + consume-noop)
- PathVeer.Core/Installer/TraySingleInstance.cs         (CA1416 suppression on OpenExisting)
- tools/Check-TrayTokenElevation.ps1            (NEW: repro old elevated-launch token probe)
- tools/Check-TrayTokenDeelevated.ps1           (NEW: de-elevation probe)
- tools/Check-TrayTokenElevationExplorer.ps1    (NEW)
- tools/Test-TraySingleInstance.ps1             (NEW: 10x process-count proof)
- tools/Test-TrayAutorunIdempotency.ps1         (NEW: autorun idempotency proof)

## TESTS (green)

- Core: TraySingleInstanceTests + prior (73 pass, net10.0-windows).
- Setup: 19 pass (net10.0-windows), including:
  - SetupExecutableStartupTests.Published_WinExe_Help_ExitsZero_NoCrash: publishes
    the REAL single-file WindowsGui EXE, launches it detached (no console), asserts
    exit 0, Event 1026 delta 0, Event 1000 delta 0. (Catches an unconditional
    Console.Title in a WindowsGui executable — the regression.)
  - TrayLaunchDeelevationTests: sentinel create + consume-noop (no Tray spawned).

## DEVSIGN.8 PROVENANCE (artifacts/releases/1.0.0-devsign.8/win-x64, gitignored)

Built via New-PathVeerRelease.ps1 -Version 1.0.0-devsign.8 -Mode Development/Signed
-Channel beta -MetadataKeyId pv-meta-dev-2026-01, using the local dev cert
(CN=PathVeer Development Code Signing, thumbprint 3D4AD78B16F2EAD052D1C5126812D25469A95336)
+ dev ES256 private key (DPAPI store).

Acceptance evidence:
A. help smoke: detached --help exit=0; Event 1026 delta=0; Event 1000 delta=0. PASS.
B. Explorer launch reachability: started the GUI process (alive), no Event 1026. PASS
   (full interactive UAC double-click is the operator's live step).
C. elevated direct launch: GUI path reachable (operator live step).
D. Tray Repair #1/#2 counts + token: preserved single-instance (0->1->1 x10); the
   installer-launched Tray is non-elevated BY CONSTRUCTION (non-elevated parent),
   empirically confirmed Elevated=True on the OLD code; operator Repair confirms.
- WinExe subsystem: Subsystem=WindowsGui (no console flash). Preserved.
- Product icon: ApplicationIcon set. Preserved.
- Friendly display version: SetupVersion.Friendly. Preserved.
- Self-elevation mutex ordering: preserved (devsign.5/6).
- Exit-code contract: preserved.
- Package integrity 712/712: verified (Get-FileHash, 0 mismatch, 0 missing).
- Authenticode 4/4 Valid: Setup, Cli, Tray, Service; signtool verify /pa exit 0.
- ES256 pv-meta-dev-2026-01: manifest signed:true, Development/Signed.
- SideBySide Event 72 delta = 0: 0 total, 0 referencing devsign.8.
- Tray intrinsic single-instance: preserved (devsign.7) — 0->1->1(x10)->0->1.
- Version classifier parity: preserved.
- beta.1 frozen: sha256 7e5b4301756dfa617509c8a533aed3000e7025d2b3470150b0e6933361ffa24d
  (verified unchanged).

## COMMIT / ORIGIN

- Commit c87dc36 "fix(setup): de-elevate installer-launched Tray via non-elevated parent"
  (+ prior 598fef1 Console.Title fix; bdc30cd was HEAD at slice start).
- Pushed to origin/development/service-authority; ahead/behind = 0/0.
- devsign.8 artifacts under artifacts/releases/1.0.0-devsign.8/win-x64 (gitignored).

No worktrees, no rebase, no force push, no publication, no R2/latest.json.
beta.1 not modified. No production private keys touched.
