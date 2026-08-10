<# .SYNOPSIS
    Phase 37.6 — verify a PathVeer installation layout on a certification VM.

.DESCRIPTION
    Read-only post-install verification used by GATE-5/8/14/15. Checks:
      * PathVeer Service is registered/running
      * CLI on PATH (single entry, no duplication)
      * Tray executable present
      * Start Menu shortcuts present
      * Apps & Features entry present (Publisher/Version/Uninstall)
      * ProgramData state root present
    Emits a JSON verification report. Does not mutate the install.

.PARAMETER StateRoot
    ProgramData state root (default: $env:ProgramData\PathVeer).
.PARAMETER OutFile
    Path for the verification JSON (default: ./install-verify.json).
#>
[CmdletBinding()]
param(
    [string]$StateRoot = '',
    [string]$OutFile = './install-verify.json'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $StateRoot) { $StateRoot = Join-Path $env:ProgramData 'PathVeer' }

$svc = Get-CimInstance Win32_Service -Filter "Name='PathVeer'" -ErrorAction SilentlyContinue
$cliOnPath = @($env:Path -split ';' | Where-Object { $_ -and (Test-Path (Join-Path $_ 'PathVeer.Cli.exe')) })
$tray = Get-ChildItem "$env:ProgramFiles\PathVeer" -Recurse -Filter PathVeer.Tray.exe -ErrorAction SilentlyContinue | Select-Object -First 1
$startMenu = @()
try {
    $startMenu = @(Get-ChildItem "$env:ProgramData\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter *.lnk -ErrorAction SilentlyContinue |
        Where-Object { $null -ne $_ -and $_.PSObject.Properties['Name'] -and $_.Name -match 'PathVeer' })
} catch { }

$appEntry = $null
try {
    $uninstallKeys = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue
    foreach ($k in $uninstallKeys) {
        if ($null -ne $k -and $k.PSObject.Properties['DisplayName'] -and $k.DisplayName -eq 'PathVeer') {
            $appEntry = $k
            break
        }
    }
} catch { }

$report = [ordered]@{
    capturedUtc        = (Get-Date).ToUniversalTime().ToString('o')
    serviceRegistered  = ($null -ne $svc)
    serviceState       = if ($svc) { $svc.State } else { 'absent' }
    serviceStartMode   = if ($svc) { $svc.StartMode } else { $null }
    cliPathEntries     = $cliOnPath.Count
    cliPathSingleEntry = ($cliOnPath.Count -eq 1)
    trayPresent        = ($null -ne $tray)
    startMenuShortcuts = @($startMenu | ForEach-Object { if ($_ -and $_.PSObject.Properties['Name']) { $_.Name } else { $null } } | Where-Object { $_ })
    appsAndFeatures    = if ($appEntry) { [ordered]@{ displayName = $appEntry.DisplayName; version = $appEntry.DisplayVersion; publisher = $appEntry.Publisher; uninstall = $appEntry.UninstallString } } else { $null }
    stateRootExists    = (Test-Path $StateRoot)
    pathClean          = ($cliOnPath.Count -le 1)
}
$report | ConvertTo-Json -Depth 4 | Set-Content -Path $OutFile -Encoding utf8
Write-Host "Install verification -> $OutFile" -ForegroundColor Cyan
$report | Format-List
