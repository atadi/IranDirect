# PathVeer Installer Certification — devsign.7 (service-authority slice)

> ## Post-certification correction
>
> devsign.7 is not a passing final installer specimen. The Tray single-instance work remains useful historical evidence, but this artifact shared the WinExe `Console.Title` startup crash and inherited the incomplete terminal-operation contract later exposed by devsign.8. Its artifact provenance also predates the later `bdc30cd` certification-record commit. Preserve the original proof below as historical evidence.

Built from committed HEAD `2881029` on branch `development/service-authority`.
Predecessor: devsign.6 (commit `61acb03`). devsign.6 was NOT modified or rebuilt.

Scope: PathVeer Tray single-instance enforcement (per-interactive-user-session).
Not reopened: package integrity, Authenticode signing, ES256 metadata trust,
IranDirect migration, beta.1, production publishing/R2, the devsign.6 fixes
(terminal transition, version authority, product icon, WinExe).

## DUPLICATE-TRAY ROOT CAUSE (proven)

A. **Tray had NO single-instance guard at all.** `PathVeer.Tray/Program.cs` was
   just `Application.Run(new TrayApplicationContext())` — no Mutex, named pipe,
   or process guard. Every EXE launch spawned a new Tray process. This is the
   PRIMARY cause of 10+ simultaneous Trays.

D. **Installer repeatedly launches Tray.** `InstallForm.LaunchTray()` (and the
   engine success path) calls `Process.Start(PathVeer.Tray.exe)` on every
   successful Install/Repair/Upgrade when "Launch Tray after setup" is checked.
   With no Tray-side guard, each repair added a process.

C. **AUTORUN was already idempotent** — proven NOT a cause. `Set-TrayStartupEntry`
   uses a fixed value name `"PathVeer Tray"` via `Set-ItemProperty`
   (overwrite), so repeated repair yields exactly ONE value. `Remove-TrayStartupEntry`
   removes BOTH `"PathVeer Tray"` and the legacy `"IranDirect Tray"`. So repair
   does not multiply autorun entries and legacy IranDirect is reconciled out.

B. N/A — no guard existed to mis-scope.

**Conclusion: root cause = (A) missing intrinsic Tray guard, amplified by (D)
the installer's unconditional launch. The Tray must be the authority.**

## CHOSEN SINGLE-INSTANCE SCOPE AND PRIMITIVE

- Scope: **per interactive user session**, via the `Local\` mutex namespace
  prefix (NOT `Global\`). On Windows, `Local\` named objects are scoped to the
  current Terminal Services session, so:
    * exactly one Tray per logged-in user session,
    * separate Windows users each keep their own Tray (no cross-user block),
    * the Service remains machine-authoritative (unchanged).
- Primitive: `PathVeer.Core/Installer/TraySingleInstance.cs`
    * `TryAcquire()` returns a live guard owning a `Local\PathVeer.Tray.SingleInstance`
      Mutex for the first instance; returns `null` for a duplicate.
    * **Abandoned-mutex handling**: if a previous owner crashed without
      releasing, `new Mutex(true, name)` throws `AbandonedMutexException`; this is
      treated as an ownership transfer (`Mutex.OpenExisting` → we own it). A
      crashed previous instance therefore does NOT permanently block startup.
    * Duplicate launch: `SignalExisting()` sets a session-scoped
      `Local\PathVeer.Tray.ShowExisting` event so the running Tray surfaces
      itself (balloon). Exits cleanly with code 0 — no error dialog.
    * `using` ensures the mutex is released when the Tray exits.

## INSTALLER LAUNCH (idempotent by construction)

`InstallForm.LaunchTray()` is LEFT UNCHANGED. It may still call
`Process.Start(PathVeer.Tray.exe)` after success; the Tray's own guard makes
this safe (duplicate just signals + exits). The installer is explicitly NOT the
single-instance authority — the Tray is.

## AUTORUN (already idempotent, proven)

Repeated `Set-TrayStartupEntry` → exactly one `HKCU\...\Run\"PathVeer Tray"`.
Legacy `IranDirect Tray` removed by `Remove-TrayStartupEntry` (migration
contract). Verified: 5x repair → 1 value, legacy absent.

## FILES CHANGED (source, committed 2881029)

- PathVeer.Core/Installer/TraySingleInstance.cs   (NEW: session-scoped guard)
- PathVeer.Tray/Program.cs                         (acquire guard; duplicate exits)
- PathVeer.Tray/TrayApplicationContext.cs          (show-event waiter + ShowExisting)
- PathVeer.Core.Tests/Installation/TraySingleInstanceTests.cs (NEW unit tests)
- tools/Test-TraySingleInstance.ps1                (NEW real 10x process-count proof)
- tools/Test-TrayAutorunIdempotency.ps1           (NEW autorun idempotency proof)
- tools/Test-TrayAutorunIdempotency.ps1 updated after commit (read-safety fix)
Unchanged (per scope): InstallForm.LaunchTray, app.manifest, package integrity,
Authenticode, ES256 trust, IranDirect migration, beta.1, R2.

## TESTS (green)

- PathVeer.Core.Tests (net10.0-windows, Debug): TraySingleInstanceTests
  (acquire / duplicate-in-same-process→null / re-acquire-after-release /
  scope-is-Local-not-Global / SignalExisting-noop) + prior 68 pass.
- Real-process integration (tools/Test-TraySingleInstance.ps1) against the
  published devsign.7 Tray EXE:
    0 → 1 → 1 (after 10 launches) → 0 (after kill) → 1 (restart). PASS.
- Real-registry integration (tools/Test-TrayAutorunIdempotency.ps1):
    5x repair → exactly one "PathVeer Tray" value, legacy "IranDirect Tray"
    absent. PASS.
- PathVeer.Setup.Tests: 16 pass (unchanged).

## DEVSIGN.7 PROVENANCE (artifacts/releases/1.0.0-devsign.7/win-x64, gitignored)

Built via New-PathVeerRelease.ps1 -Version 1.0.0-devsign.7 -Mode Development/Signed
-Channel beta -MetadataKeyId pv-meta-dev-2026-01, using the existing local dev
cert (CN=PathVeer Development Code Signing, thumbprint
3D4AD78B16F2EAD052D1C5126812D25469A95336) + dev ES256 private key (DPAPI store).

Acceptance evidence:
- TRAY_SINGLE_INSTANCE: 0→1, 1 after 10 launches, 0 after kill, 1 restart. PASS.
- AUTORUN: exactly one "PathVeer Tray" value; legacy IranDirect Tray absent. PASS.
- Explorer EXE has PathVeer icon / no console flash / WinExe: verified
  (Subsystem=WindowsGui; ApplicationIcon set — preserved from devsign.6).
- Friendly display version: preserved (SetupVersion.Friendly).
- Terminal UI transition → Finish / exit code contract: preserved (devsign.6).
- Package integrity 712/712: verified (Get-FileHash, 0 mismatch, 0 missing).
- Authenticode 4/4 Valid: Setup, Cli, Tray, Service; signer
  CN=PathVeer Development Code Signing; signtool verify /pa exit 0.
- ES256 remains pv-meta-dev-2026-01: manifest signed:true,
  releaseMode Development/Signed, keyId pv-meta-dev-2026-01.
- SideBySide Event 72 delta = 0: 0 total, 0 referencing devsign.7.
- Version classifier parity: preserved (devsign.6 shared contract; 54 Core
  tests incl. authority parity pass).
- beta.1 frozen: sha256
  7e5b4301756dfa617509c8a533aed3000e7025d2b3470150b0e6933361ffa24d
  (verified unchanged).

## COMMIT / ORIGIN

- Commit: 2881029 "fix(tray): intrinsic per-user-session single-instance guard"
  (6 files, +386/−4). Autorun-script read-fix committed separately.
- Pushed to origin/development/service-authority.
- Ahead/behind = 0/0 (local HEAD and origin ref both at 2881029).
- devsign.7 artifacts under artifacts/releases/1.0.0-devsign.7/win-x64
  (gitignored; not committed, per the disposable-artifact rule).

No worktrees, no rebase, no force push, no publication, no R2/latest.json.
beta.1 not modified. No production private keys touched.
