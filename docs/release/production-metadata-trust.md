# PathVeer production release-metadata trust (ES256)

This document describes the production ES256 release-metadata trust root, separate
from Windows Authenticode. It closes the "production ES256 metadata key" release
blocker.

## Key facts

| Field | Value |
|---|---|
| Key ID | `pv-meta-prod-2026-01` |
| Algorithm | ECDSA, NIST P-256 (secp256r1), SHA-256 → ES256 |
| Public key (base64, 64-byte Q.X\||Q.Y) | `fN95fm+Do+CGr8RLvC+XBJIhtATr4D63gbpIJhL0w+c/9YKJ6DwcsWnU65Gfmu8OLFWg3VyMn2Mp1N6ZVpmWJg==` |
| Public-key fingerprint (SHA-256 of the 64-byte blob) | `59704d9d42eb43884cc8b6c996eae65053f953564262e972f50644e132142ba9` |
| Created (UTC) | 2026-08-10T23:06:42Z |
| Private key | **never committed, never printed, never in env** |

The public key is committed (safe) at
`PathVeer.Core/Update/BuiltInReleaseTrust/pv-meta-prod-2026-01.json` and embedded
into the assembly as a resource so the production client ships with its trust
anchor. The matching private key is a machine-local DPAPI-protected secret on the
release workstation only.

## Private-key storage architecture

```
%LOCALAPPDATA%\PathVeer\Secrets\
    metadata-signing-pv-meta-prod-2026-01.xml        (DPAPI CurrentUser <SS/> blob: 96-byte X|Y|D)
    metadata-signing-pv-meta-prod-2026-01.meta.json  (public metadata, safe)
```

* Generated with platform cryptography (`System.Security.Cryptography.ECDsa`,
  `nistP256`) — no manual curve maths, no online generator.
* Stored as a **bare DPAPI-protected `SecureString`** (a top-level `Export-Clixml`
  of a `SecureString` writes a genuinely encrypted `<SS />` node; the same
  `SecureString` as a *property* of a `PSCustomObject` would be serialized in
  plaintext, which the provisioning tool explicitly refuses).
* ACL restricted to the current user; inheritance broken.
* Not in git, not in env, not in logs, not in the manifest.

Provisioning tool: `tools/New-PathVeerMetadataKey.ps1`. It refuses to keep a key
file that lacks the DPAPI `<SS />` node or that contains the private blob in
clear text, and refuses to overwrite an existing key (rotation uses a new KeyId).

## Public trust anchor bootstrap

The production client no longer relies on an operator-configured environment
variable to trust updates:

* `ReleaseSignatureVerifier.ForProduction()` builds a verifier from the **built-in**
  public key set only (`BuiltInReleaseTrust`), with `allowUnsigned = false`. No
  consumer configuration is required for a production binary to verify its own
  updates securely.
* `BuiltInReleaseTrust` reads the embedded JSON resource (single source of truth)
  with a compile-time fallback to the same public key, so the trust anchor cannot
  silently vanish.
* `PATHVEER_TRUSTED_META_KEYS` remains a **dev/test/staging** mechanism
  (`FromEnvironment`). It is deliberately NOT merged into `ForProduction()`: a
  production binary must never let an environment override (e.g. a staging key)
  weaken or dilute its production trust root.

Signing uses `Sign-ReleaseManifest.ps1` with `-ProductionKeyStore
%LOCALAPPDATA%\PathVeer\Secrets`; `PATHVEER_META_SIGN_KEY` is not required and is
not the recommended production path.

## Staging vs production separation

`pv-meta-staging-2026` (used for the R2 beta certification) is **not** in the
built-in production trust set. A production client therefore rejects
staging-signed metadata. Staging trust is available only through the explicit
`PATHVEER_TRUSTED_META_KEYS` dev/test override. A regression test
(`ProductionTrustTests.ForProduction_RejectsStagingSignedManifest` and
`AdditionalTrustedKey_ThroughFromEnvironment_DoesNotWeakenProductionTrust`) locks
this in.

## Sign → verify proof

With the real DPAPI-protected production key (loaded from the secret store, never
printed):

```
sign controlled manifest  -> manifest carries signature{algorithm:ES256, keyId:pv-meta-prod-2026-01}
verify with ForProduction  -> VALID, allowUnsigned=false
tamper payload            -> rejected
```

No beta/stable artifacts were re-signed; the existing immutable R2 objects are
untouched.

## Rotation procedure

Additive overlap, no online delegation:

1. Generate a new key, e.g. `pv-meta-prod-2027-01`, with
   `New-PathVeerMetadataKey.ps1 -KeyId pv-meta-prod-2027-01`.
2. Commit its public JSON and add it to `BuiltInReleaseTrust.All()`.
3. Sign subsequent manifests with `pv-meta-prod-2027-01`.
4. In a later release, drop `pv-meta-prod-2026-01` from `BuiltInReleaseTrust`.

An already-installed client that only trusts the old key can still verify the new
key's signatures during the overlap window because both are embedded.

## Revocation limitation (honest)

An embedded offline trust root cannot be instantly revoked on already-installed
clients unless another already-trusted update path can deliver a replacement.
Therefore:

* never operate with a single unrecoverable private key;
* maintain rotation overlap (old + new both embedded) during transitions;
* the private key has a protected backup copy (see below).

No live revocation infrastructure is implemented in this slice; it is not needed
for launch.

## Recovery status

The production private key is held in two protected places:

1. DPAPI CurrentUser store at `%LOCALAPPDATA%\PathVeer\Secrets\`
   — bound to this user **and** this machine; lost if either is lost.
2. A portable PKCS#12/PFX backup at
   `%LOCALAPPDATA%\PathVeer\Secrets\metadata-signing-pv-meta-prod-2026-01.pfx`,
   encrypted with a recovery passphrase held in the operator's KeePassXC vault.

The PFX is a STANDARD PKCS#12 (self-signed cert over the P-256 public key,
exported via `X509Certificate2.Export(Pfx, SecureString)` — platform crypto, no
home-grown encryption). Its round-trip re-import reproduces the production public
key fingerprint `59704d9d42eb43884cc8b6c996eae65053f953564262e972f50644e132142ba9`.
The file is ACL-restricted to the current user. **Recovery is COMPLETE.**

Generation tool: `tools/New-PathVeerMetadataKey.ps1`. Backup tool:
`tools/New-PathVeerMetadataKeyBackup.ps1` (reads the DPAPI store in-process, never
prints the key or the passphrase, ACL-restricts the output, and fails if the
round-trip fingerprint does not match).

## Remaining release blockers (unchanged by this slice)

* Production Windows **Authenticode** — the installer may still be unsigned.
* Disposable **VM certification** matrix (GATE-1..10, GATE-13..).
* **IranDirect** legacy artifact for GATE-7/§10.

Decision remains **CONDITIONALLY CERTIFIED** until those close.
