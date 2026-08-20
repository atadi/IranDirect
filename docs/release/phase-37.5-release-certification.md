# Phase 37.5 — Release Certification / Production Trust / Disposable-VM Readiness

> **Historical record.** This document captures the project state at the time of Phase 37.5. It is not the current PathVeer engineering-status authority. See `docs/architecture-knowledge-base/AI/CURRENT.md` for current state; see `certified-artifacts.md` for the artifact/freeze registry.

**Date:** 2026-08-10
**Starting boundary:** `de86cea` (Phase 37.4 closeout, branch `development/service-authority`)
**Phase 37.4 decision:** CONDITIONAL PASS — implementation complete; production trust + hosting outstanding.
**Phase 37.5 decision:** **CONDITIONALLY CERTIFIED** (see §Decision).

---

## 1. Environment reality (determines what could be executed)

| Item | Finding | Consequence |
|------|---------|-------------|
| Host | Windows 11 Pro, Build 26200 (developer workstation) | Destructive host tests forbidden by §62 |
| Hypervisor | Hyper-V role enabled, **no disposable VM provisioned** | VM GATE-1..12 cannot be physically executed |
| IranDirect build | No IranDirect source/build in tree (only a `.vs` solution pointer) | Legacy-upgrade gates have no executable artifact |
| Production Authenticode cert | No Code-Signing EKU certificate in user/local store | Authenticode production signing BLOCKED |
| Production metadata key | No production private/public key material present; trusted key was **not embedded** in client | Metadata production trust BLOCKED (defect fixed below) |
| Staging HTTPS host / DNS / CDN | Not provisioned / no `releases.pathveer.com` infra | GATE-11/12 cannot be executed against real network |
| Signing abstraction | `Sign-PathVeerArtifacts.ps1` already supports AzureSignTool / PFX / thumbprint | §74/§75 already satisfied architecturally |

**Do-not-invent rule applied:** no VM, no cert, no DNS, no IranDirect artifact, no cloud host was fabricated. Every gate is classified from real evidence.

---

## 2. Certification matrix classification

| Category | Gates / work |
|----------|--------------|
| AUTOMATED CODE TEST | All signing/verification/immutability/tamper tests (Core + 37.4 + 37.5) |
| LOCAL NON-DESTRUCTIVE | RC generation, ES256 sign/verify, Authenticode pipeline (dev-mode), publisher dry-run/local publish, immutability, channel isolation, secret scan, build/test gate |
| DISPOSABLE VM | GATE-1..12 — NOT EXECUTED (no VM) |
| REAL STAGING NETWORK | GATE-11, GATE-12 — NOT EXECUTED (no host/infra) |
| PRODUCTION TRUST | Metadata: fixed + tested (BLOCKED on real key provisioning); Authenticode: BLOCKED on cert |

---

## 3. Release candidate (frozen, from `de86cea`)

Version-string constraint: the build tooling (`New-PathVeerPackage.ps1`) accepts only `^\d+\.\d+\.\d+$`, so the RC uses **version `1.0.0` on the `beta` channel** (the established 37.4 prerelease model — channel, not a `-rc.N` suffix, denotes prerelease).

| Artifact | Value |
|----------|-------|
| Version / channel | `1.0.0` / `beta` |
| Git commit | `de86cea` |
| Installer | `PathVeerSetup-1.0.0-win-x64.exe` |
| Installer SHA-256 | `8871c9b73cc7f173a2b1da468249612e9fc8392d76343cded918b722b4ad7670` |
| Package SHA-256 | `cd94f2a7b4f6b23d2d18bafb4f2b43f34f5ac78cbd4fe8edbb7e9f14be2f1e65` |
| Manifest SHA-256 | `ccd9651de61751bdf24b27f178b41cbc70866f337bfca1c5b057ad537bd6daa8` |
| Metadata keyId | `pv-meta-rc1` (**TEST key**, env-only, never committed) |
| Authenticode | **UNSIGNED** (no production cert in environment) |
| RFC3161 timestamp | N/A (no Authenticode) |

> The hashes above are from a regenerated frozen RC on this machine; the bytes are reproducible from `de86cea` via `New-PathVeerRelease.ps1` + `Sign-ReleaseManifest.ps1`. No release binary is committed (gitignored).

---

## 4. Defect found and fixed (in scope, §74)

**Defect:** The production client wiring `PathVeer.Tray/TrayApplicationContext.cs::BuildReleaseVerifier()` shipped an **empty trusted key set with `allowUnsigned: true`**. This meant the production PathVeer client trusted no release-metadata key and would **accept unsigned manifests** — a release-blocking production-trust gap (§14).

**Fix:** Added `ReleaseSignatureVerifier.FromEnvironment(bool devAllowUnsigned)` which loads the trusted key set from `PATHVEER_TRUSTED_META_KEYS` (`keyId:base64(64-byte Q.X||Q.Y);...`). When any trusted key is present, unsigned manifests are **rejected** (signed-only). When no key is configured (dev/unsigned builds), `devAllowUnsigned` preserves the developer experience. `BuildReleaseVerifier()` now calls `FromEnvironment(devAllowUnsigned: true)`. This keeps the trust root secret-safe — no key is committed; production provisions the public keys via env / signed config / secret store.

**Regression coverage added** (`UpdateArchitectureTests.cs`):
- `Verifier_FromEnvironment_RejectsUnsigned_WhenTrustedKeyConfigured`
- `Verifier_FromEnvironment_VerifiesSigned_WhenTrustedKeyConfigured`
- `Verifier_FromEnvironment_AllowsUnsigned_WhenNoTrustedKeyConfigured`

All three pass. This converts the §14 prerequisite from "open" to "architecture present; awaits real key provisioning."

---

## 5. VM gate status (GATE-1 .. GATE-12)

| Gate | Status | Evidence / reason |
|------|--------|-------------------|
| GATE-1 IranDirect→PathVeer SCM upgrade | BLOCKED | Authentic IranDirect build artifact unavailable (searched repo/git/local; see 37.8 §4); no VM |
| GATE-2 Native route mutation/recovery | BLOCKED | No VM; would need real SCM/routing authority in guest |
| GATE-3 Windows reboot persistence | BLOCKED | No VM; host reboot forbidden by §62 |
| GATE-4 Purge→reinstall | BLOCKED | No VM |
| GATE-5 Interactive fresh install | BLOCKED | No VM + no production-signed Setup (unsigned → Unknown Publisher expected) |
| GATE-6 Interactive upgrade | BLOCKED | No VM |
| GATE-7 Legacy migration UI | BLOCKED | No IranDirect build (see 37.8 §4) |
| GATE-8 Apps&Features uninstall/reinstall | BLOCKED | No VM |
| GATE-9 Update check→verify→handoff | PARTIAL | Fetch+ES256-verify+download+hash-verify proven (37.4 `DistributionTests` 11 tests + 37.6 real HTTPS); Setup handoff past unsigned warning needs VM |
| GATE-10 Tampered installer rejected | PASS | 37.4 hash/sig tests + 37.6 R2 tamper transport test prove non-execution on failure |
| GATE-11 Real staging HTTPS feed | PASS | 37.6 real Cloudflare R2 + `releases.pathveer.com` (see `cloudflare-r2-certification.md`) |
| GATE-12 Production-like immutable publication | PASS | 37.6 real R2 immutable + latest-last + stable/beta isolation (see `cloudflare-r2-certification.md`) |

VM execution gates (GATE-1..8, GATE-9 handoff) could not be physically run on the
current developer workstation: Hyper-V is enabled but **CPU virtualization is disabled
in firmware** and **no legitimate Windows ISO exists** on the host (37.8 §0). They are
marked BLOCKED, not faked. A runbook (`vm-certification-runbook.md`) is ready to
execute them on a VM-capable host.

---

## 6. Authenticode (§8–§12)

- **Provider / certificate:** none provisioned in this environment.
- **Mechanism available:** `Sign-PathVeerArtifacts.ps1` supports AzureSignTool (Key Vault), PFX, or thumbprint — so a production cert plugs in without code change.
- **Targets defined (§10):** `PathVeerSetup.exe`, `PathVeer.Service.exe`, `PathVeer.Cli.exe`, `PathVeer.Tray.exe`.
- **Verification result:** UNSIGNED in this run (developer mode by design when no credential present). Production `Get-AuthenticodeSignature` / `signtool verify /pa /all` must be run once a cert is provisioned.
- **RFC3161 timestamp:** must accompany production signing (Digicert timestamp URL is already wired in the signer).

**Status: BLOCKED** — public release cannot be Authenticode-signed until a code-signing certificate is procured.

---

## 7. Metadata signing (§13–§15)

- ES256 pipeline: **validated** — `Sign-ReleaseManifest.ps1` signs and `VerifyOnly` re-verifies the identical canonical payload; `DistributionTests` + parity tests confirm PS↔C# canonical agreement.
- Production trusted-key wiring: **fixed and tested** (§4). Client now fails closed when keys are present.
- Key rotation architecture (§15): verified with test keys — a verifier constructed with two trusted keys accepts a manifest signed by either; removable-key support is inherent (Dictionary keyed by keyId). No production rotation needed at initial release.
- **Production key provisioning:** not performed — no production private key material exists. Recommendation: provision via Azure Key Vault / Managed HSM or CI secret-backed PEM; interface required is the 64-byte `Q.X||Q.Y` public key injected through `PATHVEER_TRUSTED_META_KEYS`.

**Status: BLOCKED** on production key provisioning (architecture + client wiring complete).

---

## 8. Release pipeline executed (§16) — local, from frozen commit

```
git checkout de86cea (current HEAD)
-> New-PathVeerRelease.ps1 -Version 1.0.0 -Channel beta -Mode Development/Unsigned
   -> component package + Setup publish + hashes + manifest        [PASS]
-> Sign-PathVeerArtifacts.ps1 (Authenticode)                       [UNSIGNED — no cert]
-> Sign-ReleaseManifest.ps1 (ES256 metadata)                       [PASS, keyId pv-meta-rc1]
-> Sign-ReleaseManifest.ps1 -VerifyOnly (trusted key)              [PASS — signature VERIFIED]
-> Publish-PathVeerRelease.ps1 (Local backend, beta, staging)      [PASS]
   -> immutable versioned objects uploaded
   -> installer hash verified against manifest                     [PASS]
   -> same-version/different-bytes rejected                       [PASS]
   -> beta/latest.json written LAST; stable/latest.json absent    [PASS — isolation]
```

---

## 9. Fresh build / test gate (§77–§78)

| Suite | Result |
|-------|--------|
| Debug solution build | PASS (0 errors) |
| Core tests | 2577 passed / **1 failed** |
| Service tests | 50 passed / 0 failed |
| 37.4 focused (DistributionTests) | 11 passed / 0 failed |
| 37.5 focused (UpdateArchitectureTests, incl. new FromEnvironment) | 38 passed / 0 failed |
| Benchmarks Release build | 0 errors |
| Release generation (RC 1.0.0 beta) | PASS |
| Signature verification | PASS |
| Manifest verification | PASS |

**The 1 Core failure** is `PersistenceEnduranceConcurrencyTests.CrossInstance_ReadersAndSingleWriter_NoMalformedReads`. It passes **3/3 in isolation** and is a pre-existing concurrency/timing-sensitive test, unrelated to 37.5 (which only touched the release-signature verifier and tray wiring). Not a release-blocking defect; tracked as existing flakiness, not introduced here.

---

## 10. Security (§19, §80)

- No private keys, PFX, PEM, passwords, client secrets, or token values committed.
- `PATHVEER_META_SIGN_KEY` / `PATHVEER_TRUSTED_META_KEYS` / `AZURE_*` appear only as **env-var names** (secret-safe pattern).
- Secret scan over all source/doc/config found no literal secret.
- Production signatures: not verifiable (none produced).
- Manifest signatures: verified (test key).
- Tamper rejection: proven (37.4 tests).
- Immutable versions cannot be overwritten with different bytes: proven (publisher hard-fail).

---

## 11. Business / legal prerequisites (§53, §72, §73, §20)

| Item | Status |
|------|--------|
| RIPEstat terms / commercial-use review | **OPEN** — pre-launch terms review not completed; must be closed by business before public release |
| Code-signing identity / publisher legal identity | **OPEN** — certificate not procured |
| Production metadata key | **OPEN** — architecture + client wiring done; real key not provisioned |

None of these were invented or silently marked complete.

---

## 12. Outstanding blockers (real, release-gating)

1. **Production Authenticode certificate** not procured → cannot sign PE artifacts (GATE-5/6/9 partially blocked). See `authenticode-provider-feasibility.md` (external embargo/org-only blocker).
2. ~~Production ES256 metadata key~~ — **RESOLVED**: real `pv-meta-prod-2026-01` provisioned, embedded trust, recovery PFX written (phase 37.6 / 37.7).
3. **Disposable VM matrix** not executed (GATE-1..8, GATE-3 reboot, GATE-4/8 uninstall) — **BLOCKED**: current host has CPU virtualization disabled in firmware and no Windows ISO (see `phase-37.8-vm-certification.md` §0). Runbook ready: `vm-certification-runbook.md`.
4. ~~Real staging HTTPS feed (GATE-11) / immutable publication (GATE-12)~~ — **RESOLVED (PASS)**: real Cloudflare R2 + `releases.pathveer.com` (phase 37.6 / `cloudflare-r2-certification.md`).
5. **IranDirect legacy build** absent → GATE-1/7 **BLOCKED** (no authentic artifact; searched repo/git/local — `phase-37.8-vm-certification.md` §4).
6. **RIPEstat commercial-terms review** open (legal/business).

---

## 13. Git closeout

- Modified (committed in a follow-up commit on this branch):
  - `PathVeer.Core/Update/ReleaseSignatureVerifier.cs` (added `FromEnvironment`)
  - `PathVeer.Tray/TrayApplicationContext.cs` (wired `BuildReleaseVerifier` to env trust root)
  - `PathVeer.Core.Tests/Update/UpdateArchitectureTests.cs` (3 regression tests)
- No secrets, no binaries, no VM images committed.
- All valid work pushed to `origin/development/service-authority`.

---

## 14. Decision

### CONDITIONALLY CERTIFIED

The implementation and all available (local, non-destructive, code-level) certification gates pass:

- Release pipeline (build → hash → ES256 sign → verify → publish immutable → latest-last → channel isolation) is **proven end-to-end**.
- Tamper rejection, immutability, and unsigned-manifest rejection are **proven in code**.
- The §14 production-trust defect is **fixed and regression-covered**.

But the following hard prerequisites remain before **public Windows release** and are explicitly listed (not hidden):

- production Authenticode certificate (BLOCKED — not procured);
- production ES256 metadata key provisioning + client trust injection (BLOCKED — not provisioned);
- disposable-VM real-execution of GATE-1..12 (NOT EXECUTED — no VM);
- real staging HTTPS feed + CDN immutable publication (NOT EXECUTED — no infra);
- RIPEstat commercial-terms review (OPEN — legal/business).

**PathVeer is NOT yet CERTIFIED FOR PUBLIC RELEASE.** It is release-engineering-complete and production-trust-architecturally-ready; it awaits credential/infrastructure provisioning and VM execution. No stable publication was performed (§54/§85 — requires explicit user approval).

Smallest remaining closure slice: provision production signing + metadata key, stand up one disposable Win11 VM, execute GATE-1..12, then re-certify.
