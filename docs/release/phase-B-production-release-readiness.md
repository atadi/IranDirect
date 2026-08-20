# Phase B — Production Release / Update Trust Hardening

Date: 2026-08-19
Status: ENGINEERING GAPS CLOSED — publication NOT authorized (external blockers remain).

This record documents the closure of the engineering gaps that previously
prevented building `1.0.0-rc.1` as the first production-trust release
candidate. It is complementary to `AI/CURRENT.md` (current state) and
`authenticode-readiness.md` (the Authenticode blocker detail).

## Goal

Make the production update path READY for a later operator-approved candidate
build. Do NOT build the real RC, do NOT publish, do NOT modify R2 pointers.

## Gap A — Update-apply pipeline (was architecture/test-seam only)

Before Phase B, `UpdateChecker.CheckAsync` validated the manifest and reported
availability, but no product code downloaded, verified, or executed an
installer. `InstallerDownloadVerifier.ForProduction(...)` had no caller.

Implemented:

- `PathVeer.Core/Update/UpdateDownloader.cs`
  Bounded streaming HTTP download to a `.partial` file. Enforces HTTPS (except
  localhost for tests), a configurable byte ceiling (~400 MB), and an early
  size guard. The installer is never fully buffered in memory.
- `PathVeer.Core/Update/UpdateApplyCoordinator.cs`
  Orchestrates: download → verify (size + SHA-256 + Authenticode + publisher)
  → atomic promote `.partial` → final → re-verify final (TOCTOU closure).
  Returns `UpdateApplyResult` (success + staged path, or failure). Never calls
  `Process.Start`.
- `PathVeer.Tray/TrayApplicationContext.cs`
  `CheckAppUpdatesAsync` now offers an explicit, user-initiated
  "Download and Install" flow; on success it launches Setup with
  `UseShellExecute=true` (normal UAC elevation), never on an unverified file.

Boundary preserved: Service = machine authority, Tray = user UI, Setup =
installation authority. No background auto-install; user action is explicit.

## Gap A.2 — Production publisher policy (single authoritative source)

`PathVeer.Core/Update/ProductionSigningPolicy`:

- `CurrentPublisher` is the ONLY expected production publisher identity.
- It is NOT read from the manifest, R2, a remote API, or a user env var.
- Default = `CodeSignatureVerifier.UnprovisionedPublisher`; every production
  installer check fails closed while UNPROVISIONED.
- Provisioning = change one non-secret constant once the real certificate
  exists; `CreateInstallerVerifier()` wires it into
  `InstallerDownloadVerifier.ForProduction(...)`.

## Gap B — Production ES256 DPAPI store wiring

`New-PathVeerRelease.ps1` now:

- accepts `-ProductionKeyStore` (default `%LOCALAPPDATA%\PathVeer\Secrets`);
- passes it to `Sign-ReleaseManifest.ps1` for `Release/Signed`, so signing
  reads the DPAPI-protected key blob (`metadata-signing-pv-meta-prod-2026-01.xml`)
  in-process — no `PATHVEER_META_SIGN_KEY` required, no private bytes in env or
  on disk.
- Development keys remain strictly isolated (no dev→prod or prod→dev leakage).

## Gap C — Deterministic signtool discovery

`tools/Find-PathVeerSignTool.ps1` resolves in priority order:
x64 SDK → signtool on PATH → x86 SDK → arm64 SDK. It never silently picks
arm64 due to directory enumeration order. Wired into
`Sign-PathVeerArtifacts.ps1` and `Test-PathVeerAuthenticodeReadiness.ps1`.

## PowerShell 7 prerequisite

`Publish-PathVeerRelease.ps1` and `PathVeerR2.psm1` now carry
`#requires -Version 7` (AWS.Tools.S3 / pwsh-7 syntax) so they fail early with a
clear message on Windows PowerShell 5.1.

## RC version / channel policy

No `rc` channel added. Candidate model `1.0.0-rc.1` on the existing `beta`
channel with mode `Release/Signed` and metadata key `pv-meta-prod-2026-01`.
Existing `SemanticVersion` already ranks `1.0.0-beta.1 < 1.0.0-rc.1 < 1.0.0`;
`ReleaseChannelPolicy` already lets a beta client see beta/rc/stable while
stable stays stable-only. `windows/stable/latest.json` is untouched.

## External blockers (NOT resolved by this phase)

1. No production Authenticode certificate provisioned. UNPROVISIONED publisher
   policy fails closed; a self-signed dev certificate must NOT be promoted.
2. Production publisher identity cannot be set to the real legal certificate
   Subject until the certificate is issued and the operator confirms the
   identity. Historical docs mention a possible individual legal identity but
   that must NOT be promoted into policy without the actual cert + confirmation.
3. RIPEstat commercial-use permission/source decision remains an external
   business/legal prerequisite for a commercial/public release.

## Verification

- Focused C# tests: `UpdateApplyCoordinatorTests` (10) — valid download/verify/
  promote, hash mismatch, size mismatch, Authenticode invalid, cancellation,
  UNPROVISIONED manifest-cannot-override. `ProductionSigningPolicyTests` —
  UNPROVISIONED default, fail-closed, expected publisher not from manifest.
- PowerShell: `Test-PathVeerReleaseKeyStore.ps1` (Gap B A/B/D),
  `Test-PathVeerSignToolResolution.ps1` (Gap C).
- Regression: full `PathVeer.Core.Tests` Update namespace (153) and
  `R2PublicationPolicyTests` (49) green.

## Decision

Engineering path is READY. Publication remains NOT authorized until the
production Authenticode certificate, confirmed publisher identity, and RIPEstat
commercial clearance are supplied by the operator.
