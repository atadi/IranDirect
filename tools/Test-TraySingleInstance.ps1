<#
.SYNOPSIS
Real-process proof of PathVeer Tray single-instance enforcement.

Launches the published PathVeer.Tray.exe repeatedly and asserts the process
count stays at exactly ONE, and that killing the one legitimate instance and
restarting yields 1 again.

This is the certification acceptance "real 10x launch process-count proof".
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TrayExe,

    [int]$LaunchCount = 10
)

$ErrorActionPreference = 'Stop'

function Get-TrayCount {
    (Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue).Count
}

if (-not (Test-Path $TrayExe)) { throw "TrayExe not found: $TrayExe" }

# Ensure a clean baseline.
Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# Initial: zero.
$initial = Get-TrayCount
Write-Host "initial=$initial (expect 0)"

# First launch.
Start-Process -FilePath $TrayExe -WindowStyle Hidden
Start-Sleep -Seconds 2
$first = Get-TrayCount
Write-Host "first_launch=$first (expect 1)"

# 10 (or N) additional launches.
for ($i = 0; $i -lt $LaunchCount; $i++) {
    Start-Process -FilePath $TrayExe -WindowStyle Hidden
    Start-Sleep -Milliseconds 400
}
Start-Sleep -Seconds 2
$afterMany = Get-TrayCount
Write-Host "after_${LaunchCount}_launches=$afterMany (expect 1)"

# Kill the one legitimate instance, then restart -> 0 -> 1.
Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
$restart0 = Get-TrayCount
Write-Host "after_kill=$restart0 (expect 0)"
Start-Process -FilePath $TrayExe -WindowStyle Hidden
Start-Sleep -Seconds 2
$restart1 = Get-TrayCount
Write-Host "after_restart=$restart1 (expect 1)"

# Cleanup.
Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force

$ok = ($initial -eq 0) -and ($first -eq 1) -and ($afterMany -eq 1) -and ($restart0 -eq 0) -and ($restart1 -eq 1)
if (-not $ok) {
    Write-Error "TRAY_SINGLE_INSTANCE FAILED: initial=$initial first=$first many=$afterMany restart0=$restart0 restart1=$restart1"
    exit 1
}
Write-Host "TRAY_SINGLE_INSTANCE PASS: 0 -> 1 -> 1 (x$LaunchCount) -> 0 -> 1"
exit 0
