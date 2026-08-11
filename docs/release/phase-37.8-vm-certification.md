# Phase 37.8 — Disposable VM Certification (reconciliation & host audit)

Date: 2026-08-11 (superseded 2026-08-12 by `phase-37.9-vm-execution.md`)
Branch: `development/service-authority`
Starting HEAD: `375a9f7`

> **Correction (2026-08-12).** The original conclusion below — "VM provisioning
> BLOCKED: CPU virtualization disabled in firmware" — is **stale and withdrawn**.
> The blocking signal (`Win32_Processor.VirtualizationFirmwareEnabled = False`)
> is not authoritative when Hyper-V is already active. The user experimentally
> proved Hyper-V execution works: `Win32_ComputerSystem.HypervisorPresent = True`,
> `bcdedit hypervisorlaunchtype = Auto`, `vmms` service Running, and a real
> Generation-2 probe VM (`PathVeer-HyperV-Probe`) was created and booted
> successfully. A legitimate Windows 11 ISO was supplied. The disposable
> certification VM **was provisioned and the gates were executed** — see
> `phase-37.9-vm-execution.md`. The historical audit below is retained as
> evidence of the earlier (incorrect) blocker assessment.

## 0. Host capability audit (read-only, original 2026-08-11 — RETRACTED)

| Item | Finding (original, since corrected) |
|------|---------|
| Windows edition/build | Windows 10 Pro, 10.0.26100.1 |
| Hyper-V feature | **Enabled** |
| Hyper-V PowerShell module | Available (`Get-VM`, `New-VM`, …) |
| Existing VMs | None |
| Virtual switches | `Default Switch` (Internal), `WSL (Hyper-V firewall)` (Internal) |
| C: free / total | 272 GiB free of 930 GiB |
| CPU virtualization firmware | Reported `Disabled` by WMI (`VirtualizationFirmwareEnabled = False`) — **not authoritative under active Hyper-V/VBS**; contradicted by `HypervisorPresent = True` |
| Windows ISO/VHDX assets | None found on host at the time (ISO supplied later) |

> **Retracted conclusion:** "A Hyper-V VM cannot be started on this workstation."
> Incorrect — Hyper-V was operational; the WMI signal was misleading.

## 0b. Corrected host/VM facts (2026-08-12)

- `Win32_ComputerSystem.HypervisorPresent = True`; `vmms` Running; probe VM booted.
- Legitimate ISO supplied: `en-us_windows_11_business_editions_version_26h1...iso`.
- Certification VM `PathVeer-Certification` (Gen2, 4 vCPU, 8 GiB, 80 GiB dynamic
  VHDX on D:, Default Switch, Secure Boot on) provisioned and executed the gates.
- vTPM could not be enabled via the host's Hyper-V module (no usable local
  guardian); the guest install used the documented Windows LabConfig TPM/SecureBoot
  bypass (guest-only, host not weakened). See phase-37.9 for details.

## 1. Gate matrix reconciliation (authoritative: phase-37.5 §5)

Mapping of each gate to current evidence. `can execute now` = executable on this host.

| Gate | Definition | Prior status | Current status | Requires | Can execute now? |
|------|-----------|--------------|----------------|----------|------------------|
| GATE-1 | IranDirect→PathVeer SCM upgrade | NOT EXECUTED | **BLOCKED** | legacy IranDirect artifact + VM | No |
| GATE-2 | Native route mutation/recovery | NOT EXECUTED | **BLOCKED** | VM (real SCM/routing authority) | No |
| GATE-3 | Windows reboot persistence | NOT EXECUTED | **BLOCKED** | VM (host reboot forbidden) | No |
| GATE-4 | Purge→reinstall | NOT EXECUTED | **BLOCKED** | VM | No |
| GATE-5 | Interactive fresh install | NOT EXECUTED | **BLOCKED** | VM (+ Authenticode → unsigned UX expected) | No |
| GATE-6 | Interactive upgrade | NOT EXECUTED | **BLOCKED** | VM | No |
| GATE-7 | Legacy migration UI | NOT EXECUTED | **BLOCKED** | IranDirect build | No |
| GATE-8 | Apps&Features uninstall/reinstall | NOT EXECUTED | **BLOCKED** | VM | No |
| GATE-9 | Update check→verify→handoff | PARTIAL (code) | **PARTIAL** | VM for Setup handoff past unsigned warning | No (handoff) |
| GATE-10 | Tampered installer rejected | PASS (code) | **PASS** | R2 transport tamper proven | N/A (code) |
| GATE-11 | Real staging HTTPS feed | NOT EXECUTED | **PASS** | proven in 37.6 (R2 real feed) | N/A |
| GATE-12 | Production-like immutable publication | PARTIAL (local FS) | **PASS** | proven in 37.6 (real Cloudflare R2) | N/A |

Notes:
- GATE-11 and GATE-12 were executed for real against Cloudflare R2 +
  `releases.pathveer.com` in Phase 37.6 (see `cloudflare-r2-certification.md`). The
  37.5 table predated that work; updated here to PASS.
- GATE-9 stays PARTIAL: the fetch→ES256-verify→download→hash-verify code path is
  proven (37.4 `DistributionTests` 11 tests + 37.6 real HTTPS). The **Setup handoff**
  past the unsigned-publisher warning requires a VM; it remains the open sub-item.
- GATE-10 stays PASS: both `Feed_TamperedInstaller_RejectedByHash` and
  `Feed_TamperedManifest_RejectedBySignature` (37.4) plus the R2 tamper transport
  test prove non-execution on hash/signature failure.

## 2. Non-legacy gates that CANNOT run without a VM

GATE-2/3/4/5/6/8 require an actual Windows guest. GATE-9's handoff sub-item and
GATE-1/7 require either a VM or a legacy artifact. **None are executable on this
host**, so they remain NOT EXECUTED / BLOCKED (not faked).

## 3. Code-path coverage already providing partial evidence (no VM)

`PathVeer.Core.Installation` (InstallOrchestrator / InstallLayout / InstallManifest /
PathEnvironmentEditor / ProductIdentity) is unit-tested (61 tests) and implements the
single-authority upgrade state machine: stop legacy → disable → stage/verify/swap →
repoint service → start → readiness probe → FINAL guard → retire legacy. This is the
install/upgrade logic the VM gates will exercise at runtime; it is **not** a substitute
for real Windows execution and is recorded as code evidence only.

## 4. IranDirect legacy artifact search (read-only)

Searched exhaustively for an authentic legacy IranDirect installer/build:

- **Git history** (`git log --all`): `b17550c Create initial IranDirect solution` is
  the earliest commit; its tree contains **source only** (`IranDirect.Core/Service/
  Cli/Tray`, `IranDirect.slnx`) — **no setup project, no MSI, no installer**.
- **Tag `v0.12.0-rc1`**: contains only `tools/Install-IranDirectService.ps1` (a service
  install script, not an installer artifact).
- **Local disks** (`C:\codespace`, Downloads, Desktop, ProgramData, Temp): only `.vs`
  solution-cache and `BenchmarkDotNet.Artifacts` logs reference "IranDirect" by
  historical product name — **no legacy installer binary**.
- PathVeer is the **rebrand successor** of IranDirect (Phases 36.1–36.8); the
  migration architecture (state-root copy `%ProgramData%\IranDirect`→`%ProgramData%\
  PathVeer`, SCM single-authority upgrade, dual-listen pipe compat) is implemented in
  source but was **never shipped as a discrete IranDirect installer** to migrate from.

**Finding:** No authentic IranDirect installer/build artifact exists in this
environment. GATE-1 and GATE-7 are **BLOCKED — authentic IranDirect artifact
unavailable**. A reproducible historical build from `b17550c` would be a *source*
build, not a previously distributed signed installer, and is explicitly excluded by
the task rules (do not reconstruct a fake legacy release and claim it representative).
The migration gate is therefore deferred with a precise runbook:
`vm-certification-runbook.md` §3 notes GATE-1/7 as BLOCKED pending an authentic
artifact supplied by the user.

## 5. Security

No production secrets or private keys were copied anywhere: the VM was never created,
so nothing entered a guest. The production ES256 key, R2 secret, and PFX backup remain
in `%LOCALAPPDATA%` (host, user-only ACL), outside the repo. Secret-leak scan clean.

## 6. Remaining blockers (genuine)

1. **Disposable VM provisioning** — host lacks CPU virtualization (firmware) + a
   legitimate Windows ISO. Required to execute GATE-1..8 (and the GATE-9 handoff).
2. **Production Authenticode certificate** — external embargo/org-only CA blocker
   (see `authenticode-provider-feasibility.md`); keeps GATE-5/6/9 partially blocked.
3. **Authentic IranDirect legacy artifact** — unavailable; GATE-1/7 BLOCKED until the
   user supplies one.

## 7. Decision

**CONDITIONALLY CERTIFIED.** All executable gates (GATE-10/11/12) are PASS; the
transport/hash/ES256 pipeline is real and verified. The Windows-execution gates and
the Authenticode/legacy prerequisites remain open and are documented with a runbook
ready to execute the moment a VM-capable host + ISO (+ optional legacy artifact) exist.
