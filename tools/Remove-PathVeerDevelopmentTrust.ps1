<#
.SYNOPSIS
    Remove the PathVeer DEVELOPMENT root CA trust from the local certificate store.

.DESCRIPTION
    Explicit operator action to withdraw local development trust. Removes the
    dev root (by thumbprint) from Cert:\CurrentUser\Root and/or
    Cert:\LocalMachine\Root. Only affects the dev root; production trust is
    untouched.

.PARAMETER Thumbprint
    SHA-1 thumbprint of the development root to remove.

.PARAMETER LocalMachine
    Also remove from Cert:\LocalMachine\Root (requires administrator).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Thumbprint,

    [Parameter(Mandatory = $false)]
    [switch]$LocalMachine
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$m) { Write-Host $m -ForegroundColor Cyan }

if (-not $IsWindows) { throw "Trust removal requires Windows (Cert:\ store)." }

foreach ($loc in @('CurrentUser') + $(if ($LocalMachine) { @('LocalMachine') } else { @() })) {
    $store = $null
    try {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
            'Root', [System.Security.Cryptography.X509Certificates.StoreLocation]::$loc)
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $found = $store.Certificates.Find(
            [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $Thumbprint, $false)
        if ($found.Count -gt 0) {
            $store.Remove($found[0])
            Write-Step "Removed dev root $Thumbprint from Cert:\$loc\Root."
        }
        else {
            Write-Host "  Dev root $Thumbprint not present in Cert:\$loc\Root (nothing to do)." -ForegroundColor DarkGray
        }
    }
    finally { if ($store) { $store.Close() } }
}

Write-Host "Done. Production trust and the PathVeer production installer are unaffected." -ForegroundColor Green
