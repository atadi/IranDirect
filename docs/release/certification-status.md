# PathVeer Certification Status — Historical beta.1 Snapshot

> **Historical snapshot — not current engineering status.**
>
> This document records an earlier beta.1/JEA certification state. Later installer
> certification discovered additional real product defects (including WinExe startup
> and progress-reader lifecycle failures). Do not use the conclusions below to decide
> current release readiness.
>
> Current engineering state: `../architecture-knowledge-base/AI/CURRENT.md`
>
> Current release publication remains separately authorized and is never implied by
> this historical record.


**Authoritative as of commit `953edd3`** on branch `hermes/hermes-07ff3473`.
This was the consolidated certification-status artifact at the time of this snapshot. Phase docs
(`phase-37.5-release-certification.md`, `phase-37.6-release-closure.md`,
`phase-37.8-vm-certification.md`, `cloudflare-r2-certification.md`) are
historical closeouts; this file supersedes their gate matrices by reference.

- Candidate: **PathVeer 1.0.0-beta.1** (immutable; bytes unchanged since frozen).
- Harness: JEA control plane (`tools/certification/jea`), Desktop orchestrator
  (`tools/certification/Invoke-PathVeerCertification.ps1`), checkpoint
  `PV-CERT-HARNESS` derived from `PV-CLEAN-WINDOWS`.
- Real-VM gates executed: GATE-2, GATE-3, GATE-4, GATE-5, GATE-8, GATE-9, GATE-28.
- GATE-10/11/12 proven in Phase 37.6 (real Cloudflare R2 + `releases.pathveer.com`),
  not by the current JEA VM harness (which has no `Run-GATE10/11/12`).
- No stable publication performed. Publication is a separate explicit operator action.

---

## 1. Final certification matrix

| Gate | Final State | Classification | Evidence | Blocking Prerequisite | Rerun? | Eng. Work? |
|------|-------------|----------------|----------|----------------------|--------|-----------|
| GATE-1 IranDirect→PathVeer SCM upgrade | BLOCKED | HISTORICAL ARTIFACT / EXPECTED EXTERNAL | none (no VM, no artifact) | authentic historical IranDirect installer | No | No |
| GATE-2 custom-route lifecycle | PASS / CLOSED | REAL-VM VALIDATED | `02-gate2-custom-route.json` (OUT_OF_SCOPE_ONLY, resolved includes/excludes) | — | No | No |
| GATE-3 reboot persistence | PASS / CLOSED | REAL-VM VALIDATED | `03-gate3-reboot-persistence.json` | — | No | No |
| GATE-4 purge→reinstall | PASS / CLOSED | REAL-VM VALIDATED (product fix) | `04-gate4-purge-reinstall.json` | — | No | No |
| GATE-5 interactive fresh install / JEA boundary | PASS / CLOSED | REAL-VM VALIDATED | `05-gate5-fresh-install.json`, `23-gate5-interactive-prepared.json` | — | No | No |
| GATE-6 interactive upgrade | BLOCKED | RELEASE PRECONDITION | none | two genuine product versions (beta.2 needs real next RC) | No | No |
| GATE-7 legacy migration UI | BLOCKED | HISTORICAL ARTIFACT / EXPECTED EXTERNAL | none (no IranDirect build) | authentic IranDirect installer | No | No |
| GATE-8 Apps&Features uninstall/reinstall | PASS / CLOSED | REAL-VM VALIDATED (product fix) | `08-gate8-uninstall-reinstall.json` | — | No | No |
| GATE-9 update check→verify→handoff | PARTIAL / EXPECTED EXTERNAL | REAL-VM VALIDATED subchecks + expected blockers | `09-gate9-update-flow.json` | production Authenticode cert; prod metadata key | No | No |
| GATE-10 tamper rejection | PASS / CLOSED | REAL (Phase 37.6 R2) + code tests | `phase-37.6`/`cloudflare-r2-certification.md`, 37.4 tests | — | No | No |
| GATE-11 real staging HTTPS feed | PASS / CLOSED | REAL (Phase 37.6 R2) | `cloudflare-r2-certification.md` | — | No | No |
| GATE-12 production-like immutable publication | PASS / CLOSED | REAL (Phase 37.6 R2) | `cloudflare-r2-certification.md` | — | No | No |
| GATE-28 same-version repair | PASS / CLOSED | REAL-VM VALIDATED (harness fix) | `28-gate28-repair.json` | — | No | No |

---

## 2. Authoritative PASS / CLOSED

GATE-2, GATE-3, GATE-4, GATE-5, GATE-8, GATE-9 (network/hash subchecks),
GATE-10, GATE-11, GATE-12, GATE-28.

GATE-9 is PARTIAL: its network-reachability + SHA-256 immutable-verify +
ES256-staging-verify subchecks PASS; its production-ES256-trust and
Authenticode subchecks BLOCKED by expected external prerequisites (see §4).

## 3. Blocked gates

- **GATE-1** — HISTORICAL ARTIFACT BLOCKER (authentic IranDirect installer unavailable).
- **GATE-6** — RELEASE PRECONDITION BLOCKER (needs two genuine product versions).
- **GATE-7** — HISTORICAL ARTIFACT BLOCKER (authentic IranDirect installer unavailable).
- **GATE-9** — PARTIAL / EXPECTED EXTERNAL BLOCKER (Authenticode cert; prod metadata key).

## 4. GATE-9 subcheck matrix

| Subcheck | Result | Classification | External prerequisite |
|----------|--------|----------------|----------------------|
| Network reachability / HTTPS fetch | PASS | REAL-VM | — |
| Content hash / immutable candidate verify | PASS | REAL-VM | — |
| Metadata ES256 verify (staging key) | PASS | REAL-VM | — |
| Metadata production trust (prod key) | FAIL (expected) | EXPECTED EXTERNAL BLOCKER | provision `pv-meta-prod-2026-01` in prod client trust root |
| Authenticode signing | BLOCKED (expected) | EXPECTED EXTERNAL BLOCKER | production code-signing certificate |

The production-ES256 FAIL is the **intended fail-closed policy** (client rejects
non-production key); it is NOT a trust-config defect. Production Authenticode
BLOCK is the absence of a signing cert, not a product defect.

## 5. Defect history (all closed)

- **Real product defect** (GATE-4/GATE-8): `Invoke-Uninstall` initialized
  `$version` from `$null` instead of the installed manifest → fixed at `61bed9d`,
  REAL-VM validated by GATE-4 (purge) and GATE-8 (normal uninstall).
- **Harness invocation-contract defect** (GATE-4): PurgeUninstall not translated
  to product `uninstall -PurgeState` → fixed at `b7583e7`.
- **Harness invocation-contract defect** (GATE-28): Repair/Upgrade forwarded
  verbatim as `-Action Repair`/`-Action Upgrade`, rejected by product → fixed at
  `32138e7` (map to product `install`).
- **Harness enum-normalization defect** (GATE-28): raw remoted service-state
  object compared to string `'Running'` → fixed at `953edd3` (normalize via
  shared `Get-EnumString`, retain raw + normalized evidence).

At the time of this snapshot, no unresolved REAL PRODUCT or HARNESS defect was known. Later artifact-level testing superseded that conclusion.

## 6. JEA / CLI invariants (verified from source + tests)

- `VisibleFunctions` = **exactly 12** (enforced by `Test-PathVeerCertGuestJeaBinder.ps1`).
- Action translation table (no unsupported token reaches product):
  - Install → product `install`
  - Upgrade → product `install`
  - Repair → product `install`
  - Uninstall → product `uninstall`
  - PurgeUninstall → product `uninstall -PurgeState`
- CLI SubVerb binder (exactly 11, unchanged):
  `list, add-domain, add-ip, add-cidr, enable, disable, remove, resolve, status, invalidate, invalidate-all`.

## 7. Beta.1 immutability

`artifacts/releases/1.0.0-beta.1/win-x64/PathVeer-1.0.0-beta.1` is **unchanged**
by any fix (installer `61bed9d`, JEA `32138e7`, orchestrator `953edd3`). Installer
and JEA are independent of package bytes. **No beta.2 required for certification fixes.**

## 8. Remaining blockers before stable-publication eligibility

1. Production Authenticode code-signing certificate (procure + sign PE artifacts).
2. Production ES256 metadata key provisioned + injected into client trust root
   (`PATHVEER_TRUSTED_META_KEYS`, key `pv-meta-prod-2026-01`).
3. Authentic historical IranDirect installer (GATE-1/7) — when available, runnable.
4. Genuine next product version (beta.2) — only when a real RC exists (GATE-6).
5. RIPEstat commercial-terms review (business/legal, external).
6. Disposable-VM re-run of GATE-1/6/7 once artifacts exist (optional reconfirmation).

No engineering work is required to close the currently-runnable beta.1 paths.

## 9. No-rerun list (closed/partial, do not rerun for closure)

GATE-2, GATE-3, GATE-4, GATE-5, GATE-8, GATE-9, GATE-28. Blocked gates not
rerun merely to reconfirm expected blockers.

## 10. Phase-closure recommendation

**Historical conclusion at this snapshot: CLOSE WITH EXTERNAL BLOCKERS.**

All runnable core gates pass; no unresolved harness or product defects remain.
The only open items require genuinely unavailable historical artifacts (GATE-1/7),
another real product version (GATE-6), or external signing credentials (GATE-9).
Engineering certification for the currently-runnable beta.1 paths is complete.

> This does NOT authorize stable release, beta publication, R2 mutation,
> latest.json publication, metadata-signing changes, release tagging, or GitHub
> Release creation. Publication remains a separate explicit operator authorization.
