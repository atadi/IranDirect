<#
.SYNOPSIS
    DEPRECATED — superseded by tools/Install-PathVeer.ps1.

.DESCRIPTION
    Phase 36.7 replaced this publish-directory script with a proper package
    model (New-PathVeerPackage.ps1 produces a verified package; Install-PathVeer.ps1
    installs it). The old script pointed the SCM directly at
    C:\codespace\PathVeer\artifacts\..., which is a developer checkout and
    violates the "no install path under the repo" requirement.

    Keeping this shim avoids breaking any existing callers that still invoke it,
    while routing them to the supported mechanism.

.PARAMETER Version
    Product version, forwarded to New-PathVeerPackage.ps1.

.PARAMETER RuntimeIdentifier
    win-x64 (default) or win-arm64.

.EXAMPLE
    .\tools/Install-PathVeerService.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$RuntimeIdentifier = 'win-x64'
)

# Resolve the real script next to this shim.
$realScript = Join-Path $PSScriptRoot 'New-PathVeerPackage.ps1'

Write-Warning "Install-PathVeerService.ps1 is deprecated. Producing the Phase 36.7 package via New-PathVeerPackage.ps1, then use Install-PathVeer.ps1 to install it."

& $realScript -Version $Version -RuntimeIdentifier $RuntimeIdentifier
