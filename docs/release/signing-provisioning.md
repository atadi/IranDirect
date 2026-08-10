# PathVeer — Production Signing & Trust Provisioning Contract

Phase 37.6 release-closure. This document records the **exact external inputs** the
builder needs to complete production trust, and the recommended defaults. It does
NOT invent any legal name, certificate, key, or credential.

## 1. Publisher / legal identity (BLOCKER A — root dependency)

The repository already defines the **display** identity in `Directory.Build.props`:

```xml
<Authors>PathVeer</Authors>
<Company>PathVeer</Company>
<Product>PathVeer</Product>
```

This flows into every assembly (auto-imported, no csproj overrides) and is what
appears in Apps & Features / file properties. What is **NOT** established is the
**verifiable legal entity** behind the code-signing certificate — CAs require a
real legal name (registered company or individual) to issue.

**Required input (from user/business):** the exact legal subject for the cert.
Options already surfaced to the user:
1. Registered company "PathVeer" (LLC/Inc/equivalent) + jurisdiction.
2. Individual (personal legal name).
3. Another existing company/entity.
4. Formalize later (blocks real enrollment now).

**Recommended default if user defers:** keep `<Company>PathVeer</Company>` as the
display string, but treat it as UNVERIFIED until the legal subject is supplied.
Do not enroll any certificate under a guessed identity.

## 2. Production Authenticode (BLOCKER B)

**Pipeline is ready — no code change needed.** `tools/Sign-PathVeerArtifacts.ps1`
supports three backends, selected by environment:
- `AzureSignTool` (env `AZURE_KEY_VAULT_URI/CLIENT_ID/TENANT_ID/CLIENT_SECRET`) — **recommended**: managed, non-exportable, no PFX on disk.
- `Pfx` (env `PATHVEER_SIGN_PFX` + `PATHVEER_SIGN_PASSWORD`).
- `Thumbprint` (env `PATHVEER_SIGN_THUMBPRINT`, cert in store).

RFC3161 timestamp `timestamp.digicert.com` is already wired.

**Recommended provider (current authoritative options, 2026):**
- **Azure Artifact/Trusted Signing** — ~$9.99/mo; managed/non-exportable; integrates GitHub Actions/Azure DevOps; availability USA/Canada/EU/UK (orgs), USA/Canada (individuals). Reputation builds over time (initial SmartScreen warnings expected for new certs).
- **OV code-signing cert from a CA (DigiCert/Sectigo)** — $150–300/yr; worldwide; traditional.

**Required input:** (a) the §1 legal identity, (b) provider choice, (c) the cert
material in CI/secret store (no commit). Azure Trusted Signing needs an Azure
subscription + Trusted Signing account in a supported region matching the identity.

**PE targets to sign:** `PathVeerSetup.exe`, `PathVeer.Service.exe`, `PathVeer.Cli.exe`, `PathVeer.Tray.exe`.

## 3. Production ES256 metadata key (BLOCKER C)

Authenticode key is independent from the metadata key (separate rotation/compromise boundary).

Current `tools/Sign-ReleaseManifest.ps1` reads the raw 96-byte EC P-256 private key
from `PATHVEER_META_SIGN_KEY`. The production client loads trusted **public** keys
from `PATHVEER_TRUSTED_META_KEYS` (64-byte `Q.X||Q.Y`) and rejects unsigned manifests
when any key is present (§6 hard gate satisfied architecturally).

**§8 concern:** a managed/non-exportable KMS/HSM/Key Vault EC key must NOT be
exported to fit the current raw-key script. Two clean options:
- **Keep CI-secret PEM** (the 96-byte key in `PATHVEER_META_SIGN_KEY` as a CI secret, non-exportable at rest in the secret store). Simplest; works with current script unchanged.
- **Signer-provider abstraction** (request a signature from KMS/HSM without exporting the private key). Requires a small adapter in `Sign-ReleaseManifest.ps1`; depends on the user's cloud decision.

**Required input:** (a) chosen key-storage model, (b) the production public key
injected via `PATHVEER_TRUSTED_META_KEYS` in the release pipeline, (c) final `keyId`.
The public key is the only material the shipped client needs.

## 4. Disposable VM (BLOCKER D)

Hyper-V is **Enabled**, 0 VMs provisioned. `tools/certification/New-PathVeerCertificationVm.ps1`
scaffolds a Gen2 VM + checkpoint slot (guarded by `-Confirm`; does NOT download an ISO).
**Required input:** a Windows 11 x64 ISO (official evaluation image or licensed media).
Then GATE-1..12 execute per `tools/certification/README.md`.

## 5. Supported IranDirect legacy artifact (BLOCKER E)

The supported upgrade floor is the **final globalized IranDirect build** = commit
`e4847b9` (globalization test commit; floor documented in phase-36.1 §27/§28).
No built installer/package artifact exists anywhere reachable in this environment.
**Required input:** the actual final IranDirect installer binary users would upgrade from
(or an explicit decision that legacy migration is out of scope for v1).

## 6. Staging HTTPS host / DNS / object storage (BLOCKER F)

`releases.pathveer.com` does not resolve; no storage creds configured.
`Publish-PathVeerRelease.ps1` supports only a `Local` backend today (the origin a
CDN/object store syncs from). **Required input:** a static immutable origin
(Cloudflare R2 + domain, S3 + CloudFront, Azure Blob/Front Door, or equivalent)
+ DNS control for `releases.pathveer.com`. The app consumes `https://releases.pathveer.com/...`
from signed manifests, so the URL contract must be preserved.

## 7. RIPEstat commercial terms (BLOCKER H)

Authoritative RIPE NCC RIPEstat Terms (Article 3): **commercial use** — providing
paid services/products/derivatives based on RIPEstat data, or packaging RIPEstat
data with other sources as a commercial product — **requires written permission
from RIPE NCC.** Non-commercial/internal use is permitted.

**Engineering status:** no code change required; this is a business/legal disposition.
**Required input:** written RIPE NCC permission request (or explicit acceptance of
the non-commercial limitation for v1).

## 8. Summary — inputs still required from the user

| # | Input | Blocks |
|---|-------|--------|
| 1 | Legal publisher identity (§1) | Authenticode enrollment (B) |
| 2 | Signing provider choice (§2) | B |
| 3 | Metadata key-storage model + public key (§3) | C |
| 4 | Windows 11 ISO (§4) | VM GATE-1..12 |
| 5 | Final IranDirect installer (§5) | GATE-1, GATE-7 |
| 6 | Staging host + DNS + storage (§6) | GATE-11, GATE-12 |
| 7 | RIPEstat written permission / acceptance (§7) | Business release prerequisite |

Once these are supplied, the certification matrix executes against frozen RC bytes
(installer `8871c9b7…ad7670`, package `cd94f2a7…f1e65`, manifest `ccd9651d…6daa8`)
and re-certifies. No broad engineering phase is required — only execution.
