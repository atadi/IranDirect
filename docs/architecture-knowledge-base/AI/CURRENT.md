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
