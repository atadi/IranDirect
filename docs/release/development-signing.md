# PathVeer — Development (Self-Signed) Authenticode Signing

This document describes the **optional, local-only** development code-signing
mode. It is for private-beta / dev-machine verification of PathVeer binaries
using a self-signed certificate. **It is NOT a production identity** and must
never reach a production channel or machine.

Production PathVeer continues to require a real public-CA Authenticode
certificate; `CodeSignatureVerifier.ForProduction()` and
`ReleaseSignatureVerifier.ForProduction()` (ES256, `allowUnsigned=false`) are
unchanged. The development root is never added to the production trust set.

## Trust model

| Mode | Trust | Timestamp | Publication |
|------|-------|-----------|-------------|
| Development / PrivateBeta | self-signed dev root (operator-installed, local) | none (self-signed chain) | private/dev only, never production |
| Production | public CA + publisher policy | RFC3161 SHA-256 | real public-CA cert required |

`Publish-PathVeerRelease.ps1` **hard-fails** if `PATHVEER_DEV_CODESIGN_THUMBPRINT`
is set while a `production` environment publish is requested.

## Certificate design

The profiles below are the **Microsoft-compatible subset proven by real SignTool
evidence** (see "Why the root must carry NO EKU" below). A dev root that injects a
default Client/Server Authentication EKU (what `New-SelfSignedCertificate` emits
without `-Type Custom`) makes `signtool verify /pa` fail with *"The signing
certificate is not valid for the requested usage."*

- **Root** `CN=PathVeer Development Root CA`: `CA:TRUE`, `PathLength:0` (canonical
  DER `30 06 01 01 FF 02 01 00`), KeyUsage `CertSign`+`CRLSign`, **NO Extended Key
  Usage**, RSA 4096, SHA-256. Built with `-Type Custom` so no default EKU is added.
- **Leaf** `CN=PathVeer Development Code Signing`: End Entity (Basic Constraints
  `30 00`), EKU `1.3.6.1.5.5.7.3.3` (codeSigning), KeyUsage `DigitalSignature`, RSA
  4096, SHA-256, signed by the dev root.
- Private keys stay in the local certificate store; they are **never** written
  to the repo, logs, or env.

### Why the root must carry NO EKU (proven)

A disposable pair was generated both ways and run through `signtool` on Windows:

| Root profile | `signtool sign` | `signtool verify /pa` | `Get-AuthenticodeSignature` |
|--------------|-----------------|------------------------|------------------------------|
| default EKU (Client+Server Auth) | exit 0 | **exit 1** | UnknownError |
| `-Type Custom`, EKU NONE | exit 0 | **exit 0** | Valid |

`.NET X509Chain.Build` returned `true` for *both* — so .NET chain validity is
**insufficient**. Windows Authenticode **application-policy** validation (what
`signtool verify /pa` enforces) is authoritative, and it requires the code-signing
EKU to be reachable through a chain whose root does not constrain EKU to
Client/Server Auth. Hence the root carries no EKU at all.

## Operator workflow

### 1. Create dev root + leaf (operator, on signing workstation)

```powershell
$rootCer = '.\PathVeerDevelopmentRootCA.cer'
.\tools\New-PathVeerDevelopmentSigningCertificate.ps1 `
    -ExportRootCerPath $rootCer `
    -ExportLeafPfxPath '.\dev-signing-backup.pfx'   # optional encrypted backup
```
This creates the root in `Cert:\CurrentUser\My` (private key local) and exports the
**public** root `.cer`; the leaf is created in `Cert:\CurrentUser\My`. It prints both
thumbprints. **The private-key root is not moved into the trusted store by this
script** — trust install is a separate step (see below). If a matching dev cert
already exists in the store, the generator fails safely unless you pass `-Rotate`.

### 2. Export public root

The `.cer` from step 1 is the public root — safe to copy to other dev machines.

### 3. Optionally export encrypted PFX backup

Pass `-ExportLeafPfxPath` (you are prompted for a passphrase). The PFX holds the
leaf private key; keep it out of git (`.gitignore` already excludes `*.pfx`).

### 4. Trust the dev root (explicit, per machine)

```powershell
.\tools\Install-PathVeerDevelopmentTrust.ps1 -CerPath $rootCer
# admin / shared-host variant:
.\tools\Install-PathVeerDevelopmentTrust.ps1 -CerPath $rootCer -LocalMachine
```
The script accepts **public `.cer` only** and refuses `.pfx`/private material.

### 5. Select the signing certificate

```powershell
$env:PATHVEER_DEV_CODESIGN_THUMBPRINT = '<leaf-thumbprint-from-step-1>'
```

### 6. Sign local disposable artifacts

```powershell
.\tools\New-PathVeerRelease.ps1 -Version 1.0.0-beta.1 -Mode Development/Signed `
    -Channel beta -OutputDirectory .\out-dev
# or sign an existing bundle directly:
.\tools\Sign-PathVeerArtifacts.ps1 -ReleaseRoot .\out-dev\1.0.0-beta.1\win-x64
```

### 7. Verify signature

On a machine where the dev root is trusted:

```powershell
Get-AuthenticodeSignature .\out-dev\1.0.0-beta.1\win-x64\PathVeerSetup-1.0.0-beta.1-win-x64.exe
# Status should be Valid; untrusted machines report UnknownError/NotTrusted.
```

### 8. Remove trust (when done)

```powershell
.\tools\Remove-PathVeerDevelopmentTrust.ps1 -Thumbprint '<root-thumbprint>'
```

### 9. Rotate

Repeat step 1 with a new root name/key; old root removed via step 8. The
production trust set is unaffected.

## Guards (fail-closed)

- Dev signing cannot combine with a production source
  (`AZURE_*`, `PATHVEER_SIGN_PFX`, `PATHVEER_SIGN_THUMBPRINT`).
- `Development/Signed` without `PATHVEER_DEV_CODESIGN_THUMBPRINT` → hard fail.
- Dev signing + production publish environment → hard fail.
- No dev key is merged into `BuiltInReleaseTrust` (production C# verifier).

## Validation

```powershell
.\tools\Test-PathVeerDevelopmentSigning.ps1
```
Proves the tooling contract with no certificate (production rejection,
`allowUnsigned=false`, `pv-meta-prod-2026-01` unchanged, beta.1 bytes immutable, no
committed private material, rerun/partial-state safety, parsers clean).

To exercise the full cert **profile** with a disposable pair, pass
`-CreateDisposableTestCert`. It mints a disposable root+leaf (root EKU NONE, canonical
BC `30 06 01 01 FF 02 01 00`, leaf End-Entity `30 00`), signs a real PE, and asserts
the profile (clauses 1–8: root has no EKU, CA:TRUE, PathLength:0, KU
CertSign+CRLSign; leaf CA:false, KU DigitalSignature, EKU exact code-signing OID),
`.NET chain.Build`, tamper→invalid, unrelated→rejected — **without** mutating the
system trust store (it anchors the chain in-memory via `X509Chain.ExtraStore`).

Clauses 9–11 (real `signtool verify /pa` exit 0 **and** `Get-AuthenticodeSignature.Status = Valid`)
require the dev root to be trusted in `Cert:\CurrentUser\Root`. Windows blocks headless
writes there with a UI prompt, so the disposable test SKIPs 9–11 and prints the reason;
an operator who has run `Install-PathVeerDevelopmentTrust.ps1` (interactive) gets them
live. The corrected profile's real SignTool evidence is documented in *"Why the root
must carry NO EKU"* above (NEW profile → `verify /pa` exit 0 + `Status=Valid`; the
old default-EKU profile → exit 1 + `UnknownError`). All store entries and temp files
are withdrawn in `finally`. Requires PowerShell 7+.

Operator mode (no switch, but `PATHVEER_DEV_CODESIGN_THUMBPRINT` set + `-TargetPePath`)
runs clauses 7–13 against your real installed dev cert; clause 2 is SKIPped because
your dev root is already trusted.

Trust installation (`Install-PathVeerDevelopmentTrust.ps1`) runs as a real, explicit
operator action and imports the public `.cer` headlessly (no private key moved).
