<#
.SYNOPSIS
    Install the PathVeer DEVELOPMENT root CA public certificate as a LOCAL trust
    anchor for private-beta / development Authenticode verification.

.DESCRIPTION
    Explicit operator action. Accepts a PUBLIC .cer file ONLY (exported from
    New-PathVeerDevelopmentSigningCertificate.ps1). It will:

      * refuse .pfx / .p12 / files containing a private key,
      * refuse to run on a non-Windows host,
      * install into Cert:\CurrentUser\Root by default (or -LocalMachine if admin),
      * NEVER touch the PathVeer production installer or any production trust path.

    This trust is for the operator's development machine to verify dev-signed
    binaries locally. It must not be present on production systems.

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

if (-not $IsWindows) {
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
Write-Host "Dev root trusted locally. Dev-signed PEs from this root will now verify on this machine." -ForegroundColor Green
Write-Host "Remove later with: .\tools\Remove-PathVeerDevelopmentTrust.ps1 -Thumbprint $($cert.Thumbprint)" -ForegroundColor DarkGray
