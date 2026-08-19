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

# Simulate repeated repair: each repair (re)registers the tray autorun.
for ($i = 0; $i -lt $RepairCount; $i++) {
    Set-TrayStartupEntry
}

$props = Get-ItemProperty -Path $RunKey -ErrorAction SilentlyContinue
$pathVeer = $null; $legacy = $null
if ($props) {
    if (Get-Member -InputObject $props -Name 'PathVeer Tray' -MemberType NoteProperty) {
        $pathVeer = $props.'PathVeer Tray'
    }
    if (Get-Member -InputObject $props -Name 'IranDirect Tray' -MemberType NoteProperty) {
        $legacy = $props.'IranDirect Tray'
    }
}

Write-Host "PathVeer Tray value present: $($null -ne $pathVeer)"
Write-Host "Legacy IranDirect Tray value present: $($null -ne $legacy)"

# Count how many values named 'PathVeer Tray' exist (idempotency: must be 1).
$pvCount = 0
if ($props) {
    if (Get-Member -InputObject $props -Name 'PathVeer Tray' -MemberType NoteProperty) { $pvCount++ }
}
Write-Host "PathVeer Tray value count: $pvCount (expect 1)"

$ok = ($pvCount -eq 1) -and ($null -eq $legacy)
if (-not $ok) {
    Write-Error "AUTORUN PROOF FAILED: PathVeerCount=$pvCount legacyPresent=$($null -ne $legacy)"
    exit 1
}
Write-Host "AUTORUN PASS: exactly one 'PathVeer Tray' value, no legacy 'IranDirect Tray' after $RepairCount repairs."
exit 0
