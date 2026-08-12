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
account**. The connecting user keeps a filtered standard token and never receives administrator
credentials or a password.

### Security posture (corrected — no arbitrary-execution surface)

- **RestrictedRemoteServer** session type (NOT FullLanguage). The default JEA language mode is
  preserved; no `LanguageMode = FullLanguage`.
- **No generic shell / arbitrary-execution primitive** is exposed. `powershell.exe`, `cmd.exe`,
  `pwsh.exe`, `wscript/cscript`, `Start-Process`, `Invoke-Expression`, `Invoke-Command`,
  `New-ScheduledTask`, broad filesystem/registry write cmdlets (`Set-Content`, `Set-Item`,
  `New-Item`, `Invoke-Item`) are NOT in the role capability.
- **Trusted module surface only.** `jea/PathVeerCertificationRole.psrc` exposes ONLY the
  validated functions in `jea/PathVeerCertificationJea.psm1` (install/uninstall/repair, CLI
  wrapper, service start/stop, tray stop, route/install/manifest inspection). Each function
  validates its parameters (ValidateSet / ValidatePattern / constrained types); no `-Command`,
  ScriptBlock, executable path, registry path, filesystem path, or service name is accepted
  from the caller. Installer/CLI paths are FIXED inside the trusted module.
- **Virtual account, no added groups.** `RunAsVirtualAccount = $true`, no
  `RunAsVirtualAccountGroups`. The standard virtual-account behavior yields the administrative
  identity required by certification (verify on the VM).
- **Narrow role definition.** `RoleDefinitions = @{ 'PV-CERT\pvcert' = 'PathVeerCertificationRole' }`.
  NOT every local administrator — only the certification identity.
- **Protected audit transcripts.** `TranscriptDirectory = 'C:\ProgramData\PathVeerCertificationJea\Transcripts'`.
  The bootstrap creates it with an ACL that denies the ordinary/filtered `PV-CERT\pvcert` account
  any access; only SYSTEM and local Administrators hold FullControl. Audit logs cannot be tampered
  by the filtered connecting user.

### Host bridge + probes

- `PathVeer.Certification.Jea.ps1` is the host bridge: `New-GuestJeaSession`,
  `Invoke-GuestJeaFunction`, thin wrappers (`Invoke-GuestJeaInstall`, `Invoke-GuestJeaCli`,
  `Stop-GuestJeaService`, `Start-GuestJeaService`, `Stop-GuestJeaTray`, `Get-GuestJeaServiceState`,
  `Get-GuestJeaRouteState`, `Get-GuestJeaInstallManifest`, `Get-GuestJeaProgramDataState`). The
  gate wrapper `Run-GuestJeaInstall` forwards ONLY a validated `Action` + `Feature` set to the
  trusted module function — never a raw command or ScriptBlock.
- `Test-PathVeerCertGuestJea.ps1` is the harmless proof probe. It must show:
  - `parentIsAdministrator = False` (filtered PowerShell Direct parent)
  - `jeaIsAdministrator = True` (genuine virtual-account admin in the JEA session)
  - `restrictedBoundaryOk = True` (forbidden commands `powershell.exe`, `Start-Process`,
    `Invoke-Expression`, `Invoke-Command`, `New-ScheduledTask`, `Set-Content`, … are ABSENT)

### Checkpoint semantics

- **`PV-CLEAN-WINDOWS`** — pristine Windows product baseline. No PathVeer installed, no .NET 10
  runtime added merely for PathVeer. NEVER modified or overwritten by the harness or bootstrap.
- **`PV-CERT-HARNESS`** — derived from `PV-CLEAN-WINDOWS` AFTER the operator runs the JEA
  bootstrap AND the harmless probe proves BOTH privileged execution AND the restricted command
  boundary. Contains ONLY documented certification control-plane instrumentation
  (`C:\Program Files\PathVeerCertificationJea`, the registered endpoint, protected transcripts).
- Teardown: `jea/Disable-PathVeerCertificationJea.ps1` removes ONLY the certification
  instrumentation (endpoint, module path, transcripts) — never unrelated Windows or PathVeer state.

### Bootstrap (operator, once, genuinely elevated in the guest)

```powershell
# Inside PV-CERT-WINDOWS (elevated PowerShell):
Copy-Item '\\host\share\tools\certification\jea\*' -Destination 'C:\pv-cert\jea' -Recurse
& 'C:\pv-cert\jea\Enable-PathVeerCertificationJea.ps1'
# Then from the HOST:
pwsh -NoProfile -File tools\certification\Test-PathVeerCertGuestJea.ps1
# Only AFTER "JEA CONTROL PLANE PASS" (privileged context + restricted boundary), take
# checkpoint PV-CERT-HARNESS. Do NOT touch PV-CLEAN-WINDOWS.
```
