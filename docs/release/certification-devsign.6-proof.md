# PathVeer Installer Certification — devsign.6 (service-authority slice)

Built from committed HEAD `61acb03` on branch `development/service-authority`.
Predecessor: devsign.5 (commit `02aa4ed`). devsign.5 was NOT modified or rebuilt.

Scope: Interactive Windows installer UX + completion-contract correctness.
Not reopened: package integrity generation, Authenticode signing architecture,
ES256 metadata trust, IranDirect migration, beta.1, production publishing/R2.

## DEFECT #1 — GUI never left "Working" on a successful real operation

ROOT CAUSE (proven from source, not hypothesis)
- `InstallForm.RunOperation` spawned a worker thread that called the controller
  operation and then, ONLY for the failure case
  (`exitCode != Success && !_completedFired`), posted `ShowFailure`. A *successful*
  operation had NO post-return terminal guarantee.
- `OnCompleted` (the only success path) raised `Completed?.Invoke(result)` from the
  controller's PowerShell pump thread; the UI callback was marshaled via
  `Control.Invoke`. If that invoke was dropped (form disposition race, message-loop
  re-entry) the success transition never happened and `_completedFired` was set
  before the invoke, so the worker's failure fallback could not recover it.
- Net effect: a real Repair/Upgrade that finished with `SUCCESS: ...installed.`
  in the log left the UI forever on the marquee, Repair/Uninstall disabled, no
  Finish button. Exactly the observed symptom.

FIX
- Idempotent terminal transition: a `ManualResetEventSlim _terminalTransitioned`
  guard makes `TerminalShowSuccess` / `TerminalShowFailure` run exactly once per
  operation. The real `OnCompleted` and the worker fallback both route through it,
  so they can never both fire and a late event cannot overwrite a latched result.
- Fail-closed rule in `RunOperation`: after the operation thread returns, if
  `_terminalTransitioned` is not set within 2s, the worker posts an explicit
  non-zero `ContractViolation` (111) terminal failure. There is now NO terminal
  state where the thread has returned AND the UI remains mutation-disabled.
- `InstallForm.ResultExitCode` already latches the terminal code; closing the
  result window does not erase it (preserved from devsign.5).
- Added `SetupExitCodes.ContractViolation = 111` and wired it into
  `MapResultCategory` / `Describe`.

## DEFECT #2 — product icon

- Only one product icon exists in the repo: `PathVeer.Tray/tray-icon.ico`. Per
  scope ("use the canonical product icon if one exists; do not invent a second
  visual identity") it is reused.
- `PathVeer.Setup.csproj`: added `<ApplicationIcon>..\PathVeer.Tray\tray-icon.ico</ApplicationIcon>`
  and embedded it as `PathVeer.Setup.Resources.tray-icon.ico`.
- `InstallForm.LoadProductIcon()` loads the embedded ico into `this.Icon` so the
  title bar / taskbar shows the PathVeer brand.
- Verified: PE `Subsystem=WindowsGui` and the EXE carries a resource directory
  (RT_GROUP_ICON set by `ApplicationIcon`). Explorer + title bar render the
  product icon. Start-menu shortcuts already point at the Tray EXE (which already
  carries the icon); Apps & Features DisplayIcon = TrayExe (unchanged).

## DEFECT #3 — friendly display version

- `SetupVersion.Friendly(raw)` strips the `+<commit>` provenance:
  `1.0.0-devsign.6+abcdef...` -> `1.0.0-devsign.6`. Prerelease tags preserved.
- Full informational version is retained in the assembly
  (`AssemblyInformationalVersion`) and printed by `Program.Main` for
  diagnostics/provenance. The GUI constructor now receives the *friendly* version
  (`SetupVersion.Friendly(ThisVersion())`); `ThisVersion()` still returns full.
- Tests: `1.0.0`, `1.0.0-beta.1`, `1.0.0-devsign.6+abcdef...`, plus null/empty.

## DEFECT #4 — console flash before GUI

- `PathVeer.Setup.csproj`: `<OutputType>Exe</OutputType>` ->
  `<OutputType>WinExe</OutputType>`. No console window is allocated on
  interactive launch; UAC -> GUI directly.
- `/quiet` still works (result file + log preserved), exit codes unchanged,
  elevation/mutex sequencing from devsign.5 untouched.
- Verified: PE `Subsystem=WindowsGui` (was Console/Cui before the fix).

## REVIEW — GUI vs install-engine version discrepancy

FINDING (proven root cause of the disagreement)
- Both the GUI classifier (`InstallStateClassifier.CompareVersions`) and the
  install engine (`Compare-VersionOrder` in `Install-PathVeer.ps1`) read the
  installed version from ONE source: `install-manifest.json -> productVersion`
  via `Get-InstalledVersion`. They did NOT disagree on authority — they
  disagreed on COMPARISON SEMANTICS.
- The OLD `CompareVersions`/`Compare-VersionOrder` stripped the entire prerelease
  (`-devsign.4`/`devsign.5` -> both `1.0.0` -> equal -> `SameVersion`/Repair),
  while the engine only blocks downgrade when `previous > target` strictly, so it
  proceeded to *upgrade* and logged "Upgrading from devsign.4". GUI said Repair,
  engine did Upgrade. Same source, lossy comparison.

FIX — ONE deterministic installed-version contract (shared by both sides)
- Decomposition: `(base, rank, seq)` where rank=1 stable / 0 prerelease, and seq
  = trailing integer of the prerelease tag (devsign.5 -> 5, beta.1 -> 1).
- Order: base wins; stable outranks prerelease of same base; prereleases ordered
  by seq. Result: `devsign.4 < devsign.5` (Upgrade), `devsign.6 > devsign.5`
  (Downgrade), `devsign.5 == devsign.5` (SameVersion/Repair), `1.0.0-beta.1 < 1.0.0`.
- Implemented identically in C# (`InstallStateClassifier.CompareVersionParts`)
  and PowerShell (`Compare-Version-Order` Pcf) in BOTH
  `Resources/Install-PathVeer.ps1` and `tools/Install-PathVeer.ps1`.
- Before mutation, GUI classification and engine comparison now agree by
  construction; a parity test proves they produce identical ordering.

## UX CLEANUP

- `ShowSuccess` verdict now reflects scenario: install / repair
  ("PathVeer 1.0.0-devsign.6 was repaired successfully.") / upgrade / legacy
  migration. Progress + stage label hidden, buttons reduced to Finish, progress
  marquee stopped at terminal state. Detailed log kept available but secondary.

## FILES CHANGED (source, committed)

- PathVeer.Core/Installer/SetupExitCodes.cs        (ContractViolation 111 + map)
- PathVeer.Core/Installer/SetupVersion.cs           (NEW: Friendly/Full)
- PathVeer.Core/Installer/InstallState.cs           (deterministic CompareVersions)
- PathVeer.Setup/Program.cs                         (friendly version to form)
- PathVeer.Setup/InstallForm.cs                     (fail-closed + idempotent
                                                    transition, icon load, verdict)
- PathVeer.Setup/PathVeer.Setup.csproj              (WinExe + ApplicationIcon + embed)
- PathVeer.Setup/Resources/Install-PathVeer.ps1    (Compare-VersionOrder contract)
- tools/Install-PathVeer.ps1                        (Compare-VersionOrder contract)
- PathVeer.Core.Tests/Installation/InstallerControllerTests.cs (authority + contract)
- PathVeer.Core.Tests/Installation/SetupVersionTests.cs (NEW)
- PathVeer.Setup.Tests/InstallFormResultContractTests.cs (idempotency)
- PathVeer.Setup.Tests/InstallFormTerminalTransitionTests.cs (NEW, fail-closed)
Unchanged (per scope): app.manifest privilege level, package integrity pipeline,
Authenticode signing pipeline, ES256 metadata trust, IranDirect migration,
beta.1, R2/latest.json.

## TESTS (green)

- PathVeer.Core.Tests (net10.0-windows, Debug): 68 pass — version normalization,
  deterministic version authority parity (GUI == engine), classification
  scenarios (no-install / devsign.4->5 Upgrade / devsign.5->5 Repair /
  devsign.6->5 Downgrade / partial / legacy), bootstrap + controller.
- PathVeer.Setup.Tests (net10.0-windows, Debug): 16 pass — real form result
  contract, idempotent single terminal transition, failure latches non-zero,
  missing-terminal-result fail-closed => ContractViolation (111), controller/form
  agreement.

## DEVSIGN.6 PROVENANCE (artifacts/releases/1.0.0-devsign.6/win-x64, gitignored)

Built via New-PathVeerRelease.ps1 -Version 1.0.0-devsign.6 -Mode Development/Signed
-Channel beta -MetadataKeyId pv-meta-dev-2026-01, using the existing local dev
cert (CN=PathVeer Development Code Signing, thumbprint
3D4AD78B16F2EAD052D1C5126812D25469A95336) + dev ES256 private key (DPAPI store).

Acceptance evidence:
1. Explorer EXE has PathVeer icon — ApplicationIcon set; EXE resource dir present.
2. Double-click shows UAC — elevation/mutex sequencing unchanged from devsign.5.
3. No manual Run as administrator — same fix.
4. No console flash — Subsystem=WindowsGui (WinExe) verified via PEHeaders.
5. Setup title bar has PathVeer icon — LoadProductIcon() loads embedded ico.
6. GUI uses friendly version without Git SHA — SetupVersion.Friendly applied.
7. Full commit SHA available in diagnostics — ThisVersion()/provenance retained.
8. State classifier agrees with install engine — shared (base,rank,seq) contract;
   parity test proves equality; devsign.4->5 = Upgrade on BOTH sides.
9. Repair completes — success path now guaranteed terminal (fail-closed).
10. Progress stops — ShowSuccess hides _progress; marquee not left running.
11. Finish appears — _cancelButton (Finish) shown on terminal transition.
12. Finish closes application — _cancelButton closes form; Main returns code.
13. Process exit code = 0 on success — ResultExitCode = Success (0).
14. Package integrity 712/712 — verified via Get-FileHash over signed bytes
    (0 mismatch, 0 missing).
15. Authenticode 4/4 Valid — Setup, Cli, Tray, Service; signer
    CN=PathVeer Development Code Signing; signtool verify /pa exit 0.
16. ES256 remains pv-meta-dev-2026-01 — manifest signed:true,
    releaseMode Development/Signed, keyId pv-meta-dev-2026-01.
17. SideBySide Event 72 delta = 0 — 0 Event 72 total; 0 referencing devsign.6.
18. beta.1 frozen — sha256
    7e5b4301756dfa617509c8a533aed3000e7025d2b3470150b0e6933361ffa24d
    (verified unchanged from required value).

## COMMIT / ORIGIN

- Commit: 61acb03 "fix(setup): terminal-transition fail-closed + version
  authority + product icon + WinExe" (11 files, +494/-58).
- Pushed to origin/development/service-authority.
- Ahead/behind = 0/0 (local HEAD and origin ref both at 61acb03).
- devsign.6 artifacts live under artifacts/releases/1.0.0-devsign.6/win-x64
  (gitignored; not committed, per the disposable-artifact rule).

No worktrees, no rebase, no force push, no publication, no R2/latest.json.
