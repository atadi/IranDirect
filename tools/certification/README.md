# PathVeer Release Certification Tooling

Reusable, **guarded** PowerShell helpers for executing the Phase 37.6 certification
matrix (GATE-1 .. GATE-12) on a disposable Windows VM. These are read-only inspectors
or protected scaffolding — they never mutate the developer workstation and never
commit secrets/VM disks.

## Scripts

| Script | Purpose | Mutates? |
|--------|---------|----------|
| `Get-PathVeerCertificationBaseline.ps1` | Capture VM OS/build/runtime/admin snapshot evidence | Read-only |
| `Get-PathVeerServiceEvidence.ps1` | Capture SCM state for PathVeer + legacy IranDirect (single-authority check) | Read-only |
| `Get-PathVeerRouteEvidence.ps1` | Capture bounded managed/external route state for GATE-2 before/after | Read-only |
| `Test-PathVeerInstallation.ps1` | Verify installed layout: Service, CLI PATH, Tray, Start Menu, Apps&Features, state root | Read-only |
| `Test-PathVeerReleaseArtifact.ps1` | Recompute + compare installer/package SHA-256 against frozen RC evidence | Read-only |
| `New-PathVeerCertificationVm.ps1` | Scaffold a disposable Hyper-V VM + checkpoint slot | Guarded (`-Confirm`, no ISO download) |

## Intended workflow (on a disposable VM, not the dev workstation)

1. `New-PathVeerCertificationVm.ps1 -IsoPath <win11.iso> -Confirm` — provision VM.
2. Install Windows 11, take `cert-baseline` checkpoint.
3. For each GATE, restore the clean checkpoint, then:
   - `Get-PathVeerCertificationBaseline.ps1`
   - run the install/upgrade/migration scenario
   - `Get-PathVeerServiceEvidence.ps1` / `Get-PathVeerRouteEvidence.ps1` / `Test-PathVeerInstallation.ps1`
   - `Test-PathVeerReleaseArtifact.ps1` to confirm frozen bytes
4. Record concise evidence (command, expected, actual, PASS/FAIL) in the gate matrix.

These scripts require no signing credentials, no cloud access, and no production keys.
They advance GATE-1..12 execution readiness; the gates themselves remain NOT EXECUTED
until a VM + external resources are available.

## Certification elevation: JEA control plane (replaces Scheduled-Task design)

The original Scheduled-Task (RunLevel Highest) elevation design was **rejected by real-VM
Windows** with `Scheduled task registration/start failed: Access is denied.` The filtered
PowerShell Direct parent (`PV-CERT\pvcert`, UAC-filtered token) cannot bootstrap an elevated
task, so that design is circular and unusable on the certification VM.

The replacement is a narrow **Just Enough Administration (JEA)** endpoint,
`PathVeer.Certification`, registered ONCE by the operator with genuine elevation inside the
guest (`jea/Enable-PathVeerCertificationJea.ps1`). The harness then connects to that endpoint
over PowerShell Direct; privileged certification commands run as a per-connection **virtual
account** (member of `BUILTIN\Administrators`). The connecting user keeps a filtered standard
token and never receives administrator credentials or a password.

- Narrow role capability (`jea/PathVeerCertificationRole.psrc`) exposes ONLY the cmdlets and
  external commands required by the gates (installer, CLI, service/route/process inspection
  and control). It is NOT an unrestricted Administrator PowerShell endpoint.
- `PathVeer.Certification.Jea.ps1` is the host bridge: `New-GuestJeaSession`,
  `Invoke-GuestJeaElevated`, `Invoke-GuestJeaScriptElevated`.
- `Test-PathVeerCertGuestJea.ps1` is the harmless proof probe (no install / route / service
  mutation). It must show `parentIsAdministrator = False` AND `jeaIsAdministrator = True`.

### Checkpoint semantics

- **`PV-CLEAN-WINDOWS`** — pristine Windows product baseline. No PathVeer installed, no .NET 10
  runtime added merely for PathVeer. NEVER modified or overwritten by the harness or bootstrap.
- **`PV-CERT-HARNESS`** — derived from `PV-CLEAN-WINDOWS` AFTER the operator runs the JEA
  bootstrap. Contains ONLY documented certification control-plane instrumentation
  (`C:\Program Files\PathVeerCertificationJea`, the registered endpoint, transcripts). It is
  NOT identical to pristine Windows; the instrumentation exists because PowerShell Direct
  provides a filtered `pvcert` token and Windows denies bootstrapping elevation from it.
- Teardown: `jea/Disable-PathVeerCertificationJea.ps1` removes ONLY the certification
  instrumentation (endpoint, module path, transcripts) — never unrelated Windows or PathVeer state.

### Bootstrap (operator, once, genuinely elevated in the guest)

```powershell
# Inside PV-CERT-WINDOWS (elevated PowerShell):
Copy-Item '\\host\share\tools\certification\jea\*' -Destination 'C:\pv-cert\jea' -Recurse
& 'C:\pv-cert\jea\Enable-PathVeerCertificationJea.ps1'
# Then from the HOST:
pwsh -NoProfile -File tools\certification\Test-PathVeerCertGuestJea.ps1
# After JEA CONTROL PLANE PASS, take checkpoint PV-CERT-HARNESS (do NOT touch PV-CLEAN-WINDOWS).
```
