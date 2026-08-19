# PathVeer — Disposable VM Certification Runbook

Reusable procedure for executing the **Phase 37.5 release-certification gate matrix
(GATE-1 .. GATE-12)** on a disposable Windows VM. This runbook is the authoritative
companion to `phase-37.5-release-certification.md` and `phase-37.8-vm-certification.md`.

It was prepared on the `development/service-authority` branch after a host-capability
audit found that a VM **could not be provisioned on the developer workstation at the
time** (see §0 of phase-37.8). That conclusion was **subsequently withdrawn**: the
`Win32_Processor.VirtualizationFirmwareEnabled = False` signal is not authoritative
under active Hyper-V/VBS, and a real Generation-2 VM (`PathVeer-Certification`) was
built and booted on this same host. The runbook below is therefore **executable
as-is** against the already-provisioned certification VM. The supplied documentation archive does not contain `phase-37.9-vm-execution.md`; treat any reference to it as historical until the repository confirms that file exists. Current certification status is in `../architecture-knowledge-base/AI/CURRENT.md`.

> **Correction (2026-08-12).** The certification VM **does exist and is operational**:
> `PathVeer-Certification` (Gen2, vTPM, Secure Boot On, 4 vCPU / 8 GiB, Default
> Switch, guest `PV-CERT` local admin `pvcert`), with a clean checkpoint
> `PV-CLEAN-WINDOWS`. PowerShell Direct from the host as `pvcert` is the supported
> host→guest automation channel. No BIOS/firmware change is required or permitted.

## 0. Prerequisites (VM-capable host) — verified satisfied

- Windows 10/11 Pro/Enterprise with the **Hyper-V role enabled** — ✅ confirmed.
- **Hyper-V execution capable** — ✅ confirmed (`HypervisorPresent = True`,
  `vmms` Running, real probe VM booted). Do NOT reclassify virtualization as blocked.
- A **legitimate Windows 11 x64 ISO** — ✅ supplied by the user (Windows 11 Business
  Editions 26H1 ISO).
- `Default Switch` (Internal NAT) — ✅ present; used by the certification VM.
- Host disk headroom for a dynamic VHDX — ✅ confirmed (~64 GiB RAM, ample disk).
- Network outbound HTTPS to `https://releases.pathveer.com` — ✅ the guest reaches the
  public release feed over the Default Switch NAT.

## 1. Provision the disposable VM (guarded)

```powershell
# Dry run first (no changes):
pwsh -NoProfile -File tools/certification/New-PathVeerCertificationVm.ps1 `
    -IsoPath <win11.iso>            # or -DownloadIso -IsoOutPath <path> -Apply

# Apply:
pwsh -NoProfile -File tools/certification/New-PathVeerCertificationVm.ps1 `
    -IsoPath <win11.iso> -Apply
```

- Gen2, UEFI, Secure Boot on (`MicrosoftWindows` template), 4 vCPU / 8 GiB (scale to host).
- Dynamic VHDX ≈ 100 GiB.
- Connect to `Default Switch`.
- **Do NOT copy production secrets into the guest** (see §7).

## 2. Install + clean checkpoint

1. Start the VM, install Windows 11, create a dedicated local admin account with a
   **disposable test password** (not the user's normal Windows password).
2. Install the .NET runtime representative of consumer installs.
3. Inside the guest (PowerShell, admin):
   ```powershell
   Checkpoint-VM -Name PathVeer-Cert -SnapshotName VM-CLEAN
   ```
4. Record `Get-PathVeerCertificationBaseline.ps1` output to the gate log.

## 3. Per-gate procedure (restore clean checkpoint between scenarios)

For every gate: `Restore-VMSnapshot -VMName PathVeer-Cert -Name VM-CLEAN -Confirm:$false`,
then run the scenario, then capture evidence with:

- `Get-PathVeerServiceEvidence.ps1` — SCM state (PathVeer + legacy IranDirect single-authority).
- `Get-PathVeerRouteEvidence.ps1` — bounded route state before/after (GATE-2).
- `Test-PathVeerInstallation.ps1` — layout: Service, CLI PATH, Tray, Start Menu, Apps&Features, state root.
- `Test-PathVeerReleaseArtifact.ps1` — installer/package SHA-256 vs frozen RC evidence.

### Gate → scenario map (authoritative source: phase-37.5 §5)

| Gate | Scenario | Checkpoint after |
|------|----------|------------------|
| GATE-5 fresh install | Run the selected certification `PathVeerSetup-*.exe` interactively; record the actual signer/publisher UX for that artifact | VM-PATHVEER-FRESH-INSTALLED |
| GATE-6 upgrade | Install older beta, then newer Setup; verify upgrade + state survival | VM-PATHVEER-UPGRADE-BASE |
| GATE-4 purge→reinstall | Purge state, reinstall, verify preserved-state recognition | — |
| GATE-8 uninstall/reinstall | Apps&Features uninstall → verify removal; reinstall → verify preserved state | — |
| GATE-2 route mutation/recovery | Before/after route table; capture journal; validate rollback | — |
| GATE-3 reboot persistence | Reboot VM only; verify Service startup + policy + routing post-reboot | — |
| GATE-9 update handoff | Update check → ES256 verify → download → hash verify → Setup handoff; record actual Authenticode/publisher state of the selected artifact | — |
| GATE-10 tamper | Tampered installer → rejected → Setup not launched (reuse R2 fixture) | — |
| GATE-1 / GATE-7 | IranDirect→PathVeer migration — **BLOCKED** (no authentic legacy artifact; see phase-37.8 §4) | — |

## 4. Service / Tray authority contract (GATE-H, architecture)

Inside the guest, prove:
- Service registered as routing authority; automatic startup.
- Tray optional UI/controller.
- `Stop-Process` the Tray → Service keeps running, startup type unchanged, routing policy intact.
- CLI usable while Tray absent.
Capture with `Get-PathVeerServiceEvidence.ps1` + manual SCM queries.

## 5. Reboot / persistence (GATE-3)

Reboot the **VM only**. After reboot capture: Service startup state, Enabled/Disabled
policy, routing ownership, Tray behavior, IPC availability, persisted config. Compare to pre-reboot snapshot.

## 6. Authenticode position

Do not hard-code the runbook to an unsigned installer. Certification artifacts may be unsigned, development-signed, or production-signed depending on the current gate.

Before each run, record:

- artifact version and SHA-256;
- `Get-AuthenticodeSignature` status;
- signer subject when present;
- whether the certificate is development/private or production-public trust.

Do **not** disable Windows signature enforcement. If a deliberately unsigned/private-development artifact must be exercised inside the disposable VM, operator-approved continuation must be documented and must not be reported as production Authenticode proof.

## 7. Secrets / safety

- **Never copy into the guest:** R2 Secret Access Key, production ES256 private key,
  KeePassXC vault, production metadata signing key, any PFX/PKCS#12 private key.
- The guest needs only **public** release artifacts + trust anchors (embedded in the
  client build).
- Use a disposable local guest password; do not commit it; do not print it in final logs.

## 8. Cleanup

Retain the final VM state + `VM-CLEAN` checkpoint + textual evidence until the
certification report is reviewed. Clean only: temporary tamper files, throwaway
installers, scripts containing secrets. Never commit VHDX/ISO/checkpoints/secrets.

## 9. Result classification (per gate)

Only `PASS | PARTIAL | BLOCKED | FAIL | NOT APPLICABLE`.
- Transport/hash/ES256 portion PASS + Authenticode portion BLOCKED ⇒ overall `PARTIAL`.
- Do not convert `BLOCKED` to `PASS` because architecture/code tests exist.
