<#
.SYNOPSIS
    Phase 37.9 — Create a local self-signed DEVELOPMENT Authenticode certificate.

.DESCRIPTION
    Creates a two-tier self-signed development certificate chain for PRIVATE
    development / private-beta Authenticode signing only:

      * Root  CN=PathVeer Development Root CA        (CA, RSA 4096, SHA-256)
      * Leaf  CN=PathVeer Development Code Signing   (EKU codeSigning, RSA 4096)

    The PRIVATE KEYS NEVER LEAVE THE LOCAL CERTIFICATE STORE and are NEVER
    written to the repository, logs, or env. The script only ever exports:
      - the root public certificate (.cer) for trust installation, and
      - an OPTIONAL encrypted PFX backup of the leaf (operator passphrase).

    This is explicitly NOT a production identity. Production continues to require
    a real public-CA Authenticode certificate. Never install the dev root into a
    production trust path, and never combine dev signing with production
    publication (Publish-PathVeerRelease.ps1 hard-fails that combination).

.PARAMETER RootName
    Subject CN for the development root CA. Defaults to the standard name.

.PARAMETER LeafName
    Subject CN for the development code-signing leaf. Defaults to the standard name.

.PARAMETER ExportRootCerPath
    Where to write the root PUBLIC certificate (.cer). Required for trust install.

.PARAMETER ExportLeafPfxPath
    Optional. If set, write an encrypted PFX backup of the leaf private key.
    The passphrase is read interactively and never printed/logged.

.PARAMETER ValidityYears
    Validity span for both certificates. Defaults to 5 years.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$RootName = 'PathVeer Development Root CA',

    [Parameter(Mandatory = $false)]
    [string]$LeafName = 'PathVeer Development Code Signing',

    [Parameter(Mandatory = $true)]
    [string]$ExportRootCerPath,

    [Parameter(Mandatory = $false)]
    [string]$ExportLeafPfxPath = '',

    [Parameter(Mandatory = $false)]
    [int]$ValidityYears = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$m) { Write-Host $m -ForegroundColor Cyan }

if ($PSVersionTable.PSVersion.Major -lt 5) {
    throw "New-SelfSignedCertificate requires Windows PowerShell 5.1+ / PowerShell 7 on Windows."
}
if (-not $IsWindows) {
    throw "Development certificate creation requires Windows (Cert:/ drive + Windows crypto APIs)."
}

$notBefore = Get-Date
$notAfter  = $notBefore.AddYears($ValidityYears)

# --- Root CA ---------------------------------------------------------------
Write-Step "Creating development root CA: $RootName"
$root = New-SelfSignedCertificate -CertStoreLocation 'Cert:\CurrentUser\My' `
    -Subject "CN=$RootName" `
    -KeyAlgorithm RSA -KeyLength 4096 -HashAlgorithm SHA256 `
    -KeyUsage CertSign, CRLSign -KeyUsageProperty Sign `
    -BasicConstraints 'Critical, CA:TRUE, PathLength:0' `
    -NotBefore $notBefore -NotAfter $notAfter `
    -TextExtension @('2.5.29.19={text}CA:TRUE, PathLength:0')

# Move the root into the user Trusted Root store so it can issue a trusted chain
# for local verification. This is a LOCAL development-machine trust only.
Write-Step "Installing dev root into Cert:\CurrentUser\Root (local trust only)..."
Move-Item -Path "Cert:\CurrentUser\My\$($root.Thumbprint)" `
          -Destination 'Cert:\CurrentUser\Root' -Force

# --- Leaf (code signing) ---------------------------------------------------
Write-Step "Creating development code-signing leaf: $LeafName"
$leaf = New-SelfSignedCertificate -CertStoreLocation 'Cert:\CurrentUser\My' `
    -Subject "CN=$LeafName" `
    -KeyAlgorithm RSA -KeyLength 4096 -HashAlgorithm SHA256 `
    -KeyUsage DigitalSignature -KeyUsageProperty Sign `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3') `  # id-kp-codeSigning
    -Signer $root `
    -NotBefore $notBefore -NotAfter $notAfter

# --- Exports (public only; private key stays in store) ----------------------
Write-Step "Exporting root public certificate -> $ExportRootCerPath"
$rootAfter = Get-Item -Path "Cert:\CurrentUser\Root\$($root.Thumbprint)" -ErrorAction Stop
Export-Certificate -Cert $rootAfter -FilePath $ExportRootCerPath -Type CERT | Out-Null

if ($ExportLeafPfxPath) {
    $pass = Read-Host -Prompt "Enter an encryption passphrase for the leaf PFX backup" -AsSecureString
    Write-Step "Exporting encrypted leaf PFX backup -> $ExportLeafPfxPath (private key stays out of repo)"
    Export-PfxCertificate -Cert $leaf -FilePath $ExportLeafPfxPath -Password $pass | Out-Null
    # The PFX contains a private key: warn loudly and rely on .gitignore to keep it out of git.
    Write-Host "  WARNING: $ExportLeafPfxPath contains a PRIVATE KEY. Never commit it." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "SUCCESS: development signing certificate created (LOCAL ONLY)." -ForegroundColor Green
Write-Host "  Root thumbprint : $($root.Thumbprint)"
Write-Host "  Leaf thumbprint : $($leaf.Thumbprint)"
Write-Host "  Root .cer       : $ExportRootCerPath"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  Set signing env:  `$env:PATHVEER_DEV_CODESIGN_THUMBPRINT = '$($leaf.Thumbprint)'"
Write-Host "  Trust root:      .\tools\Install-PathVeerDevelopmentTrust.ps1 -CerPath '$ExportRootCerPath'"
Write-Host "  Sign dev bundle: .\tools\New-PathVeerRelease.ps1 -Version 1.0.0-beta.1 -Mode Development/Signed"
Write-Host "  Verify:          .\tools\Test-PathVeerDevelopmentSigning.ps1"
Write-Host ""
Write-Host "NEVER install the dev root on a production machine or publish a dev-signed" -ForegroundColor Yellow
Write-Host "artifact to a production channel." -ForegroundColor Yellow
