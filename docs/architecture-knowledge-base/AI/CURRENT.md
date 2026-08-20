# PathVeer — Current Engineering State

This is the single committed document that answers:

> What is PathVeer working on right now?

Read it immediately after `AI-START-HERE.md`.

Keep this document current and concise. Historical detail belongs in phase, evolution, or release-certification records. Machine-specific state belongs in `AI-LOCAL-STATE.md`.

---

## Current Product

Current identity: **PathVeer**.

IranDirect is the legacy predecessor and remains relevant only where required for migration, rollback, persisted compatibility, legacy IPC/Service behavior, compatibility shims, or historical records.

---

## Expected Development Branch

Historically:

```text
development/service-authority
```

Always verify the actual checkout, HEAD, upstream, ahead/behind, and working tree before implementation.

Last known certified source from the installer-certification sequence:

```text
aea0e8c  fix(setup): render installer logs as plain text and fix failure wording
```

This commit value is evidence from the certification history, not a permanent assumption.

---

## Current Mission

PathVeer is a Windows network control plane that:

- sends configured country IPv4 prefixes through the physical ISP path;
- leaves other traffic on the VPN path;
- protects VPN endpoint connectivity;
- maintains routing through one authoritative Windows Service.

Safety rule:

> VPN endpoint connectivity has priority over direct-prefix routing.

---

## Current High-Level Architecture

```text
DesiredConfiguration
        |
        v
RuntimeCoordinator
   |             |
   v             v
RuntimeObserver  RuntimePlanner
   |             |
   v             v
ObservedRuntime  DesiredRuntime
        \       /
         \     /
      RuntimePlanSnapshot
              |
              v
     RuntimeReconciler
              |
              v
   RuntimeChangeSet
              |
              v
 RuntimeExecutionPlanner
              |
              v
    RuntimeExecutionPlan
              |
              v
 RuntimeDecisionBuilder
              |
              v
      RuntimeDecision
              |
              v
 RuntimeCycleCoordinator
              |
              v
      RuntimeExecutor
              |
              v
 Windows Networking Platform
```

CLI and Tray reach this authority through IPC. The Service is the only machine route-mutation authority.

The routing architecture above is established and is **not** the current milestone.

---

## Current Engineering Phase

```text
Windows installer / runtime release certification — CLOSED
```

The installer-certification slice (elevated Tray launch ownership, installed-Tray
quiescence before binary swap, plain-text installer logging, failure-wording,
progress-tailer lifetime, ANSI-free GUI capture) is complete and certified.

---

## Current Certification State

**devsign.10 is CERTIFIED (Development/Signed).**

```text
Certified artifact:
  PathVeerSetup-1.0.0-devsign.10-win-x64.exe

Artifact source commit:
  aea0e8c1151327920ef1b0f1bd67ea2acec4b243

Certification evidence/tooling HEAD:
  4474403f89142b6e6522d949c586ebb90180f6e2
  (helper fix + package-test harness fix; later documentation-only closure
  commits do NOT rebuild or alter the binary)

Trust:
  Development/Signed
  pv-meta-dev-2026-01  (development ES256 key; NOT production)

Status:
  FROZEN + CERTIFIED DEVELOPMENT/SIGNED SPECIMEN
  Not a production publication artifact.
```

Live operator acceptance (TESTS 1-5) passed:

- normal Explorer/UAC install -> Tray count 1, Elevated=False, Service Running/Automatic;
- same-version Repair while Tray running -> old PID removed, fresh PID, Elevated=False (Accessibility.dll lock defect closed);
- 10x Tray launches -> count stays 1;
- forced Tray termination/restart -> recovers; normal-UI launch Elevated=False;
- direct Run-as-administrator Setup -> Tray count 0 (fail-safe, no elevated Tray).

Detailed evidence: `docs/release/certification-devsign.10-proof.md`.

---

## Current Product Blocker

**NONE from the completed installer-certification slice.**

All five live operator tests passed; all automated gates (provenance,
package integrity, Authenticode, ES256, SideBySide, WinExe, beta.1 hash)
passed.

---

## Current Phase

```text
installer certification phase CLOSED
```

---

## Frozen Specimens

`1.0.0-beta.1` is frozen historical evidence. Do not rebuild, re-sign,
overwrite, modify, or republish it.

Expected SHA-256 (verified against the actual frozen exe):

```text
7e5b4301756dfa617509c8a533aed3000e7025d2b3470150b0e6933361ffa24d
```

`1.0.0-devsign.8` and `1.0.0-devsign.9` are FROZEN FAILED REGRESSION SPECIMENS
(their defects were closed in devsign.10). `1.0.0-devsign.10` is the FROZEN
CERTIFIED specimen. The release tooling now refuses to rebuild an existing
version (fail-closed overwrite guard).

Canonical freeze record: `docs/release/certified-artifacts.md`.

---

## Trust Invariants

Production metadata identity:

```text
pv-meta-prod-2026-01
allowUnsigned=false
```

Development certification identity:

```text
pv-meta-dev-2026-01
```

Production trust uses committed/built-in public anchors. Development/staging
overrides must not be merged into production trust.

Do not publish and do not modify `R2/latest.json` during certification without
explicit authorization.

---

## Recently Proven / Closed Items

The installer-certification sequence proved and corrected defects involving:

- development Authenticode certificate profile/trust installation;
- invalid Windows application-manifest hierarchy;
- isolated development ES256 metadata trust;
- corrupt internal package-relative paths;
- stale package hashes after Authenticode signing;
- self-elevation mutex ordering;
- explicit interactive Setup exit-code propagation;
- installer icon/friendly version;
- WinExe startup crash caused by `Console.Title` without a console;
- intrinsic Tray single-instance ownership;
- non-elevated post-install Tray-launch design;
- elevated Tray launch ownership moved to the non-elevated parent;
- installed-Tray quiescence before lifecycle mutations;
- plain-text installer logging (no ANSI in GUI capture);
- progress-reader lifetime / result-processing contract.

Detailed evidence belongs under `docs/release/`; do not duplicate it here.

---

## Phase B — Production Release / Update Trust Hardening (engineering gaps CLOSED)

The installer-certification slice is closed (devsign.10 CERTIFIED). Phase B
closed the remaining **engineering** gaps required before an operator could
build `1.0.0-rc.1` as the first production-trust release candidate. This is
engineering readiness only — it does **NOT** authorize publication (see
external blockers below).

### What changed

- **Gap A — update-apply pipeline is now real (not architecture-only).**
  `PathVeer.Core.Update.UpdateApplyCoordinator` orchestrates the previously
  document-only chain: manifest (already ES256-verified by `UpdateChecker`)
  → bounded streaming download to a `.partial` file (never fully buffered in
  RAM, size-bounded) → verify expected size + installer SHA-256 + production
  Authenticode + expected publisher → atomic promote `.partial` → final →
  re-verify final (TOCTOU closure). A failed verification never promotes and
  never returns an executable path. The Tray now offers explicit, user-initiated
  "Download and Install" and launches Setup via `UseShellExecute=true` (Setup's
  own normal UAC flow); it never calls `Process.Start` on an unverified file.
- **Production publisher policy has one authoritative home.**
  `PathVeer.Core.Update.ProductionSigningPolicy` is the ONLY source of the
  expected production publisher identity. It is NOT derived from the manifest,
  R2, or any remote/env source. While `CurrentPublisher` is
  `UNPROVISIONED`, every production installer signature check fails closed.
  Provisioning = change one non-secret constant once the real certificate exists.
- **Gap B — production ES256 DPAPI store is wired through the release
  orchestrator.** `New-PathVeerRelease.ps1` now accepts `-ProductionKeyStore`
  (default `%LOCALAPPDATA%\PathVeer\Secrets`) and passes it to
  `Sign-ReleaseManifest.ps1` for `Release/Signed`, so signing reads the
  protected DPAPI key blob in-process instead of requiring `PATHVEER_META_SIGN_KEY`.
  Development keys remain strictly isolated.
- **Gap C — signtool discovery is deterministic.** `tools/Find-PathVeerSignTool.ps1`
  prefers the x64 SDK binary on a win-x64 host (never silently choosing arm64
  due to enumeration order), honors an explicit `-ToolPath`, and fails cleanly
  when absent. Wired into both `Sign-PathVeerArtifacts.ps1` and
  `Test-PathVeerAuthenticodeReadiness.ps1`.
- **PowerShell 7 prerequisite is explicit.** `Publish-PathVeerRelease.ps1` and
  `PathVeerR2.psm1` now carry `#requires -Version 7` so AWS.Tools.S3 /
  pwsh-7 syntax fails early on Windows PowerShell 5.1 instead of with a confusing
  later error.

### Readiness matrix

```text
R2 distribution                     READY (immutable, idempotent, channel-last)
production ES256                   READY (pv-meta-prod-2026-01, built-in trust)
release DPAPI integration          READY (wired through New-PathVeerRelease)
update download/staging pipeline   READY (UpdateApplyCoordinator, verified)
Authenticode enforcement (apply)   READY architecturally (UNPROVISIONED = fail-closed)
production Authenticode cert       EXTERNAL BLOCKER (no cert provisioned)
production publisher identity      WAITING ON REAL CERT / operator confirmation
RIPEstat commercial terms          EXTERNAL BUSINESS/LEGAL BLOCKER
rc.1 build                         NOT AUTHORIZED (do not build until cert ready)
```

### Security invariants (unchanged)

- `ReleaseSignatureVerifier.ForProduction()` — built-in trust only, `allowUnsigned=false`.
- `PATHVEER_TRUSTED_META_KEYS` never merged into production trust.
- Production Windows update apply requires ALL: valid signed manifest, known
  production ES256 key, expected installer size, installer SHA-256, valid
  Authenticode trust chain, expected publisher policy. HTTPS/R2 is transport,
  not the final trust authority.
- No signing secrets in repo / logs / manifest / R2 / tests / CLI output.

Detailed evidence: `docs/release/phase-B-production-release-readiness.md`.

---

## Next Decision (awaiting explicit authorization)

Installer certification is closed. The next action is **NOT automatically
active** merely because devsign.10 certification passed.

Awaiting an explicit operator/product decision about:

1. the next release version; and
2. the production release/signing ceremony strategy (production Authenticode
   identity, `pv-meta-prod-2026-01` trust provisioning, publication
   authorization).

Do not start a production release, rebuild beta.1, or create devsign.11 without
that authorization. devsign.10 is a certified **development** specimen, not a
production publication.

---

## First Question

> What is the next release version and production release/signing ceremony, and
> is publication explicitly authorized?
