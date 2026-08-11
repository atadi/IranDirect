# PathVeer — Disposable VM Certification Runbook

Reusable procedure for executing the **Phase 37.5 release-certification gate matrix
(GATE-1 .. GATE-12)** on a disposable Windows VM. This runbook is the authoritative
companion to `phase-37.5-release-certification.md` and `phase-37.8-vm-certification.md`.

It was prepared on the `development/service-authority` branch after a host-capability
audit found that a VM **cannot be provisioned on the current developer workstation**
(see §0 of phase-37.8). When a VM-capable host + legitimate Windows ISO become
available, execute this runbook verbatim — no redesign needed.

## 0. Prerequisites (VM-capable host)

- Windows 10/11 Pro or Enterprise with the **Hyper-V role enabled**.
- **CPU virtualization (VT-x / AMD-V) enabled in firmware** — Hyper-V will not run a
  VM without it. Verify with:
  `Get-CimInstance Win32_Processor | Select VirtualizationFirmwareEnabled`
  (must be `True`).
- A **legitimate Windows 11 x64 ISO** (Microsoft Evaluation Center image, or a
  licensed retail/VL ISO). No unofficial images.
- `Default Switch` (Internal NAT) or an isolated NAT switch present.
- Host disk headroom for a dynamic VHDX (≈80–100 GiB max, thin-provisioned).
- Network outbound HTTPS to `https://releases.pathveer.com`.

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
| GATE-5 fresh install | Run `PathVeerSetup-*.exe` interactive; note Unknown Publisher (unsigned) UX | VM-PATHVEER-FRESH-INSTALLED |
| GATE-6 upgrade | Install older beta, then newer Setup; verify upgrade + state survival | VM-PATHVEER-UPGRADE-BASE |
| GATE-4 purge→reinstall | Purge state, reinstall, verify preserved-state recognition | — |
| GATE-8 uninstall/reinstall | Apps&Features uninstall → verify removal; reinstall → verify preserved state | — |
| GATE-2 route mutation/recovery | Before/after route table; capture journal; validate rollback | — |
| GATE-3 reboot persistence | Reboot VM only; verify Service startup + policy + routing post-reboot | — |
| GATE-9 update handoff | Update check → ES256 verify → download → hash verify → Setup handoff (past unsigned warning, operator-approved) | — |
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

The installer is **unsigned** (production Authenticode externally blocked — see
`authenticode-provider-feasibility.md`). Expected UX: `Unknown publisher` /
SmartScreen reputation warning. Do **not** disable Windows signature enforcement.
Where a gate needs to continue past the warning, operator-approved continuation
**inside the disposable VM** is acceptable and must be documented as such.

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
