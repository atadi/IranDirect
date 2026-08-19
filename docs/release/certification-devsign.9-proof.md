# PathVeer Installer Certification — devsign.9 (service-authority slice)

Built from committed HEAD `ca3031f` on branch `development/service-authority`.
Predecessors: devsign.8 (commit `c87dc36`, FROZEN regression specimen), devsign.7
(`2881029`), devsign.6 (`61acb03`). devsign.6/7/8 were NOT modified or rebuilt.

Scope: fix the proven devsign.8 GUI hang — after the PowerShell deployment child
exited, the progress tailer kept polling for up to 600 seconds because nothing
tied its lifetime to the child process, so the UI stayed on "Working..." for up
to 10 minutes. Plus a second, independently-discovered fail-closed bug: the
controller deserialized progress/result JSON case-sensitively while the
deployment script emits lowercase keys, so every record parsed to
empty/false and a successful backend reported as a failed install.

Not reopened (preserved): package integrity, Authenticode, ES256 metadata trust,
IranDirect migration, beta.1, production publishing/R2, Tray single-instance,
non-elevated-parent Tray launch, WinExe/no-console-flash, product icon, friendly
display version, self-elevation mutex ordering, interactive exit-code contract,
version classifier parity.

## PROGRESS-READER LIFETIME — ROOT CAUSE (proven)

devsign.8 `InstallController.Execute` spawned the tailer as:

    var reader = Task.Run(() => StreamProgress(progressFile, ct));
    proc.WaitForExit();
    // finally { reader.Wait(ct); }

`StreamProgress` looped `while (sw.Elapsed.TotalSeconds < 600)` with NO signal
that the child had exited. The deployment script writes `result.json` and the
final `Finished` progress line and THEN exits; once `proc.WaitForExit()` returns
the PowerShell process is already gone — yet the tailer kept polling for the full
600s before `ReadResult`/`Completed` ever ran. The GUI's `operation()` never
returned, so the marquee/"Working..." state never cleared and devsign.6's
terminal fallback never ran.

REPRODUCED: reverted `InstallController.cs` to the buggy HEAD `c87dc36`, ran the
new lifetime regression test `SuccessOperation_...`; the test host was killed by
an outer 35s timeout (the controller would otherwise block the full 600s).
Restored the fixed controller → same test returns in <1s.

## SHUTDOWN / DRAIN DESIGN (the fix)

`InstallController.cs` now owns a `ProgressTailer` (replaces inline
`StreamProgress`) whose `RunLoop` is terminated by a
`CancellationTokenSource` (`progressCts`) linked to the caller's `ct`:

    var tailer = new ProgressTailer(progressFile, rec => Progress?.Invoke(rec));
    var progressCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var reader = Task.Run(() => tailer.RunLoop(progressCts.Token));
    try { proc.WaitForExit(); }
    finally
    {
        progressCts.Cancel();                 // child exited -> stop tailer
        try { reader.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { tailer.Drain(); }               // one final read to EOF
    }
    ResultRecord? result = ReadResult(resultFile);
    if (result is not null) { Completed?.Invoke(result); return ...; }
    return MapProcessExit(proc.ExitCode);

Invariant satisfied: CHILD PROCESS EXIT -> tailer terminates promptly ->
final result is read -> Completed fires exactly once -> RunInstall/RunUninstall
returns. No 600-second post-exit wait. The 600s cap remains only as a defensive
maximum while the child is genuinely still running.

PROOF THE FINAL `Finished` RECORD CANNOT BE LOST: the tailer uses a single shared
`_position` across `RunLoop` and `Drain`. The script writes `Finished` +
`result.json` immediately before exiting, so both are fully flushed to disk by
the time `WaitForExit` returns. `Drain()` performs one final `PumpOnce()` that
seeks to `_position` and reads to EOF, raising every record the loop had not yet
seen — including `Finished`. Cancellation is issued only AFTER the child has
exited (so it cannot race the final write), and the bounded `reader.Wait(2s)`
guarantees the loop has stopped before `Drain` runs. Each record is raised
exactly once (position advances past it).

## CASE-INSENSITIVE RESULT PARSING (second bug fixed)

System.Text.Json is case-sensitive by default. `Install-PathVeer.ps1` emits
lowercase JSON keys (`stage`, `success`, `category`); PowerShell's
`ConvertTo-Json` (both pwsh and Windows PowerShell 5.1) does the same. The C#
`ProgressRecord`/`ResultRecord` types use PascalCase. Without an explicit
option, `JsonSerializer.Deserialize` left `Stage=""` and `Success=false` on
every record — so a successful install surfaced as a failed one (exit code
mapped from an empty category → GenericFailure). Added a shared
`JsonOptions { PropertyNameCaseInsensitive = true }` used by both `PumpOnce`
(progress) and `ReadResult` (result). This is the correct contract for the real
script and for the tests.

## FILES CHANGED (source, committed ca3031f)

- PathVeer.Setup/InstallController.cs
    - Replace inline `StreamProgress` with `ProgressTailer` (RunLoop +
      bounded Drain), driven by `progressCts` cancelled on child exit.
    - Add `JsonOptions` (PropertyNameCaseInsensitive) and use it for progress +
      result deserialization.
- PathVeer.Setup.Tests/InstallControllerLifetimeTests.cs  (NEW)
- docs/release/certification-devsign.8-proof.md           (carried over, frozen specimen)

Unrelated working-tree doc edits (AI-START-HERE.md, CURRENT.md,
SESSION-PROTOCOL.md) were present before this slice and were deliberately LEFT
UNTOUCHED / uncommitted.

## TESTS (green)

Setup suite (PathVeer.Setup.Tests): 25/25 pass (net10.0-windows).
New regression tests (InstallControllerLifetimeTests) exercise the REAL
controller operation-lifetime contract via short-lived fake PowerShell
producers (real progress tailer + real result handling):

  SuccessOperation_ReturnsPromptly_CompletedOnce_ZeroExit_FinishedObserved
    fake writes VerifyingPackage/Installing/Finished + success result.json,
    then exits -> Execute returns in ~0.35s (was 600s), Completed fires once,
    exit 0, "Finished" observed. STRICT BOUND: elapsed < 30s. (358 ms actual.)
  FailureOperation_ReturnsPromptly_NonzeroExit
    fake writes Finished + failure result.json -> nonzero exit promptly,
    Finished observed.
  NoResultFile_ProcessExitFallback_ReturnsPromptly
    fake exits with no result.json -> fallback to process exit code mapping,
    returns promptly, Completed not raised.
  MissingProgressFile_NoHang
    fake writes only result.json (no progress file) -> no hang, exit 0.
  MalformedProgressLine_Ignored_ValidFinishedStillObserved
    fake mixes a non-JSON line with a valid Finished line -> malformed ignored,
    Finished still processed, exit 0.
  Cancellation_NoReaderHang
    fake sleeps 3s then exits; cancelling ct must not hang — operation returns
    shortly after child exit (<15s), Not 600s.

ELAPSED-TIME REGRESSION PROOF:
  - Buggy HEAD c87dc36: lifetime test killed at outer 35s timeout (would run
    600s) — HANG reproduced.
  - Fixed ca3031f: same test 358 ms; full suite 6/6 under 6s total.

Core suite (PathVeer.Core.Tests): 2713/2713 pass. One transient failure in the
first 10-minute run (`CrossInstance_ReadersAndSingleWriter_NoMalformedReads`,
`UnauthorizedAccessException` on a concurrent temp path) was a re-run-proven
file-lock flake in an endurance/concurrency test, unrelated to this slice
(no Setup/JSON code touched). Re-run: 1/1 pass.

## DEVSIGN.9 PROVENANCE

Built via:
  New-PathVeerRelease.ps1 -Version 1.0.0-devsign.9 -Mode Development/Signed
  -Channel beta -MetadataKeyId pv-meta-dev-2026-01

from committed HEAD ca3031f (fix already committed; release compiles from source).
Local dev cert CN=PathVeer Development Code Signing
(thumbprint 3D4AD78B16F2EAD052D1C5126812D25469A95336) + dev ES256 key
(DPAPI store). Artifacts under
artifacts/releases/1.0.0-devsign.9/win-x64 (gitignored).

  - Package integrity: 712/712 files hashed (checksums.txt, 0 mismatch).
  - Authenticode 4/4 Valid (signtool verify /pa exit 0):
      PathVeerSetup-1.0.0-devsign.9-win-x64.exe
      PathVeer-1.0.0-devsign.9/Service/PathVeer.Service.exe
      PathVeer-1.0.0-devsign.9/Cli/PathVeer.Cli.exe
      PathVeer-1.0.0-devsign.9/Tray/PathVeer.Tray.exe
  - ES256 pv-meta-dev-2026-01: release-manifest.json signed:true,
    releaseMode "Development/Signed".
  - SideBySide Event 72 delta = 0 (no WinSxS registration for this disposable
    dev release; query for '*devsign.9*' returned no Event 72).

## DEVSIGN.9 LIVE EXPLORER ACCEPTANCE

Automated build/unit/integrity/Authenticode/ES256/SideBySide verification is
complete and green. The interactive GUI acceptance — Explorer double-click ->
UAC -> Upgrade/Repair with "Launch Tray" -> Verifying/Installing/Starting
service/Creating shortcuts/Finished -> progress stops within a few seconds ->
Working disappears -> success verdict -> Finish appears -> Setup exits 0, plus
Repair #1/#2 Tray counts (1, then 1) and Tray Elevated=False, and 10x Tray
launch staying at 1 — is an OPERATOR step requiring interactive UAC and the
desktop GUI, which cannot be driven from this CLI environment.

The fixed behavior is proven by:
  (a) the 600s-hang regression test now returning in 358 ms against the real
      controller operation-lifetime contract, and
  (b) the built devsign.9 Setup compiling from the committed fix (Setup working
      tree matches HEAD ca3031f exactly; no local modifications).
Operator should run the GUI acceptance from the artifact above; expected Finish
elapsed is sub-second-to-few-seconds (not minutes), matching the fixed contract.

## PRESERVED PRIOR PASSES

WinExe/WindowsGui (no console flash); product icon; friendly display version;
Explorer self-elevation mutex sequencing; interactive exit-code contract;
version classifier parity; package integrity 712/712; Authenticode 4/4 Valid;
ES256 pv-meta-dev-2026-01; SideBySide Event 72 delta 0; Tray intrinsic
per-session single-instance; non-elevated-parent Tray-launch design.

beta.1 remains frozen:
  sha256 7e5b4301756dfa617509c8a533aed3000e7025d2b3470150b0e6933361ffa24d
(verified byte-identical to the frozen artifact; not modified).

## COMMIT / ORIGIN

- Commit ca3031f "fix(setup): tie progress-tailer lifetime to child process
  exit + case-insensitive result parsing" on development/service-authority.
- Pushed to origin/development/service-authority; ahead/behind = 0/0.
- devsign.9 artifacts under artifacts/releases/1.0.0-devsign.9/win-x64
  (gitignored).

No worktrees, no rebase, no force push, no publication, no R2/latest.json.
beta.1 not modified. No production private keys touched.
