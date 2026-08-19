<#
.SYNOPSIS
Real-registry proof of PathVeer Tray autorun idempotency + legacy reconciliation.

Repeated install/repair calls Set-TrayStartupEntry; it must produce exactly ONE
"PathVeer Tray" HKCU Run value, and the legacy "IranDirect Tray" value must not
be present (reconciled by the migration contract).

Run from the repo root so Install-PathVeer.ps1 can be dot-sourced.
#>
[CmdletBinding()]
param(
    [int]$RepairCount = 5
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $root 'tools\Install-PathVeer.ps1') -Action status | Out-Null

$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

for ($i = 0; $i -lt $RepairCount; $i++) {
    Set-TrayStartupEntry
}

$props = Get-ItemProperty -Path $RunKey -ErrorAction SilentlyContinue
$pathVeer = if ($props) { $props.'PathVeer Tray' } else { $null }
$legacy = if ($props) { $props.'IranDirect Tray' } else { $null }

Write-Host "PathVeer Tray value present: $($null -ne $pathVeer)"
Write-Host "Legacy IranDirect Tray value present: $($null -ne $legacy)"

# Reconcile (simulate repair reconciliation that removes legacy).
Remove-TrayStartupEntry
$props2 = Get-ItemProperty -Path $RunKey -ErrorAction SilentlyContinue
$legacyAfter = if ($props2) { $props2.'IranDirect Tray' } else { $null }
Write-Host "After Remove-TrayStartupEntry legacy IranDirect Tray present: $($null -ne $legacyAfter)"

$ok = ($null -ne $pathVeer) -and ($null -eq $legacy)
if (-not $ok) {
    Write-Error "AUTORUN PROOF FAILED: PathVeer='$pathVeer' legacyBefore='$legacy'"
    exit 1
}
Write-Host "AUTORUN PASS: one 'PathVeer Tray' value, no legacy 'IranDirect Tray'."
exit 0
