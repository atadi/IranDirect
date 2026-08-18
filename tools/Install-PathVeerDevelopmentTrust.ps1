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

    The PUBLIC root .cer is imported into the trusted store. If a certificate with the
    same thumbprint already exists in Cert:\CurrentUser\My WITH a private key (the
    operator's signing root), adding the public copy to Root would otherwise cause
    Windows to consolidate it and DROP the keyed My entry. To preserve the operator's
    signing identity, this script captures the keyed My cert object before the Root Add
    and re-adds it back into My afterwards, so Root holds the public trust copy AND My
    keeps the signing key. No PFX export / Import-PfxCertificate is used, so this stays
    non-interactive (headless-safe).

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

# Load the certificate via the public-only constructor (proven under pwsh 7.6.5:
# [X509Certificate2]::new(path) loads a .cer with HasPrivateKey=False; no KeyStorageFlags
# needed for a public certificate, and avoids any ambiguity a 2-arg overload could surface).
$cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CerPath)
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

    # CRITICAL: if a cert with the SAME thumbprint already lives in CurrentUser\My WITH a
    # private key (the operator's signing identity), calling $store.Add on Root triggers
    # Windows to consolidate it: the keyed My cert is DROPPED and only the public copy lands
    # in Root — destroying the operator's signing private key in My. To preserve the
    # operator's signing capability we capture the keyed My cert object BEFORE the Root Add,
    # then re-Add that SAME in-memory object back into My afterwards, so Root gets the public
    # trust copy and My keeps the keyed root. (Proven regression: operator root 458E7E77...
    # vanished from My after the old installer ran. No PFX / Import-PfxCertificate is used, so
    # this stays non-interactive / headless-safe.)
    $myStore = $null
    $keyedCert = $null
    try {
        $myStore = New-Object System.Security.Cryptography.X509Certificates.X509Store('My', [System.Security.Cryptography.X509Certificates.StoreLocation]::$storeLocation)
        $myStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $existingKeyed = @($myStore.Certificates.Find(
            [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $cert.Thumbprint, $false) |
            Where-Object { $_.HasPrivateKey })
        if ($existingKeyed.Count -gt 0) {
            $keyedCert = $existingKeyed[0]
            Write-Host "  Preserving operator signing key: re-adding root private key into My after trust install." -ForegroundColor DarkGray
        }
    }
    finally { if ($myStore) { $myStore.Close() } }

    if ($store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $cert.Thumbprint, $false).Count -gt 0) {
        Write-Host "  Dev root already trusted ($($cert.Thumbprint))." -ForegroundColor DarkGray
    }
    else {
        $store.Add($cert)
        Write-Step "Installed dev root into Cert:\$storeLocation\Root (thumbprint $($cert.Thumbprint))."
    }

    # Re-add the captured keyed root object back into My so the operator's signing identity
    # survives the Root consolidation (only when a keyed My cert with this thumbprint existed).
    if ($keyedCert) {
        $myStore2 = $null
        try {
            $myStore2 = New-Object System.Security.Cryptography.X509Certificates.X509Store('My', [System.Security.Cryptography.X509Certificates.StoreLocation]::$storeLocation)
            $myStore2.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $myStore2.Add($keyedCert) | Out-Null
        }
        finally { if ($myStore2) { $myStore2.Close() } }
    }
}
finally {
    if ($store) { $store.Close() }
}

Write-Host ""
Write-Host "Dev root trusted locally (public .cer imported; signing key preserved in My). Dev-signed PEs from this root will now verify on this machine." -ForegroundColor Green
Write-Host "Remove later with: .\tools\Remove-PathVeerDevelopmentTrust.ps1 -Thumbprint $($cert.Thumbprint)" -ForegroundColor DarkGray
