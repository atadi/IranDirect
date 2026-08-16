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
- **Trust boundary (protected vs untrusted zones).** The privileged JEA installer executes the
  installer script and consumes the package payload ONLY from the PROTECTED tree
  `C:\ProgramData\PathVeerCertificationJea\Trusted` and `...\Payloads`. These are created by the
  operator-elevated bootstrap with ACLs that DENY the ordinary/filtered `PV-CERT\pvcert` account
  write access (SYSTEM + local Administrators hold FullControl; pvcert gets an explicit DENY).
  The untrusted incoming staging area `C:\pv-cert\incoming` is pvcert-writable but is NEVER
  executed by privileged code. The bootstrap performs the one-way, operator-authorized promotion
  (copy + ACL) from `C:\pv-cert\incoming` (or the operator-provided source) into the protected
  tree. NO JEA function promotes arbitrary content, so a filtered pvcert caller cannot convert
  incoming content into executed privileged content.
- **Virtual account, no added groups.** `RunAsVirtualAccount = $true`, no
  `RunAsVirtualAccountGroups`. The standard virtual-account behavior yields the administrative
  identity required by certification (verify on the VM).
- **Narrow role definition.** `RoleDefinitions = @{ 'PV-CERT\pvcert' = 'PathVeerCertificationRole' }`.
  NOT every local administrator — only the certification identity.
- **Protected audit transcripts + trusted payloads.** `TranscriptDirectory =
  'C:\ProgramData\PathVeerCertificationJea\Transcripts'`. The bootstrap creates the whole
  `C:\ProgramData\PathVeerCertificationJea\{Trusted,Payloads,Transcripts}` tree with ACLs that
  deny the ordinary/filtered `PV-CERT\pvcert` account any write; only SYSTEM and local
  Administrators hold FullControl. Audit logs and trusted installer/payload cannot be tampered
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
  - `trustedFilesNotWritableByParent = True` (the filtered parent CANNOT write into the protected
    `C:\ProgramData\PathVeerCertificationJea\{Trusted,Payloads,Transcripts}` tree — proven with
    harmless sentinel-write attempts that must be Access Denied)

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
# Stage the JEA control-plane sources AND the protected candidate payload. The bootstrap promotes
# BOTH the trusted installer (Install-PathVeer.ps1) and the package payload
# (PathVeer-1.0.0-beta.1) into the protected tree C:\ProgramData\PathVeerCertificationJea\{Trusted,Payloads}.
# Staging only jea/* (no payload) leaves Payloads\PathVeer-1.0.0-beta.1 ABSENT and the bootstrap
# silently warns — the resulting PV-CERT-HARNESS would then fail every install-dependent GATE with
# "install exitCode was not 0 (exitCode=)". The payload source MUST ride along.
Copy-Item '\\host\share\tools\certification\jea\*'        -Destination 'C:\pv-cert\jea'     -Recurse
Copy-Item '\\host\share\artifacts\packages\PathVeer-1.0.0-beta.1' -Destination 'C:\pv-cert\jea\PathVeer-1.0.0-beta.1' -Recurse
# Install-PathVeer.ps1 is NOT in jea/* — also stage it so the bootstrap can promote the trusted installer.
Copy-Item '\\host\share\tools\Install-PathVeer.ps1'       -Destination 'C:\pv-cert\jea\Install-PathVeer.ps1'
& 'C:\pv-cert\jea\Enable-PathVeerCertificationJea.ps1'
# Then from the HOST:
pwsh -NoProfile -File tools\certification\Test-PathVeerCertGuestJea.ps1
# Read-only harness-baseline preflight: proves the protected installer + payload are present in the
# guest protected tree WITHOUT installing product. Run BEFORE taking the checkpoint. If it reports
# baselineReady=false, DO NOT checkpoint — re-stage the payload and re-run the bootstrap first.
pwsh -NoProfile -File tools\certification\Invoke-PathVeerCertification.ps1 -Stage BASELINE
# Only AFTER "JEA CONTROL PLANE PASS" (privileged context + restricted boundary) AND baselineReady=true,
# take checkpoint PV-CERT-HARNESS. Do NOT touch PV-CLEAN-WINDOWS.
```

> The bootstrap performs the one-way, operator-authorized promotion (copy + ACL) from the staged
> source (or `C:\pv-cert\incoming`) into the protected tree and FAILS LOUDLY on validation. It only
> warns (does not fail) when the payload/installer source is absent — so the baseline preflight above
> is the authoritative gate that prevents an incomplete checkpoint.
