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

### Bootstrap (operator, once, genuinely elevated in the VM)

The certification control plane is staged from the **Desktop** (PowerShell Direct) into the VM,
then promoted by the operator-elevated bootstrap inside the VM. No network share (no
`\\host\share`) is used.

**Step 1 — RUN ON DESKTOP: stage all required content into the VM via PowerShell Direct.**

```powershell
# Resolve the operator credential (local prompt; password is never printed/logged).
$cred = Get-Credential -UserName 'PV-CERT\pvcert' -Message 'PathVeer-Certification (PV-CERT) local admin password'

# Open a PowerShell Direct session to the certification VM.
$vmSession = New-PSSession -VMName 'PathVeer-Certification' -Credential $cred

# Exact Desktop source paths (at this commit):
#   JEA control-plane:    tools\certification\jea
#   Trusted installer:    tools\Install-PathVeer.ps1
#   Protected package:    artifacts/releases/1.0.0-beta.1/win-x64/PathVeer-1.0.0-beta.1
# The bootstrap's -SourceDir defaults to the jea folder, so stage the installer + package as
# siblings of the jea sources so $SourceDir\Install-PathVeer.ps1 and
# $SourceDir\PathVeer-1.0.0-beta.1 resolve.
$jeaSrc = Resolve-Path 'tools\certification\jea'
$installerSrc = Resolve-Path 'tools\Install-PathVeer.ps1'
$pkgSrc = Resolve-Path 'artifacts/releases/1.0.0-beta.1/win-x64/PathVeer-1.0.0-beta.1'

# Stage into C:\pv-cert\jea on the VM (the bootstrap's official -SourceDir).
Invoke-Command -Session $vmSession -ScriptBlock { New-Item -ItemType Directory -Force -Path 'C:\pv-cert\jea' | Out-Null }
Copy-Item -Path "$jeaSrc\*"                       -Destination 'C:\pv-cert\jea'     -ToSession $vmSession -Recurse -Force
Copy-Item -Path $installerSrc                     -Destination 'C:\pv-cert\jea\Install-PathVeer.ps1' -ToSession $vmSession -Force
Copy-Item -Path "$pkgSrc\*"                       -Destination 'C:\pv-cert\jea\PathVeer-1.0.0-beta.1' -ToSession $vmSession -Recurse -Force

Remove-PSSession $vmSession
```

**Step 2 — RUN ON VM — ELEVATED POWERSHELL: operator bootstrap (the only trust transition).**

```powershell
# Inside the VM, Run As Administrator (not from the filtered PowerShell Direct session).
& 'C:\pv-cert\jea\Enable-PathVeerCertificationJea.ps1'
```

The bootstrap validates BOTH the installer and package payload sources exist BEFORE any mutation;
if either is missing it THROWS and preserves any previously valid protected candidate (it never
silently destroys a good baseline). It promotes the installer + payload into the protected tree
`C:\ProgramData\PathVeerCertificationJea\{Trusted,Payloads}` and registers the
`PathVeer.Certification` endpoint. Do NOT copy anything directly into `C:\ProgramData\...` or
`C:\Program Files\WindowsPowerShell\Modules\...` — the bootstrap performs that trust transition.

**Step 3 — RUN ON DESKTOP: prove the control plane + LIVE baseline (NO restore).**

```powershell
# JEA probe: privileged context + restricted boundary must PASS.
pwsh -NoProfile -File tools\certification\Test-PathVeerCertGuestJea.ps1

# LIVE-STATE baseline preflight: inspects the VM's CURRENT state (does NOT restore the
# canonical checkpoint first). Proves the protected installer + payload are present without
# installing product. If baselineReady=false, DO NOT checkpoint — re-stage and re-run Step 1–2.
pwsh -NoProfile -File tools\certification\Invoke-PathVeerCertification.ps1 -Stage BASELINE
```

**Step 4 — RUN ON DESKTOP: transactional checkpoint replacement (Hyper-V).**

```powershell
# Current canonical PV-CERT-HARNESS stays intact until a verified replacement exists.
$vm = 'PathVeer-Certification'
$snap = Get-VMSnapshot -VMName $vm -Name 'PV-CERT-HARNESS'
# 1. create NEW temporary checkpoint
$tmpName = 'PV-CERT-HARNESS-NEW'
Checkpoint-VM -VMName $vm -SnapshotName $tmpName
# 2. verify NEW exists
if (-not (Get-VMSnapshot -VMName $vm -Name $tmpName)) { throw 'New checkpoint failed to materialize.' }
# 3. rename old canonical -> OLD
Rename-VMSnapshot -VMName $vm -Name 'PV-CERT-HARNESS' -NewName 'PV-CERT-HARNESS-OLD'
# 4. rename NEW -> PV-CERT-HARNESS
Rename-VMSnapshot -VMName $vm -Name $tmpName -NewName 'PV-CERT-HARNESS'
# 5. verify new canonical exists
if (-not (Get-VMSnapshot -VMName $vm -Name 'PV-CERT-HARNESS')) { throw 'Replacement checkpoint missing.' }
# 6. delete OLD
Remove-VMSnapshot -VMName $vm -Name 'PV-CERT-HARNESS-OLD' -Confirm:$false
# 7. verify exactly one of each
Get-VMSnapshot -VMName $vm | Select-Object Name | Sort-Object Name
#    -> exactly: PV-CERT-HARNESS, PV-CLEAN-WINDOWS
```

**Step 5 — RUN ON DESKTOP: post-checkpoint verification (optional but recommended).**

```powershell
# Restore the NEW checkpoint and re-validate it contains the validated state.
Restore-VMSnapshot -VMName 'PathVeer-Certification' -Name 'PV-CERT-HARNESS' -Confirm:$false
pwsh -NoProfile -File tools\certification\Test-PathVeerCertGuestJea.ps1
pwsh -NoProfile -File tools\certification\Invoke-PathVeerCertification.ps1 -Stage BASELINE
```

**Step 6 — RUN ON DESKTOP: run GATE-2 against the refreshed checkpoint.**

```powershell
pwsh -NoProfile -File tools\certification\Invoke-PathVeerCertification.ps1 -Stage GATE2
# (-Stage All runs the full gate sequence against the canonical PV-CERT-HARNESS; BASELINE is NOT
#  part of All — it is the pre-checkpoint live-state preflight only.)
```

> Never use Host/Guest wording in operator steps: use **Desktop** / **VM**. Never use a network
> share. PV-CLEAN-WINDOWS must remain untouched.
