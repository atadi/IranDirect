# PathVeer Phase 37.3 — Release Manifest / Update Architecture

Status: **CONDITIONAL PASS** (Phase 37.2 final HEAD `94c8ea1`).
Production Authenticode certificate still NOT provisioned; release-metadata signing key
separate from Authenticode (ECDsa P-256, ES256) and verifiable independently of transport.

This document is the authoritative Phase 37.3 contract. It defines the release-metadata
schema, the trust model, the client-side update-selection model, and the verified
installer handoff. It deliberately does NOT implement the pathveer.com backend, a public
CDN, account-aware policy, cloud device management, a background auto-updater, or forced
updates — those are deferred to 37.4 / later.

---

## 1. Design goals

A future PathVeer client must be able to determine, safely and offline-verifiably:

* What is the latest available version for its platform/architecture/channel?
* Is it newer than the installed version? Is it compatible?
* Where is the installer, what is its hash, and is the metadata authentic?
* Can the installer itself be verified before execution?
* Can a verified installer be handed to `PathVeerSetup.exe` (the single install
  authority) without duplicating any install logic?

Update checking is **architecture/check/discovery only**. It never installs, never
elevates, never affects routing, and fails non-fatally.

---

## 2. Release manifest schema (schemaVersion 1)

Path: `artifacts/releases/<Version>/win-x64/release-manifest.json` (produced by
`tools/New-PathVeerRelease.ps1`). Can also be served as a static file from GitHub
Releases / object storage / CDN / static site — the client does not care about the
transport (see §6).

Required fields:

| Field | Type | Notes |
|-------|------|-------|
| `schemaVersion` | int | Must be `1`. Unknown versions are rejected. |
| `product` | string | Must equal `PathVeer`. |
| `version` | string | SemVer, e.g. `1.0.0`, `1.0.0-beta.1`. |
| `channel` | string | `stable` or `beta`. |
| `platform` | string | `windows`. |
| `architecture` | string | `x64`. |
| `publishedAtUtc` | string | ISO-8601 UTC. |
| `minimumUpgradeVersion` | string | Floor below which a direct installer upgrade is unsupported. |
| `installer` | object | `{ fileName, url, sha256, size }`. |
| `packageArchive` | object | Portable ZIP metadata (not used by the updater). |
| `signed` | bool | `true` when a signature envelope is present. |
| `signature` | object | `{ algorithm, keyId, value }` (absent when unsigned). |

Optional: `releaseMode`, `components`, `releaseNotesUrl`.

`installer.url` uses `https://` for production. Local/dev test feeds may use
`http://localhost` or `file://` only under explicitly controlled conditions (the parser
rejects plain `http://` for non-localhost hosts in production validation).

`installer.sha256` is lowercase hex, exactly 64 characters; malformed hashes are
rejected at parse time. `size` is an additional sanity check (hash remains authoritative).

### Envelope (signed form)

```json
{
  "schemaVersion": 1,
  "product": "PathVeer",
  "version": "1.0.0",
  "channel": "stable",
  "platform": "windows",
  "architecture": "x64",
  "publishedAtUtc": "2026-08-09T12:00:00Z",
  "minimumUpgradeVersion": "1.0.0",
  "installer": { "fileName": "PathVeerSetup-1.0.0-win-x64.exe", "url": "https://releases.pathveer.com/windows/stable/PathVeerSetup-1.0.0-win-x64.exe", "sha256": "...", "size": 132022269 },
  "packageArchive": { "fileName": "PathVeer-1.0.0-win-x64.zip", "url": "...", "sha256": "...", "size": 2844598 },
  "signed": true,
  "releaseMode": "Release/Signed",
  "components": ["Service", "Cli", "Tray"],
  "signature": { "algorithm": "ES256", "keyId": "pv-meta-2026", "value": "<base64 ECDSA-SHA256>" }
}
```

---

## 3. Channel model

Centralized in `ReleaseChannelPolicy` (`PathVeer.Core.Update`). Two channels now:

* **stable** — eligible only for `channel == "stable"` releases. Ignores beta.
* **beta** — eligible for both `beta`/`rc` and `stable` releases (may see newer
  prereleases and stable per precedence).

Selection logic:
* `IsEligible(UpdateChannel, manifest)` decides eligibility based purely on the
  manifest's `channel` vs the client's channel.
* Version precedence uses full SemVer (prerelease-aware): `1.0.0-beta.1 < 1.0.0-rc.1 <
  1.0.0 < 1.0.1 < 2.0.0`. A newer major is recognized as newer; an older release is
  never presented as an upgrade.

Beta is explicit opt-in (default client channel is `stable`). Stable users are never
silently migrated to beta.

---

## 4. Trust / signature architecture

* **Algorithm**: ECDsa P-256 over SHA-256 (`ES256`). Chosen over Ed25519 because the
  in-box .NET BCL surface here does not expose `System.Security.Cryptography.Ed25519`;
  ECDsa P-256 is fully in-box, hermetic (no NuGet package), and PowerShell uses the
  same `System.Security.Cryptography.ECDsa` API so cross-language signatures agree.
* **Separate key from Authenticode**: the release-metadata signing key is distinct from
  the Authenticode code-signing certificate. Rationale: independent rotation, offline
  verification without Windows trust-store dependency, smaller compromise scope, and
  SaaS/CDN independence. Compromise of the Authenticode cert does not imply compromise
  of release-metadata trust, and vice-versa.
* **Canonicalization** (`JsonCanonicalizer` / PowerShell `ConvertTo-CanonicalJson`):
  parse → remove `signature` and `signed` envelope/status fields → recursively sort
  object property names lexicographically → emit compact UTF-8, no insignificant
  whitespace, arrays preserve order. The **exact same** canonical bytes are produced by
  the PowerShell signer and consumed by the C# verifier (proven by the cross-language
  parity test `ManifestSigning_PowerShellSigned_VerifiesInCSharp`).
* **Verification** (`ReleaseSignatureVerifier`): given a trusted key set
  (`Dictionary<keyId, publicKey(64 bytes: Q.X|Q.Y)>`) and `allowUnsigned` flag.
  * Signed manifest → verifies signature over canonical payload; wrong key, truncated,
    or tampered content → hard fail.
  * Unsigned manifest → `IsUnsigned` true; rejected unless `allowUnsigned` (dev/test).
* **Key rotation**: manifests carry `keyId`; the client trusts a *set* of keys. New
  client releases can add the next key before rotation; old keys can be retired without
  a permanent single-key dead end. Revocation for a compromised key is documented as
  future work (remove the key from the trusted set via a signed configuration update in
  37.4), but the architecture does not create an un-rotatable single-key dependency.
* **Production key status**: NOT provisioned yet. The pipeline fail-closes when
  `PATHVEER_META_SIGN_KEY` is absent under `Release/Signed`. Dev/unsigned mode produces
  an unsigned manifest (`signed:false`); the client trusts it only in dev `allowUnsigned`
  mode.

---

## 5. Update source / checker architecture

* **`IReleaseSource`**: minimal abstraction isolating remote transport from update
  policy. Implementations:
  * `LocalFileReleaseSource` — file-backed, for offline/deterministic tests and dev.
  * `HttpReleaseSource` — `HttpClient`, HTTPS-enforced for production URLs, bounded
    timeout, cancellation, predictable User-Agent, controlled max payload (a few KB;
    oversized manifests are rejected), no automatic HTTP→HTTPS downgrade acceptance.
  (Future: enterprise mirror, GitHub release source — no framework needed beyond this
  interface.)
* **`UpdateChecker.CheckForUpdatesAsync(channel)`** returns a typed `UpdateCheckResult`
  with state in `UpdateCheckState`:
  `NoUpdate`, `UpdateAvailable`, `CurrentVersionNewer`, `UnsupportedInstalledVersion`,
  `InvalidManifest`, `InvalidSignature`, `IncompatibleArchitecture`, `WrongProduct`,
  `WrongChannel`, `NetworkUnavailable`, `MinimumUpgradeNotMet`.
  Normal states (`NoUpdate`, `CurrentVersionNewer`) are not exceptions; only exceptional
  failures surface as exceptions or are wrapped into typed failures consistently.
* **Installed-version source** (`InstalledVersionSource`): reads
  `%ProgramFiles%\PathVeer\install-manifest.json` (`productVersion`), the authoritative
  install state written by `Install-PathVeer.ps1`. Missing/corrupt manifest →
  `UnsupportedInstalledVersion` (fail closed; never infers version from folder names).
* **Compatibility checks**: product/platform/architecture must match exactly (wrong
  values → hard fail); `minimumUpgradeVersion` floor is enforced (installed below floor
  → `MinimumUpgradeNotMet`, never blindly launches latest installer); downgrade is never
  recommended (feed older than installed → `CurrentVersionNewer`).

---

## 6. Download verification & installer handoff

* **Staging** (`UpdateStagingPaths`): `%ProgramData%\PathVeer\Updates\<version>\`,
  unique per version, machine-owned ACL, partial downloads named `<file>.partial`.
  Version directory is sanitized (path-separator stripping) so `1.0.0/../evil` cannot
  escape the updates root.
* **Partial downloads**: write to `.partial`, flush, verify, then atomic rename to
  final. Partially downloaded files are never treated as valid release artifacts.
  Resume is not implemented (clean restart acceptable for v1 artifact sizes).
* **Verification before execution** (`InstallerDownloadVerifier`), fail-closed:
  1. expected file name; 2. expected size (if provided) — sanity only; 3. SHA-256 over
     final bytes — authoritative; 4. Authenticode signature / publisher expectation seam
     (verification seam present; production trust not yet enforced because the production
     cert is outstanding). A downloaded installer is **never** executed until manifest
     authenticity is valid, artifact hash is valid, and (when configured) artifact
     signature is valid.
* **Handoff**: a verified installer is launched normally (`PathVeerSetup.exe`), which
  self-elevates and owns the entire upgrade (no second install engine). The updater
  never becomes an installation authority. The Tray may exit after launching setup; the
  Service keeps running until the deployment contract decides transition. No forced
  automatic installation; v1 flow is manual Download/Install after a user-facing
  "Update available" state.

---

## 7. Privacy & isolation

* Update checks send minimal dimensions: `channel`, `platform`, `architecture`,
  `current version`. No machine name, user identity, routes, VPN details, country
  config, or device identifiers.
* Network failure is non-fatal and isolated: routing continues normally, no Service
  crash, no Enabled-state change, no route reconciliation impact. An unreachable feed
  yields `NetworkUnavailable` (product stays "Unable to check for updates"), not
  "PathVeer failed".

---

## 8. Tray seam (manual only)

`PathVeer.Tray` adds a **"Check for app updates…"** menu item, distinct from the
existing "Update prefixes" (Iran-prefix dataset) action. It is manual, non-fatal, and
isolated from routing. Until Phase 37.4 publishes a live feed, the source resolves from
`PATHVEER_UPDATE_MANIFEST_PATH` (dev/test); when unset, the Tray reports that automatic
update checking is not yet configured. No background scheduling, no network traffic
introduced by default.

---

## 9. Release build ordering (corrected from 37.1)

Final release integrity refers to the final bytes actually distributed:

```
build components
  -> build setup (self-contained)
    -> sign executables (Authenticode; fail-closed if cert unavailable)
      -> verify binary signatures
        -> produce zip / final package
          -> hash final signed artifacts (checksums.txt, manifest hashes)
            -> generate release-manifest.json (schemaVersion 1, channel, urls, sizes)
              -> sign manifest (ES256, separate key; self-verify; fail-closed)
                -> (client) verify manifest -> verify installer hash/sig -> launch Setup
```

The installer hash is computed **after** binary signing, so the published hash refers to
the signed bytes (this corrected a 37.1 bug where hashing could precede signing).

---

## 10. Threat model (mitigations)

| Threat | Mitigation |
|--------|------------|
| Malicious manifest | Schema validation + ES256 signature over canonical payload; wrong/truncated key → hard fail. |
| Tampered CDN artifact | SHA-256 of final bytes authoritative; mismatch → reject before execution. |
| MITM | HTTPS-enforced for production URLs; signature verified independently of transport. |
| Compromised metadata key | Key set (not single key); rotate by adding next key; remove compromised key from trust set. |
| Compromised Authenticode cert | Separate metadata key; cert compromise does not invalidate release metadata trust. |
| Rollback attack | Version comparison is prerelease-aware and monotonic; downgrade never recommended; feed older than installed → `CurrentVersionNewer`. |
| Wrong architecture artifact | `architecture` checked exactly; mismatch → hard fail. |
| Unsafe URL | Parser rejects non-https (non-localhost) installer URLs. |
| Temp directory attack | Staging under `%ProgramData%\PathVeer\Updates\<version>\`, sanitized version dir, `.partial` never executed. |
| Path substitution | Version dir sanitized; `.partial` renamed to final only after full verification. |
| Argument injection | Installer is launched with no untrusted arguments from feed metadata; only the established setup contract is used. |

Residual risks: production Authenticode + metadata signing keys not yet provisioned
(verification seams present, fail-closed); revocation service deferred to 37.4; no
real remote-feed or real-installer-handoff executed on a VM yet (gates 9–10 below).

---

## 11. Tests

`PathVeer.Core.Tests/Update/UpdateArchitectureTests.cs` (35 tests) covers: valid parse,
missing-required-field, malformed version, wrong product/platform/architecture, unknown
schema version, unsafe http prod URL, local test URL accepted, signature valid, one-byte
tamper invalid, wrong key invalid, truncated signature invalid, unsigned rejected in
prod / allowed in dev, SemanticVersion prerelease ordering, stable-ignores-beta,
beta-accepts-stable-and-beta, update available, same version no-update, installed newer
than feed, invalid signature, minimum-upgrade floor blocks, hash valid, hash mismatch,
size mismatch, partial never final, version-dir sanitization, and **cross-language
PowerShell-signed → C#-verified parity**.

---

## 12. Deferred work

* Phase 37.4 — pathveer.com distribution / public release surface (live endpoint,
  CDN, latest-release API/static feed, stable/beta publication, website download UX,
  production manifest hosting).
* Production Authenticode certificate + production release-metadata signing key.
* VM release certification: real update check → verified download → Setup handoff
  (gate 9); tampered installer rejected before execution (gate 10).
* Automatic updater scheduling / background download (explicitly out of scope for 37.3).
