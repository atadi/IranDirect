<#
.SYNOPSIS
    Phase 37.9 — Create a local self-signed DEVELOPMENT Authenticode certificate.

.DESCRIPTION
    Creates a two-tier self-signed development certificate chain for PRIVATE
    development / private-beta Authenticode signing only:

      * Root  CN=PathVeer Development Root CA        (CA:TRUE, PathLen:0, RSA 4096, SHA-256, KU CertSign+CRLSign, NO EKU)
      * Leaf  CN=PathVeer Development Code Signing   (End-Entity, EKU codeSigning 1.3.6.1.5.5.7.3.3, RSA 4096, SHA-256, KU DigitalSignature)

    Both certificates use the STRICT Microsoft-compatible profile proven by real
    SignTool evidence (see docs/release/development-signing.md):

      * The ROOT MUST carry NO Extended Key Usage (EKU) extension. New-SelfSignedCertificate
        injects a default Client+Server Authentication EKU unless -Type Custom is given;
        that default EKU causes `signtool verify /pa` to fail with "The signing certificate
        is not valid for the requested usage." -Type Custom suppresses the default EKU.
      * Root Basic Constraints is the CANONICAL DER SEQUENCE { BOOLEAN TRUE ; INTEGER 0 }
        = 30 06 01 01 FF 02 01 00 (CA:TRUE, PathLength:0). The non-canonical 30 03 ... form
        is rejected by strict application-policy validators.
      * The leaf is an End Entity (Basic Constraints SEQUENCE { } = 30 00) with the exact
        code-signing EKU and DigitalSignature KeyUsage.

    The PRIVATE KEYS NEVER LEAVE THE LOCAL CERTIFICATE STORE and are NEVER written to the
    repository, logs, or env. The script only ever exports:
      - the root PUBLIC certificate (.cer) for trust installation, and
      - an OPTIONAL encrypted PFX backup of the leaf (operator passphrase).

    Trust installation is a SEPARATE explicit operator action
    (Install-PathVeerDevelopmentTrust.ps1 imports the public .cer into Cert:\CurrentUser\Root).
    This generator does NOT move the private-key root into the trusted store and does NOT
    silently install trust.

    This is explicitly NOT a production identity. Production continues to require a real
    public-CA Authenticode certificate. Never combine dev signing with production publication
    (Publish-PathVeerRelease.ps1 hard-fails that combination).

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

.PARAMETER Rotate
    Required to create a replacement when a matching dev root/leaf already exists in the
    local store (partial/rerun safety). Without it, the script fails safely and refuses to
    create an orphan root.
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
    [int]$ValidityYears = 5,

    [Parameter(Mandatory = $false)]
    [switch]$Rotate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$m) { Write-Host $m -ForegroundColor Cyan }

# Windows check. Use a distinct name: PowerShell variables are case-insensitive, and $IsWindows
# is a read-only automatic constant on PowerShell 7, so we must not assign to it (or any
# case-variant of it).
$runningWindows = $env:OS -eq 'Windows_NT'
if (-not $runningWindows) {
    throw "Development certificate creation requires Windows (Cert:/ drive + Windows crypto APIs)."
}

# --- Canonical DER encodings (proven by real SignTool evidence) -------------------
# Root Basic Constraints: SEQUENCE { BOOLEAN TRUE ; INTEGER 0 } = CA:TRUE, PathLength:0
$RootBasicConstraintsHex = '30060101ff020100'
# Leaf Basic Constraints: SEQUENCE { } = End Entity (CA:false)
$LeafBasicConstraintsHex = '3000'

# --- Partial-state / rerun safety ---------------------------------------------
# Returns a SAFE array (never $null) of any existing dev root/leaf certs, so callers
# can use .Count reliably under Set-StrictMode (an empty pipeline otherwise returns
# $null, and `$null.Count` throws).
function Find-ExistingDevCerts {
    $found = @()
    foreach ($storeName in @('My', 'Root')) {
        try {
            $items = Get-ChildItem -Path "Cert:\CurrentUser\$storeName" -ErrorAction SilentlyContinue
            if ($items) {
                $found += @($items) | Where-Object { $_.Subject -eq "CN=$RootName" -or $_.Subject -eq "CN=$LeafName" }
            }
        } catch { }
    }
    return $found
}

# @(...) guarantees an array even when Find-ExistingDevCerts matches nothing (a
# zero-match pipeline emits $null, not @(), under Set-StrictMode).
$existing = @(Find-ExistingDevCerts)
if ($existing.Count -gt 0) {
    if (-not $Rotate) {
        $thumbs = ($existing | ForEach-Object { $_.Thumbprint }) -join ', '
        throw ("A matching development certificate already exists in the local store (thumbs: $thumbs). " +
               "Refusing to create an orphan root. Remove the existing cert (and its trust) first, " +
               "or pass -Rotate to explicitly replace it.")
    }
    Write-Step "Rotating: removing $($existing.Count) existing dev cert(s) before recreation..."
    foreach ($c in $existing) {
        try { Remove-Item -Path "Cert:\CurrentUser\My\$($c.Thumbprint)" -ErrorAction SilentlyContinue } catch { }
        try { Remove-Item -Path "Cert:\CurrentUser\Root\$($c.Thumbprint)" -ErrorAction SilentlyContinue } catch { }
    }
}

$notBefore = Get-Date
$notAfter  = $notBefore.AddYears($ValidityYears)

# --- Root CA (NO EKU; CA:TRUE; PathLength:0; KU CertSign+CRLSign) ---------------
Write-Step "Creating development root CA: $RootName"
$root = New-SelfSignedCertificate -CertStoreLocation 'Cert:\CurrentUser\My' -Type Custom `
    -Subject "CN=$RootName" `
    -KeyAlgorithm RSA -KeyLength 4096 -HashAlgorithm SHA256 `
    -KeyUsage CertSign, CRLSign -KeyUsageProperty Sign `
    -NotBefore $notBefore -NotAfter $notAfter `
    -TextExtension @("2.5.29.19={hex}$RootBasicConstraintsHex")

# NOTE: the root stays in Cert:\CurrentUser\My (private key local). Trust is installed
# separately by Import-PathVeerDevelopmentTrust.ps1 importing the exported PUBLIC .cer.
# We deliberately do NOT Move-Item the private-key root into Cert:\CurrentUser\Root.

# --- Leaf (code signing End Entity; EKU codeSigning; KU DigitalSignature) -------
Write-Step "Creating development code-signing leaf: $LeafName"
$leaf = New-SelfSignedCertificate -CertStoreLocation 'Cert:\CurrentUser\My' -Type Custom `
    -Subject "CN=$LeafName" `
    -KeyAlgorithm RSA -KeyLength 4096 -HashAlgorithm SHA256 `
    -KeyUsage DigitalSignature -KeyUsageProperty Sign `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', "2.5.29.19={hex}$LeafBasicConstraintsHex") `
    -Signer $root `
    -NotBefore $notBefore -NotAfter $notAfter

# --- Exports (public only; private key stays in store) --------------------------
Write-Step "Exporting root public certificate -> $ExportRootCerPath"
Export-Certificate -Cert $root -FilePath $ExportRootCerPath -Type CERT | Out-Null

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
Write-Host "Next steps (operator, on this machine):" -ForegroundColor Cyan
Write-Host "  Trust root:      .\tools\Install-PathVeerDevelopmentTrust.ps1 -CerPath '$ExportRootCerPath'"
Write-Host "  Set signing env:  `$env:PATHVEER_DEV_CODESIGN_THUMBPRINT = '$($leaf.Thumbprint)'"
Write-Host "  Sign dev bundle: .\tools\New-PathVeerRelease.ps1 -Version 1.0.0-beta.1 -Mode Development/Signed"
Write-Host "  Verify:          .\tools\Test-PathVeerDevelopmentSigning.ps1 -CreateDisposableTestCert"
Write-Host ""
Write-Host "NEVER install the dev root on a production machine or publish a dev-signed" -ForegroundColor Yellow
Write-Host "artifact to a production channel." -ForegroundColor Yellow
