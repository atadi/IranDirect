# PathVeer — Authenticode production signing readiness

This document records the engineering state of Windows Authenticode signing for
PathVeer and the current production blocker. It is complementary to
`authenticode-provider-feasibility.md` (the external CA research).

## Production signing order (contract, fail-closed)

1. Build final PE bytes (dotnet publish self-contained single-file).
2. **Authenticode-sign** every signable artifact (SHA-256 + RFC3161 SHA-256
   timestamp). Signing changes bytes, so it happens BEFORE hashing.
3. **Verify** the signature locally (trust chain + code-signing EKU + publisher
   policy). A verification failure aborts the release.
4. **Hash** the signed bytes → SHA-256.
5. **Produce** the release manifest, recording `SHA-256(signed final installer)`.
6. **ES256-sign** the manifest with the production metadata key
   (`pv-meta-prod-2026-01`).
7. **Publish** (R2, immutable).

`New-PathVeerRelease.ps1` implements steps 1–6 in this order. The manifest keyId
defaults to `pv-meta-prod-2026-01` (overridable via `-MetadataKeyId` for
staging/beta). The regression test
`DistributionTests.ReleaseBuild_RecordsShaOfFinalOnDiskInstaller` proves the
manifest records the SHA-256 of the final on-disk installer, guarding against the
prior hash-then-sign defect.

## Artifacts signed

`Sign-PathVeerArtifacts.ps1` targets, in priority order:

* `PathVeerSetup-*.exe` (the self-contained bootstrapper)
* `PathVeer-*/Service/PathVeer.Service.exe`
* `PathVeer-*/Cli/PathVeer.Cli.exe`
* `PathVeer-*/Tray/PathVeer.Tray.exe`

Contained binaries are signed **before** packaging (the setup bootstrapper is
signed; the package contents are signed where present). ZIP archives are NOT
Authenticode-signed (ZIP is not a PE); the binaries *inside* the archive are
signed first.

## Signing-provider architecture

Small, provider-agnostic — supports modern key protection without weakening it:

1. **AzureSignTool** (cloud/HSM key vault) — recommended CI path; no key on disk.
2. **Local PFX** via `PATHVEER_SIGN_PFX` + `PATHVEER_SIGN_PASSWORD` (dev/test).
3. **Certificate-store thumbprint** (`PATHVEER_SIGN_THUMBPRINT`) for an installed,
   possibly non-exportable, code-signing cert.
4. **Unsigned/Test** — when no provider is configured and `-FailIfUnavailable` is
   not set, artifacts remain unsigned (developer mode).

No plain exportable PFX is required for production; the Azure/thumbprint backends
use non-exportable/HSM keys.

## Verification requirements (production)

`CodeSignatureVerifier` (PathVeer.Core) enforces, via `WinVerifyTrust` on Windows:

* signature present,
* certificate chain builds to a trusted root,
* certificate is valid for code signing (`id-kp-codeSigning` EKU),
* signed-file hash is valid (implied by a successful WinVerifyTrust),
* timestamp present and timestamp chain valid (revocation checked),
* publisher identity matches the configured production publisher policy.

It does NOT merely check `Status != NotSigned`. Until a production certificate is
provisioned, the publisher policy is `UNPROVISIONED` and every signed file is
reported `Invalid` (fail-closed) — no silent trust.

`InstallerDownloadVerifier` consumes the signature probe (`ForProduction(policy)`)
and fails installation if the installer is not both hash-valid AND signature-valid.

## Timestamping

RFC3161 SHA-256 timestamping to `timestamp.digicert.com` (config-driven). No
SHA-1-only flow. Production signing fails if timestamping is required but does not
succeed.

## Production publisher identity policy

Expected publisher is configurable (`CodeSignatureVerifier` + readiness command).
Until a real certificate exists it is `UNPROVISIONED`. When provisioned, set the
legal publisher name (e.g. `Alireza Tadi`) so the verifier matches the cert
subject case-insensitively. The exact Subject CN is recorded in the production
signing policy once a legitimate certificate is obtained.

## Readiness command

`tools/Test-PathVeerAuthenticodeReadiness.ps1` answers:

```
AUTHENTICODE READINESS
  Signing provider
  Certificate available
  Certificate subject
  Certificate thumbprint
  Code-signing EKU
  Validity
  Private key accessible
  Timestamp service
  signtool.exe
  Sign/verify smoke test
  Expected publisher
Production ready: YES | NO
Reason: ...
```

It prints no private-key or certificate secret material, and exits 1 (not ready)
when no production certificate is configured, so it is CI-gateable. Current output
on this workstation: `Production ready: NO — no production Authenticode certificate
configured`.

## Test certificate

An ephemeral self-signed code-signing certificate (ECDsa P-256, `id-kp-codeSigning`
EKU, subject containing "TEST ONLY — NOT PUBLICLY TRUSTED") is used in
`AuthenticodeReadinessTests` to prove the publisher-matching and signature-seam
contract. It is never presented as solving production trust.

## Current blocker

**EXTERNAL BLOCKER — NO CONFIRMED ELIGIBLE ISSUER FOUND** (see
`authenticode-provider-feasibility.md`). PathVeer ships ES256 + SHA-256 verified
downloads without misrepresenting them as Authenticode-trusted.

## Remaining public-release blockers

* Production Authenticode certificate (external, blocked by sanctions/org-only CA
  policies).
* Disposable VM certification matrix (GATE-1..10, §9–§18).
* IranDirect legacy artifact (GATE-7/§10).

Decision remains **CONDITIONALLY CERTIFIED**.
