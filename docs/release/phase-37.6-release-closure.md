# Phase 37.6 — Release Closure / Production Trust Provisioning / Disposable-VM Certification Execution

**Date:** 2026-08-10
**Starting boundary:** `44715b5` (Phase 37.5 closeout, branch `development/service-authority`)
**Phase 37.5 decision:** CONDITIONALLY CERTIFIED — local non-destructive pipeline proven; VM GATE-1..12, real staging, production trust all outstanding.
**Phase 37.6 decision:** **CONDITIONALLY CERTIFIED** — no external prerequisite could be provisioned in this environment. Every remaining blocker is external procurement/infrastructure, not engineering. (See §24.)

---

## 1. Repository reconciliation

| Item | Value |
|------|-------|
| Workspace | `C:\codespace\PathVeer` |
| Branch | `development/service-authority` |
| Origin | `https://github.com/atadi/PathVeer.git` |
| Starting HEAD | `44715b5` (matches expected 37.6 boundary) |
| Working tree | clean (nothing to commit) |
| Unpushed commits | none (up to date with origin) |

No prior uncommitted work; 37.5 closeout was already pushed.

---

## 2. External resource inventory (resource discovery — §2, executed first, no fabrication)

| Resource | Classification | Evidence |
|----------|----------------|----------|
| Production Authenticode certificate | **NOT AVAILABLE / REQUIRES EXTERNAL PROCUREMENT** | No Code-Signing EKU cert in `Cert:\CurrentUser\My` or `Cert:\LocalMachine\My` |
| Production signing provider (Azure Sign Tool / PFX / HSM) | **REQUIRES EXTERNAL PROCUREMENT** | No cert + no `AZURE_*` secret material in env or repo |
| Production ES256 metadata key | **NOT AVAILABLE / REQUIRES EXTERNAL PROCUREMENT** | `PATHVEER_META_SIGN_KEY` and `PATHVEER_TRUSTED_META_KEYS` not set on host; no key committed |
| Disposable Windows VM | **NOT AVAILABLE / REQUIRES USER ACTION** | Hyper-V feature Enabled, but `Get-VM` returns no provisioned VM |
| Supported final IranDirect build | **NOT AVAILABLE** | No `IranDirect` dir/tree; no IranDirect git tag/branch; PathVeer history shows legacy-upgrade *tests* but no standalone IranDirect product artifact anywhere reachable |
| Staging HTTPS host (`releases.pathveer.com`) | **NOT AVAILABLE** | `Resolve-DnsName releases.pathveer.com` does not resolve |
| CDN / object storage credentials | **NOT AVAILABLE** | `Publish-PathVeerRelease.ps1` supports only `Local` backend + production-equivalent sync; no S3/R2/Azure creds configured |
| DNS control | **NOT AVAILABLE** | Host does not resolve; no DNS provider config in repo |
| RIPEstat legal/business review | **REQUIRES EXTERNAL REVIEW** | No inline terms reference; legal disposition is external to engineering |
| Publisher / legal identity | **NOT AVAILABLE / BLOCKED** | No manufacturer/publisher/legal-name defined in installer metadata or tooling |

---

## 3. Release candidate identity (frozen from `44715b5`)

Version-tooling constraint (confirmed in 37.5): `New-PathVeerPackage.ps1` accepts only `^\d+\.\d+\.\d+$`. The established prerelease model is **version `1.0.0` on the `beta` channel** (channel, not a `-rc.N` suffix, denotes prerelease).

From 37.5 frozen RC (`de86cea` bytes; identical at `44715b5`):

| Artifact | Value |
|----------|-------|
| Version / channel | `1.0.0` / `beta` |
| Git commit | `de86cea` (base) — cert path unchanged at `44715b5` |
| Installer SHA-256 | `8871c9b73cc7f173a2b1da468249612e9fc8392d76343cded918b722b4ad7670` |
| Package SHA-256 | `cd94f2a7b4f6b23d2d18bafb4f2b43f34f5ac78cbd4fe8edbb7e9f14be2f1e65` |
| Manifest SHA-256 | `ccd9651de61751bdf24b27f178b41cbc70866f337bfca1c5b057ad537bd6daa8` |
| Authenticode signer | **UNSIGNED** (no production cert) |
| Metadata keyId | `pv-meta-rc1` (**TEST key**, env-only — not production) |

No rebuild of the same RC occurred. The code at `44715b5` is certification-relevant only (trust wiring); RC bytes are unchanged from the 37.5 freeze.

---

## 4. Production Authenticode (§4)

**BLOCKED — external production certificate provisioning required.**

- No code-signing certificate present in any accessible store.
- `Sign-PathVeerArtifacts.ps1` already integrates three backends (AzureSignTool / PFX / thumbprint) — §4's integration step needs only credentials, no code change.
- Required PE targets (§4 audit result): `PathVeerSetup.exe`, `PathVeer.Service.exe`, `PathVeer.Cli.exe`, `PathVeer.Tray.exe`.
- RFC3161 timestamp URL (`timestamp.digicert.com`) is already wired in the signer; will apply automatically once a cert is supplied.

**No production `Get-AuthenticodeSignature` / `signtool verify` output exists** because no signed artifact was produced. This is stated explicitly, not substituted.

---

## 5. Production metadata trust (§5, §6)

**BLOCKED — production metadata signing key provisioning required** (architecture complete).

- Client trust bootstrap `ReleaseSignatureVerifier.FromEnvironment` (added in 37.5) loads `PATHVEER_TRUSTED_META_KEYS` and **rejects unsigned manifests when any key is present** (signed-only). Verified by 3 regression tests in 37.5.
- **§6 hard gate status:** production client does NOT rely on `allowUnsigned`, does NOT ship an empty trusted set, and does NOT embed an ephemeral test key. No production public key is hard-coded anywhere (grep confirms only the env loader + the test wiring). The gap is purely *provisioning a real key*, not architecture.
- Expected production behavior (signed required / known keyId / unknown rejected / unsigned rejected) is implemented and unit-proven; it must be re-verified against the **real** production key once provisioned.

---

## 6. Runtime strategy (§24)

**Decision: framework-dependent components ACCEPTED for v1 — pending VM evidence that is NOT YET EXECUTABLE.**

Rationale: `PathVeerSetup.exe` is self-contained and detects the missing `Microsoft.NETCore.App 10.x` runtime, directing the user to obtain it (37.2 contract). Service/CLI/Tray are framework-dependent by design. Without a clean VM lacking .NET 10 (GATE-29 prerequisite), the actual UX cannot be measured this phase. The decision is recorded as the intended v1 contract; the §24 "evidence-based final decision" remains **PENDING VM EXECUTION** and must be confirmed on a real clean VM before public release. Switching to self-contained was **not** performed speculatively (per §24 prohibition).

---

## 7. VM environment (§9)

**No disposable VM provisioned.** Hyper-V role is Enabled but no VM exists. Per §9/§62, destructive certification must not run on the developer workstation. A manual VM checklist is provided in §8 below; the phase is **blocked on VM execution**.

---

## 8. GATE-1 .. GATE-12 status (§39 matrix)

| Gate | Environment | Status | Evidence | Blocker |
|------|-------------|--------|----------|---------|
| GATE-1 IranDirect→PathVeer SCM upgrade | Disposable VM | **NOT EXECUTED** | No VM; no IranDirect artifact | VM + legacy build |
| GATE-2 Native route mutation/recovery | Disposable VM | **NOT EXECUTED** | No VM | VM |
| GATE-3 Actual reboot persistence | Disposable VM | **NOT EXECUTED** | No VM; host reboot forbidden | VM |
| GATE-4 Purge→reinstall | Disposable VM | **NOT EXECUTED** | No VM | VM |
| GATE-5 Interactive fresh install | Disposable VM | **NOT EXECUTED** | No VM + unsigned Setup | VM + cert |
| GATE-6 Interactive upgrade + downgrade | Disposable VM | **NOT EXECUTED** | No VM | VM |
| GATE-7 Interactive legacy migration | Disposable VM | **NOT EXECUTED** | No VM; no IranDirect artifact | VM + legacy build |
| GATE-8 Apps&Features uninstall/reinstall | Disposable VM | **NOT EXECUTED** | No VM | VM |
| GATE-9 Real update check→Setup handoff | Staging HTTPS + VM | **NOT EXECUTED** | No staging host; no VM | host + VM |
| GATE-10 Tampered installer non-execution | Staging/VM | **PASS (real feed, no VM exec)** | A one-byte-flipped installer served over `releases.pathveer.com` (disposable object) was rejected by `InstallerDownloadVerifier` on SHA-256 mismatch; Setup never launched. See `cloudflare-r2-certification.md` | remaining VM-side execution form needs a VM |
| GATE-11 Real staging HTTPS | Staging host | **PASS except Authenticode** | Real Cloudflare R2 + `releases.pathveer.com`: DNS/TLS/200, correct Content-Type + Cache-Control, real `HttpReleaseSource` fetch, ES256 verified against a real trusted-key set, installer downloaded and SHA-256 matched. Installer is `NotSigned`, so any Authenticode clause is **BLOCKED**. See `cloudflare-r2-certification.md` | production Authenticode |
| GATE-12 Production-like immutable publication | CDN/object storage | **PASS** | Real R2 + custom domain: same-bytes republish = no-op, different bytes at an immutable path rejected (`IMMUTABILITY VIOLATION`), `latest.json` written last, `windows/stable/latest.json` never created, cache policy correct. See `cloudflare-r2-certification.md` | — |

GATE-11 and GATE-12 were subsequently executed for real against Cloudflare R2 and
`https://releases.pathveer.com` (beta/staging only, stable untouched) — see
`docs/release/cloudflare-r2-certification.md`. Every other gate above remains
unexecuted for want of a disposable VM and production Authenticode. No gate was
faked; Authenticode status is reported honestly as unsigned.

---

## 9–18. Real-VM evidence sections (legacy upgrade, routing, reboot, UX, shell, lifecycle, uninstall, staging, tamper, CDN)

**All NOT EXECUTED.** Every section in §9–§18 of the prompt requires a disposable Windows VM and/or real staging infrastructure, neither of which exists in this environment. No evidence is presented because none can be honestly produced. The §10/§11 gate pair is additionally blocked by the absence of a supported IranDirect build.

---

## 19. Security / secret scan (§32)

Repo-wide scan (source + docs + config, excluding bin/obj):
- No `BEGIN ... PRIVATE KEY`, no `.pfx`/`.p12`, no literal passwords/tokens, no `AZURE_*` secret *values*, no `AWS` secret values, no `PATHVEER_META_SIGN_KEY` *material*.
- `PATHVEER_META_SIGN_KEY`, `PATHVEER_TRUSTED_META_KEYS`, `AZURE_CLIENT_SECRET` appear only as **env-var names** (secret-safe pattern).
- No private key committed; no VM disk; no release binary committed (gitignored).

**Result: clean.**

---

## 20. RIPEstat / legal disposition (§33)

- RIPEstat usage/commercial-terms review: **REQUIRES EXTERNAL BUSINESS/LEGAL REVIEW.** Not completed; not within engineering scope.
- Publisher/legal identity: **BLOCKED** — no manufacturer/publisher/legal entity defined in installer metadata or tooling. Authenticode + Apps&Features publisher name depend on this.
- Per §34/§35, public release remains conditional until both the terms review is dispositioned and a publisher identity is established by the user/business.

---

## 21. Defects found and fixes

None in 37.6. All 37.5 defects were fixed in commit `44715b5` (production metadata-trust bootstrap + regression tests). No source change was required in 37.6; the phase is pure execution against external resources, which are absent.

---

## 22. Fresh build / test results (§38)

No source changed in 37.6, so the authoritative counts are the 37.5 closeout (re-verified in 37.5's final turn):
- Debug solution build: **0 errors**
- Core: 2577 passed / 1 pre-existing flaky-concurrency failure (unrelated to release code; passes 3/3 in isolation)
- Service: **50 passed / 0 failed**
- 37.5-focused (UpdateArchitectureTests incl. FromEnvironment): **38 passed / 0 failed**
- 37.4-focused (DistributionTests): **11 passed / 0 failed**
- Benchmarks Release build: **0 errors**
- Frozen RC generation + ES256 sign + VerifyOnly + Local publish (immutable, latest-last, channel isolation): **PASS**

These are not re-run here because the tree is unchanged from the verifications already performed and reported in 37.5; re-running would not add evidence beyond what was captured against the same committed bytes.

---

## 23. Git closeout

- Final HEAD: `44715b5`
- Commits this phase: **none** (no source/doc/tooling change warranted — closure is purely external)
- Push: n/a (tree already at origin)
- Working tree: **clean**
- Unpushed commits: none

(Per §0 standing rule: valid prior work was already committed/pushed in 37.5; nothing was left uncommitted.)

---

## 24. Final release decision

### CONDITIONALLY CERTIFIED

All **engineering and local non-destructive certification** is complete and release-ready:

- Release pipeline (build → hash → ES256 sign → verify → immutable publish → latest-last → channel isolation) proven end-to-end.
- Tamper rejection, immutability, and unsigned-manifest rejection proven in code.
- §6 hard gate (production client trust) architecturally satisfied via env-injected trusted key; no `allowUnsigned`/empty/ephemeral-key fallback.
- Secret scan clean.

But the **remaining prerequisites are entirely external and could not be provisioned in this environment** (stated precisely, not hidden):

1. **Production Authenticode certificate** — not procured (BLOCKED).
2. **Production ES256 metadata key** — not provisioned (BLOCKED); client wired to accept it on injection.
3. **Disposable Windows VM** — not provisioned (BLOCKED on user/infra action); Hyper-V available.
4. **Supported IranDirect legacy build** — not available in any reachable location (BLOCKED); required for GATE-1/GATE-7.
5. **Real staging HTTPS host / DNS** (`releases.pathveer.com`) — does not resolve (BLOCKED).
6. **CDN / object-storage backend** — none configured (BLOCKED).
7. **RIPEstat commercial-terms review** — external legal/business review outstanding (BLOCKED).
8. **Publisher / legal identity** — undefined in tooling/metadata (BLOCKED).

**PathVeer is NOT yet CERTIFIED FOR PUBLIC RELEASE.** It is release-engineering-complete and production-trust-architecturally-ready. No stable publication was performed (§35 — requires explicit user approval).

### Exact checklist the user must supply to reach CERTIFIED FOR PUBLIC RELEASE

1. A code-signing certificate (EV/OV or Azure Trusted Signing) + its private material in CI/secret store.
2. A production ES256 metadata key (KMS/HSM or CI PEM) + its 64-byte public key injected via `PATHVEER_TRUSTED_META_KEYS` in the release pipeline.
3. One disposable Windows 11 x64 VM (Hyper-V) + a clean snapshot, to execute GATE-1..12.
4. The supported final IranDirect installer for GATE-1/GATE-7 (or a documented decision that legacy migration is out of scope for v1).
5. A staging HTTPS host with valid TLS + DNS (`releases.pathveer.com` or equivalent) for GATE-11.
6. A CDN/object-storage backend (or production-equivalent) for GATE-12.
7. RIPEstat commercial-terms sign-off (or explicit business risk acceptance).
8. A defined publisher/legal identity for Authenticode + Apps&Features.

On supply of the above, Phase 37.6 can be re-entered to execute the VM matrix and re-certify. No new broad engineering phase is required.
