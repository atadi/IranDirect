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

- **Root** `CN=PathVeer Development Root CA`: `CA:TRUE`, RSA 4096, SHA-256,
  KeyUsage `CertSign`+`CRLSign`.
- **Leaf** `CN=PathVeer Development Code Signing`: signed by dev root, EKU
  `1.3.6.1.5.5.7.3.3` (codeSigning), KeyUsage `DigitalSignature`, RSA 4096,
  SHA-256.
- Private keys stay in the local certificate store; they are **never** written
  to the repo, logs, or env.

## Operator workflow

### 1. Create dev root + leaf (operator, on signing workstation)

```powershell
$rootCer = '.\PathVeerDevelopmentRootCA.cer'
.\tools\New-PathVeerDevelopmentSigningCertificate.ps1 `
    -ExportRootCerPath $rootCer `
    -ExportLeafPfxPath '.\dev-signing-backup.pfx'   # optional encrypted backup
```
This installs the root into `Cert:\CurrentUser\Root` and the leaf into
`Cert:\CurrentUser\My`. It prints both thumbprints.

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
Proves the tooling contract (production rejection, `allowUnsigned=false`,
`pv-meta-prod-2026-01` unchanged, sign-before-hash ordering, beta.1 bytes
immutable, no committed private material, parsers clean). Signature-layer
checks (1–5) run on a dev machine with the dev cert installed.
