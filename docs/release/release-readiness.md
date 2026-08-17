# PathVeer — Release / Signing Readiness

**Candidate:** PathVeer 1.0.0-beta.1
**Canonical branch:** development/service-authority
**Canonical HEAD:** 6692b08
**Certification state:** CLOSE WITH EXTERNAL BLOCKERS (integrated & validated)
**Document date of audit:** current (post-merge canonical validation)

This is the operator-facing go/no-go rollup. Technical detail lives in
`authenticode-readiness.md`, `production-metadata-trust.md`,
`cloudflare-r2-certification.md`, `phase-37.4-release-architecture.md`,
`certification-status.md`. This file does NOT authorize publication.

---

## 1. Release tooling inventory

| Tool | Responsibility |
|------|----------------|
| `tools/New-PathVeerRelease.ps1` | Build frozen release bundle (PE + manifest), hash-then-sign order |
| `tools/Sign-PathVeerArtifacts.ps1` | Authenticode-sign PE artifacts (Service/Cli/Tray/Setup) |
| `tools/Sign-ReleaseManifest.ps1` | ES256-sign release manifest; verify-only mode |
| `tools/Publish-PathVeerRelease.ps1` | **Authoritative publish entry point** (validates → immutable upload → latest.json LAST) |
| `tools/New-PathVeerMetadataKey.ps1` | Generate ES256 prod metadata key (DPAPI-protected private) |
| `tools/New-PathVeerMetadataKeyBackup.ps1` | PFX backup of metadata private key |
| `tools/Test-PathVeerAuthenticodeReadiness.ps1` | Authenticode readiness probe (CI-gateable) |
| `PathVeer.Core/Update/BuiltInReleaseTrust/pv-meta-prod-2026-01.json` | Committed production public trust anchor |
| `PathVeer.Core/Update/ReleaseSignatureVerifier.cs` | `ForProduction()` → `allowUnsigned=false` |
| `PathVeerR2.psm1` | Cloudflare R2 (S3-compatible) backend |

## 2. Channel model

- Channels: `beta`, `stable` (mutually isolated object prefixes `windows/beta/`,
  `windows/stable/`). Version identity is **global per versioned path**
  (`windows/<version>/win-x64/...`); a channel only carries a `latest.json`
  pointer. One channel CANNOT overwrite the other (stable pointer is snapshotted
  and proven unchanged during any beta publish).

## 3. R2 configuration

- Bucket: `pathveer-releases`; public base: `https://releases.pathveer.com`.
- Secrets required (NOT committed; documented, not requested): R2 credential
  file (`R2CredentialPath`), R2 config (`R2ConfigPath`). Metadata signing uses
  local DPAPI store (`%LOCALAPPDATA%\PathVeer\Secrets`), never env/git.
- Public identifiers (not secrets): keyId `pv-meta-prod-2026-01`, its
  fingerprint `59704d9d…142ba9`, staging keyId `pv-meta-staging-2026`.

## 4. Immutability contract

- Same version + same bytes → idempotent (no-op / "unchanged").
- Same version + **different bytes** → HARD FAIL (Local + R2 backends both
  reject; "Refusing to overwrite a published release").
- No silent overwrite; latest.json is the verbatim signed manifest bytes,
  written LAST after all immutable objects verify.

## 5. latest.json ordering

versioned artifacts → manifest/hash verify → signed metadata → verify →
channel pointer LAST. Failure before latest leaves consumers on the previous
known-good pointer (rollback = repoint, no destructive action).

## 6. Authenticode (exact blocker)

- Targets: `PathVeerSetup-*.exe`, `PathVeer.Service.exe`, `PathVeer.Cli.exe`,
  `PathVeer.Tray.exe` (PE only; ZIP contains already-signed binaries).
- Workflow: build → Authenticode-sign (SHA-256 + RFC3161 timestamp) → verify →
  hash → manifest → ES256-sign → publish. Signing is BEFORE hashing.
- Expected publisher: `Alireza Tadi`. Verifier (`CodeSignatureVerifier`) is
  fail-closed: `UNPROVISIONED` → every file `Invalid`, no silent trust.
- **Blocker: NO production Authenticode certificate available** (external;
  sanctions/org-only CA constraints per `authenticode-provider-feasibility.md`).

## 7. Metadata signing state (RESOLVED)

- Algorithm ES256 (ECDSA P-256, SHA-256), keyId `pv-meta-prod-2026-01`,
  fingerprint `59704d9d…142ba9`.
- **Public key committed** (`BuiltInReleaseTrust/pv-meta-prod-2026-01.json`,
  embedded as assembly resource) → production client ships with trust anchor.
- **Private signing key EXISTS**: DPAPI CurrentUser store + PFX backup
  (recovery COMPLETE per `production-metadata-trust.md`).
- `allowUnsigned = false` in `ForProduction()`; `PATHVEER_TRUSTED_META_KEYS`
  is dev/staging-only and NOT merged into production trust.
- Staging key `pv-meta-staging-2026` is NOT in the production trust set → a
  production client rejects staging-signed metadata (fail-closed; regression
  test locks this).
- **Conclusion:** production ES256 metadata trust is provisioned. The remaining
  GATE-9 item is Authenticode, not metadata key provisioning.

## 8. beta.1 remote state

- beta.1 is **already published to the staging R2 bucket** (evidence:
  `publish-audit-staging-r2.json`: environment=staging, backend=R2, signed=true,
  commit 3dcc509). The hosted installer SHA-256
  (`03adadd3…045ecc7`) matches the certified local bytes → byte-identical /
  immutable.
- It was signed with the **staging** metadata key (Phase 37.6), NOT the
  production key. A production-channel publish requires re-running the publish
  ceremony with `-TrustedKeyBase64 pv-meta-prod-2026-01` (and Authenticode).
- The local `artifacts/releases/1.0.0-beta.1/.../release-manifest.json` shows
  `signed=false` (the unsigned local generation artifact); the R2-hosted copy
  was the signed one.

## 9. beta.1 immutability

UNCHANGED / IMMUTABLE. Certified bytes match the staging-R2 bytes. No package
rebuild occurred for certification fixes (only installer/JEA/orchestrator
source changed). **beta.2 is NOT required now** — there is no product change
since beta.1; GATE-6 needs a genuine second version only when a real next RC
exists.

## 10. RIPEstat

Technical: runtime data dependency (country prefix source via
`stat.ripe.net`, ISO 3166-1 alpha-2). Runtime, cached, with fallback; NOT a
build/release/build-time blocker. App operates without it (prefix updates
degrade, routing core unaffected). **Business/legal terms review REQUIRED**
(external) — does not block software build/release mechanics, only production
service use of the commercial data feed.

## 11. Release failure-mode audit (static/unit)

All fail-closed by design and locked by tests (52/0 passing):
missing/inválid Authenticode → rejected; missing timestamp → fail; wrong
metadata key → rejected; tampered payload → rejected; same-version/different
bytes → hard fail; partial upload → post-upload hash verify fails; latest
upload failure → pointer never written; wrong channel (beta→stable) → stable
pointer snapshot proves untouched; unsigned under production trust → rejected.

## 12. Secret hygiene

No private keys, PFX, credentials, or `<SS>` blobs tracked in git. Only the
safe public trust anchor is committed. Release secrets live in local DPAPI /
operator KeePassXC vault only.

## 13. Stable authorization guard

`Publish-PathVeerRelease.ps1` requires `-Environment production -ConfirmProduction`
(explicit operator intent); production also refuses localhost URLs and requires
a signed manifest. Build success, certification success, beta publish, and
merge-to-canonical are NOT interpreted as stable authorization.

## 14. Current blocker state

| Blocker | State |
|---------|-------|
| GATE-1 historical IranDirect installer | EXTERNAL — artifact unavailable |
| GATE-7 historical IranDirect installer | EXTERNAL — artifact unavailable |
| GATE-6 genuine second version | RELEASE PRECONDITION — none needed now |
| GATE-9 Authenticode cert | EXTERNAL — no eligible issuer found |
| GATE-9 prod metadata key | RESOLVED (public committed + private exists) |
| RIPEstat commercial terms | BUSINESS/LEGAL — external review |

## 15. What can be done now (operator/automation)

- Run Authenticode readiness probe + acquire a production code-signing cert.
- Perform production publish ceremony for beta.1 (re-sign manifest with
  `pv-meta-prod-2026-01`, Authenticode-sign PEs, publish to `beta` channel).
- Prepare GATE-1/6/7 procedures for when artifacts/versions exist.

## 16. What requires external prerequisite

- Production Authenticode certificate (external CA).
- RIPEstat commercial-terms clearance.
- Genuine beta.2 (only when a real next RC exists) for GATE-6.
- Authentic IranDirect installer for GATE-1/7.

## 17. Publication authorization

**NOT AUTHORIZED.** This document is a readiness rollup only. Stable and beta
publication each require an explicit, separate operator authorization and a
real publish ceremony. No tag, no GitHub Release, no latest.json mutation, no
R2 overwrite occurs under this audit.

---

**Release-readiness verdict: BLOCKED ON SIGNING MATERIAL** (production
Authenticode certificate). All release tooling, immutability, channel
isolation, metadata trust, and production-trust fail-closed invariants are
validated and locally ready.
