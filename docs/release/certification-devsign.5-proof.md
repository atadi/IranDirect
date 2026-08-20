# PathVeer Installer Certification — devsign.5 (service-authority slice)

> **Historical certification record.**
>
> This document captures the project state at the time of the devsign.5
> certification slice. It is preserved as durable historical evidence for the
> installer-certification lineage. It is not the current PathVeer
> engineering-status authority. See
> `docs/architecture-knowledge-base/AI/CURRENT.md` for current state. Freeze
> status for the devsign series is tracked in `certified-artifacts.md`.

Built from clean committed HEAD `67ca14c` on branch `development/service-authority`.

Scope: tightly scoped PathVeer.Setup correctness slice. OUT OF SCOPE (untouched):
package integrity pipeline, Authenticode signing pipeline, ES256 metadata trust,
IranDirect migration architecture, beta.1, production release metadata, R2/latest.json.

## PROVEN ISSUE #1 — self-elevation mutex (root cause + fix)

### Root cause (proven by code reading, not fabricated)
`Program.Main` previously acquired the single-instance mutex
`Global\PathVeer.Setup.SingleInstance` **before** checking `IsAdministrator()`:

```
parent (non-admin, double-click)
  -> acquires mutex            (owner = parent)
  -> calls RelaunchElevated()  (spawns runas child, waits)
  -> child starts
  -> child tries SAME mutex
  -> created == false          (parent still owns it)
  -> child returns InvalidArguments and exits
```

The elevated installer that actually mutates the system never ran. The only escape
was manually choosing "Run as administrator". This is the classic
"double-click silently does nothing" bug.

### Fix (exact sequencing)
The self-elevation gate now runs **first**, and the single-instance mutex is
acquired **only after** elevation is confirmed:

```
if (!ProcessPrivileges.IsAdministrator())
    return RelaunchElevated(args, quiet);   // parent never owns the mutex

using var singleInstance = SetupSingleInstance.TryAcquire();  // elevated owner only
if (singleInstance is null) return InvalidArguments;          // concurrent elevated rejected
... actual install ...
```

- The non-elevated parent relaunches the elevated child and exits **without ever
  holding the installer lock**, so the child is free to become the owner.
- The elevated installer owns `Global\PathVeer.Setup.SingleInstance`; a second
  concurrent elevated instance sees `created == false` and is rejected — protection
  against two real elevated installers operating concurrently is preserved.
- `app.manifest` remains `asInvoker`. It was NOT changed to hide the bug; the fix is
  in control flow, not manifest privilege level (no separately proven architectural
  reason existed).

### Proof
- `SetupSingleInstance.cs` extracted so the algorithm is unit-testable.
- Tests (`SetupBootstrapContractTests`):
  - `SingleInstance_ProductionName_IsExactConstant` — asserts the exact global name.
  - `SingleInstance_FirstAcquire_OwnsMutex` — first elevated instance owns it.
  - `SingleInstance_SecondConcurrent_Rejected` — concurrent attempt returns null.
  - `SingleInstance_Released_AllowsNext` — after disposal the next instance proceeds.
  - `SingleInstance_ParentDoesNotHold_AfterRelaunchAnalogue` — models the corrected
    non-admin launcher never owning the mutex; the child becomes authoritative.

## NEW ISSUE #2 — interactive exit-code propagation (proven + fixed)

### Proof of loss (confirmed before changing code)
`Program.Main` previously did:
```
Application.Run(form);
return form.DialogResult == DialogResult.Cancel
    ? SetupExitCodes.UserCancelled
    : SetupExitCodes.Success;
```
`ShowFailure()` exposes the SAME Close button as `ShowSuccess()`; the Close handler
only calls `Close()`. `DialogResult` stays `None` on a failure window, so the
ternary's else-branch returns `Success`. A failed interactive install therefore
closed its UI and caused `Program.Main` to report **Success** — erasing the failure.
Confirmed by reading `InstallForm.ShowFailure` / `_cancelButton` handler and
`Program.Main`'s post-`Application.Run` return.

### Resulting exit-code design
Explicit contract from `InstallForm` to `Program.Main` via `ResultExitCode`
(int, defaults to `UserCancelled`):

| Outcome                         | Exit code        |
|---------------------------------|------------------|
| success (install/repair/uninstall) | 0 `Success`    |
| user cancel before mutation     | 100 `UserCancelled` |
| package verification failure    | 104 `PackageVerificationFailed` |
| elevation denied (UAC cancel)   | 101 `ElevationDenied` |
| downgrade blocked               | 103 `DowngradeBlocked` |
| service startup/readiness fail  | 106/107 `ServiceFailed`/`ReadinessFailed` |
| generic/unexpected failure      | 1 `GenericFailure` (never 0) |

- `InstallForm.ResultExitCode` is set in `OnCompleted` (success -> 0; failure ->
  mapped category) and in `ShowFailure` (latches the code). Closing the result
  window never erases it (`_cancelButton` only calls `Close()`; the property is
  independent of `DialogResult`).
- `Program.Main` returns `form.ResultExitCode` (not `DialogResult`-derived).
- Category->code mapping is centralized in `SetupExitCodes.MapResultCategory` (single
  source of truth) used by both `InstallController.ResolveExitCode` and
  `InstallForm.OnCompleted`, so console / unattended / UI paths cannot disagree.
- `WinForms` `DialogResult` is intentionally NOT used as the success/failure signal.

### Proof
- Tests (`InstallFormResultContractTests`, real form via `SimulateCompleted` seam):
  - `Fresh_Form_DefaultsTo_UserCancelled_NotSuccess` — closing before mutation is a
    cancel, never Success.
  - `Completed_Success_Latches_Success` — success -> 0.
  - `Completed_Failure_Latches_MappedNonZeroCode` (theory over ServiceFailed,
    PackageVerificationFailed, DowngradeBlocked, ReadinessFailed, UninstallFailed,
    PurgeFailed, LegacyUnsupported, UserCancelled) — each latches its non-zero code.
  - `Completed_Failure_ThenResultWindowClose_DoesNotErase` — closing the result
    window does not revert to success.
  - `Controller_ResolveExitCode_AgreesWithForm_ForEveryCategory` — console/unattended
    and UI paths agree for every category.

## UX CLEANUP
- Integrity message changed from
  `"712 files verified (integrity only; package is not signed)."`
  to
  `"712 files verified against the package integrity manifest."`
  (describes only what `package-hashes.sha256` proves — integrity, not authenticity).
  Updated in `PathVeer.Setup/Resources/Install-PathVeer.ps1` and `tools/Install-PathVeer.ps1`.
- Final result button renamed `Close` -> `Finish` (optional UX cleanup, not auto-close).
- The successful-installer-remains-open behavior is intentional and unchanged.

## DEVSIGN.5 ARTIFACT PROVENANCE
Built via `tools/New-PathVeerRelease.ps1 -Version 1.0.0-devsign.5 -Mode Development/Signed
-Channel beta -MetadataKeyId pv-meta-dev-2026-01`.

1. Authenticode: 4/4 PEs `Valid` (Get-AuthenticodeSignature) and
   `signtool verify /pa` exit 0 — Setup, Service, Cli, Tray.
   Signer: CN=PathVeer Development Code Signing.
2. Package integrity: 712/712 files verified against `package-hashes.sha256`
   (SHA-256 over the signed release bytes, regenerated at step 3b).
3. ES256 dev metadata key id: `pv-meta-dev-2026-01` (manifest `signed:true`,
   `releaseMode: Development/Signed`).
4. SideBySide Event 72 delta for devsign.5 PE: **0** (no SideBySide errors reference
   the devsign.5 setup executable; 11 pre-existing unrelated historical entries).
5. beta.1 installer bytes unchanged: sha256
   `7e5b4301756dfa617509c8a533aed3000e7025d2b3470150b0e6933361ffa24d` (verified).

## Acceptance mapping (DEVSign.5)
1. Explorer double-click triggers UAC and opens installer — control flow fixed so the
   non-admin launcher no longer holds the mutex; proved by sequencing change + tests.
2. No manual "Run as administrator" required — same fix.
3. Second concurrent elevated installer rejected — `SetupSingleInstance.TryAcquire`
   returns null for the concurrent owner; unit-tested.
4. Package integrity 712/712 — verified above.
5. Authenticode 4/4 valid — verified above.
6. ES256 dev key remains pv-meta-dev-2026-01 — verified above.
7. SideBySide Event 72 delta = 0 — verified above.
8. Successful install shows Finish/Close and exits Success after user closes — wired
   via `ResultExitCode = Success` + `Program.Main` returns it; unit-tested.
9. Controlled interactive failure returns NONZERO corresponding exit code — wired via
   `ResultExitCode = mapped(category)`, never erased by close; unit-tested.
10. Quiet mode behavior unchanged — `RunQuiet` still returns the controller's exit code
    untouched by the new form contract.
11. beta.1 hash unchanged — verified above.

## FILES CHANGED
- PathVeer.Core/Installer/SetupExitCodes.cs        (added MapResultCategory, FromElevationDenied)
- PathVeer.Core/Installer/SetupSingleInstance.cs    (NEW: testable elevation + mutex guard)
- PathVeer.Setup/Program.cs                         (mutex after elevation; returns ResultExitCode)
- PathVeer.Setup/InstallController.cs               (uses MapResultCategory; ResolveExitCode)
- PathVeer.Setup/InstallForm.cs                     (ResultExitCode contract; Finish button; wiring)
- PathVeer.Setup/Resources/Install-PathVeer.ps1    (integrity message wording)
- tools/Install-PathVeer.ps1                        (integrity message wording)
- PathVeer.Core.Tests/Installation/InstallerControllerTests.cs (uses MapResultCategory)
- PathVeer.Core.Tests/Installation/SetupBootstrapContractTests.cs (NEW)
- PathVeer.Setup.Tests/InstallFormResultContractTests.cs (NEW)

## TESTS
- PathVeer.Core.Tests (net10.0-windows, Debug): 45 pass (SetupBootstrapContractTests
  + InstallerControllerTests).
- PathVeer.Setup.Tests (net10.0-windows, Debug): 12 pass (InstallFormResultContractTests).
- Whole-solution Release build of PathVeer.Core + PathVeer.Setup: succeeded.

## COMMIT / ORIGIN
- Committed on `development/service-authority`, pushed to origin.
- After push: origin ahead/behind = 0/0.
- devsign.5 artifact (1.0.0-devsign.5) produced at
  artifacts/releases/1.0.0-devsign.5/win-x64 (gitignored, not committed).
