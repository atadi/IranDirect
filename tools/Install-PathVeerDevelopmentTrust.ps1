<#
.SYNOPSIS
    Install the PathVeer DEVELOPMENT root CA public certificate as a LOCAL trust
    anchor for private-beta / development Authenticode verification.

.DESCRIPTION
    Explicit operator action. Accepts a PUBLIC .cer file ONLY (exported from
    New-PathVeerDevelopmentSigningCertificate.ps1). It will:

      * refuse .pfx / .p12 / files containing a private key,
      * refuse to run on a non-Windows host,
      * validate the public root is a proper CA (Basic Constraints CA:TRUE) with NO
        Extended Key Usage (a dev root must NOT carry Client/Server Auth EKU, or
        Windows application-policy validation rejects code-signing children),
      * install into Cert:\CurrentUser\Root by default (or -LocalMachine if admin),
      * NEVER touch the PathVeer production installer or any production trust path.

    This trust is for the operator's development machine to verify dev-signed
    binaries locally. It must not be present on production systems.

    The PUBLIC root .cer is imported into the trusted store; the private key remains
    in the signing workstation's Cert:\CurrentUser\My and is NEVER moved or exported by
    this script.

.PARAMETER CerPath
    Path to the root PUBLIC certificate (.cer). Required.

.PARAMETER LocalMachine
    Install into Cert:\LocalMachine\Root instead of Cert:\CurrentUser\Root
    (requires administrator). Use only on dedicated dev/test hosts.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CerPath,

    [Parameter(Mandatory = $false)]
    [switch]$LocalMachine
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$m) { Write-Host $m -ForegroundColor Cyan }

# PS-version-agnostic Windows detection. PowerShell variables are case-insensitive and $IsWindows
# is a read-only automatic constant on PowerShell 7, so we use a distinct name and never assign
# to (any case-variant of) $IsWindows.
$runningWindows = $env:OS -eq 'Windows_NT'
if (-not $runningWindows) {
    throw "Trust installation requires Windows (Cert:\ store)."
}
if (-not (Test-Path $CerPath)) {
    throw "Certificate file not found: $CerPath"
}

$ext = [System.IO.Path]::GetExtension($CerPath).ToLowerInvariant()
if ($ext -notin @('.cer', '.crt')) {
    throw "Only a PUBLIC certificate (.cer/.crt) is accepted. Refusing '$ext' (may contain a private key)."
}
if ($ext -in @('.pfx', '.p12')) {
    throw "Refusing private-key container '$CerPath'. Pass the public .cer only."
}

# Load the certificate bytes and confirm there is no private key.
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
    $CerPath, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::DefaultKeySet)
if ($cert.HasPrivateKey) {
    throw "The provided file contains a PRIVATE KEY. Install-PathVeerDevelopmentTrust accepts public .cer only."
}
if (-not $cert.Subject.Contains('Development Root CA')) {
    Write-Warning "Certificate subject does not look like the PathVeer development root: $($cert.Subject)"
}

# Validate the root profile: it MUST be a CA with NO EKU. A root carrying a default
# Client/Server Auth EKU would make `signtool verify /pa` reject code-signing children.
$bc = $cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' }
$eku = $cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' }
if (-not $bc) {
    throw "The provided root has NO Basic Constraints extension; it is not a valid CA certificate."
}
$bcRaw = [System.BitConverter]::ToString($bc.RawData).Replace('-', '').ToLowerInvariant()
if ($bcRaw -notmatch '^3006|^30') {
    throw "Unrecognized Basic Constraints encoding on the root: $bcRaw"
}
if ($eku) {
    $ekuVals = ($eku.EnhancedKeyUsages | ForEach-Object { $_.Value }) -join ','
    throw "The provided root carries an Extended Key Usage ($ekuVals). A development root MUST have NO EKU; install would break code-signing chain validation (signtool verify /pa)."
}

$storeLocation = if ($LocalMachine) { 'LocalMachine' } else { 'CurrentUser' }
$store = $null
try {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('Root', [System.Security.Cryptography.X509Certificates.StoreLocation]::$storeLocation)
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    if ($store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $cert.Thumbprint, $false).Count -gt 0) {
        Write-Host "  Dev root already trusted ($($cert.Thumbprint))." -ForegroundColor DarkGray
    }
    else {
        $store.Add($cert)
        Write-Step "Installed dev root into Cert:\$storeLocation\Root (thumbprint $($cert.Thumbprint))."
    }
}
finally {
    if ($store) { $store.Close() }
}

Write-Host ""
Write-Host "Dev root trusted locally (public .cer imported; private key untouched). Dev-signed PEs from this root will now verify on this machine." -ForegroundColor Green
Write-Host "Remove later with: .\tools\Remove-PathVeerDevelopmentTrust.ps1 -Thumbprint $($cert.Thumbprint)" -ForegroundColor DarkGray
