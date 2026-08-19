<# SYNOPSIS devsign.10 — Tray quiescence orchestration boundary tests (single scope).

Runs in a SINGLE pwsh process. It makes a TEMP COPY of the REAL embedded
Install-PathVeer.ps1 with two deterministic edits:
  * the action dispatch is skipped (PATHVEER_INSTALL_TEST), and
  * the script's own `$TrayDir` assignment is redirected to a fake canonical
    install directory we control.
Then it overrides the two process queries (Get-CimInstance / Get-Process) with an
injected, path-scoped PID model and calls Stop-InstalledTrayIfRunning directly,
so the production detection, graceful-signal attempt, bounded fallback, and
fail-before-swap contract are evaluated against the real code path.

No real PathVeer processes are started or stopped.

Proves:
  A. No canonical installed Tray running -> proceeds (no throw).
  B. Canonical installed Tray running -> identified + quiesced.
  C. Same executable NAME from a DIFFERENT path -> ignored (dev build).
  D. Multiple canonical installed Tray processes -> all identified + quiesced.
  E. Assisted stop succeeds -> function returns (mutation allowed after).
  G. Tray cannot be stopped -> function THROWS before destructive swap.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$dir = $PSScriptRoot
while ($dir -and -not (Test-Path (Join-Path $dir 'PathVeer.Setup' 'Resources' 'Install-PathVeer.ps1'))) {
    $dir = Split-Path $dir -Parent
}
if (-not $dir) { throw "Could not locate repo root (PathVeer.Setup/Resources/Install-PathVeer.ps1)." }
$scriptPath = Join-Path $dir 'PathVeer.Setup' 'Resources' 'Install-PathVeer.ps1'
if (-not (Test-Path $scriptPath)) { throw "Embedded script not found: $scriptPath" }

# Fake canonical install directory + a fake dev-build directory.
$canon = Join-Path $env:TEMP ('PathVeer.Tray.Test.' + [guid]::NewGuid().ToString('N'))
$trayDir = Join-Path $canon 'Tray'
New-Item -ItemType Directory -Force -Path $trayDir | Out-Null
$canonExe = Join-Path $trayDir 'PathVeer.Tray.exe'
'EXE' | Set-Content -Path $canonExe
$devExe = Join-Path $env:TEMP ('PathVeer.Dev.' + [guid]::NewGuid().ToString('N') + '\Tray\PathVeer.Tray.exe')
New-Item -ItemType Directory -Force -Path (Split-Path $devExe) | Out-Null
'EXE' | Set-Content -Path $devExe

# Build a temp copy of the REAL script with deterministic edits.
$srcLines = (Get-Content $scriptPath -Raw) -split "`n"
$newLines = foreach ($line in $srcLines) {
    if ($line -match '\$TrayDir\s*=\s*Join-Path') {
        # Redirect the script's own $TrayDir to our fake canonical dir.
        "    `$TrayDir = '" + $trayDir.Replace("'", "''") + "'"
    } else {
        $line
    }
}
$src = $newLines -join "`n"
$tmpScript = Join-Path $env:TEMP ('Install-PathVeer.Test.' + [guid]::NewGuid().ToString('N') + '.ps1')
Set-Content -Path $tmpScript -Value $src -Encoding UTF8

try {
    $test = @'
param($TmpScript, $CanonExe, $DevExe)

$env:PATHVEER_INSTALL_TEST = '1'
. $TmpScript

$failures = [System.Collections.Generic.List[string]]::new()
function Assert-True($c, $m) { if (-not $c) { $failures.Add($m) } }

# PID -> ExecutablePath model. Kill removes the PID; Get-CimInstance reflects it.
$script:__pids = @{}

function Get-CimInstance {
    param($ClassName, $Filter)
    if ($ClassName -eq 'Win32_Process') {
        return @($script:__pids.Keys | ForEach-Object {
            [pscustomobject]@{ Name = 'PathVeer.Tray.exe'; ProcessId = $_; ExecutablePath = $script:__pids[$_] }
        })
    }
    return @()
}
function Get-Process {
    param($Id)
    if (-not $script:__pids.ContainsKey([int]$Id)) { return $null }
    $o = [pscustomobject]@{ ProcessId = [int]$Id }
    $o | Add-Member -MemberType ScriptMethod -Name 'Kill' -Value {
        [void]$script:__pids.Remove([int]$this.ProcessId)
    }
    return $o
}
function New-Proc($exe, $pidNum) { $script:__pids[[int]$pidNum] = $exe }

# A: no canonical installed Tray running -> proceeds.
$script:__pids = @{}
$threw = $false
try { Stop-InstalledTrayIfRunning } catch { $threw = $true }
Assert-True (-not $threw) 'A: no running Tray must not throw'

# B: canonical installed Tray running -> identified + quiesced.
$script:__pids = @{}
New-Proc $CanonExe 1001
$threw = $false
try { Stop-InstalledTrayIfRunning } catch { $threw = $true }
Assert-True (-not $threw) 'B: single canonical running must not throw'
Assert-True ($script:__pids.Count -eq 0) 'B: single canonical process must be quiesced (removed)'

# C: same NAME, different path -> ignored (dev build untouched).
$script:__pids = @{}
New-Proc $DevExe 1002
$threw = $false
try { Stop-InstalledTrayIfRunning } catch { $threw = $true }
Assert-True (-not $threw) 'C: dev-build Tray must not throw'
Assert-True ($script:__pids.Count -eq 1) 'C: dev-build Tray (different path) must NOT be touched'

# D: multiple canonical installed Tray processes -> all identified + quiesced.
$script:__pids = @{}
New-Proc $CanonExe 1003
New-Proc $CanonExe 1004
$threw = $false; $dmsg = ''
try { Stop-InstalledTrayIfRunning } catch { $threw = $true; $dmsg = $_.Exception.Message }
Assert-True (-not $threw) ('D: multiple canonical must not throw' + $(if ($threw) { ' :: ' + $dmsg }))
Assert-True ($script:__pids.Count -eq 0) 'D: all canonical processes must be quiesced'

# E: assisted stop succeeds -> function returns (mutation allowed after).
$script:__pids = @{}
New-Proc $CanonExe 1005
$threw = $false
try { Stop-InstalledTrayIfRunning } catch { $threw = $true }
Assert-True (-not $threw) 'E: assisted stop returns without throw'
Assert-True ($script:__pids.Count -eq 0) 'E: process quiesced'

# G: Tray cannot be stopped -> FAIL before swap (throw). Kill is a no-op.
function Get-Process {
    param($Id)
    $o = [pscustomobject]@{ ProcessId = [int]$Id }
    $o | Add-Member -MemberType ScriptMethod -Name 'Kill' -Value { }  # no-op: stays running
    return $o
}
$script:__pids = @{}
New-Proc $CanonExe 1006
$threw = $false; $msg = ''
try { Stop-InstalledTrayIfRunning } catch { $threw = $true; $msg = $_.Exception.Message }
Assert-True $threw 'G: unstoppable Tray MUST throw before destructive swap'
Assert-True ($msg -match 'Tray') 'G: failure message must reference Tray'

if ($failures.Count -eq 0) {
    Write-Host 'ALL TRAY-QUIESCENCE CHECKS PASSED'
    exit 0
} else {
    Write-Host 'TRAY-QUIESCENCE CHECKS FAILED:'
    foreach ($f in $failures) { Write-Host ('  - ' + $f) }
    exit 1
}
'@

    $runner = Join-Path $env:TEMP ('TrayQuiescence.Runner.' + [guid]::NewGuid().ToString('N') + '.ps1')
    Set-Content -Path $runner -Value $test -Encoding UTF8
    try {
        & pwsh -NoLogo -NoProfile -File $runner -TmpScript $tmpScript -CanonExe $canonExe -DevExe $devExe
        $rc = $LASTEXITCODE
    } finally {
        if (Test-Path $runner) { Remove-Item $runner -Force }
    }
} finally {
    if (Test-Path $tmpScript) { Remove-Item $tmpScript -Force }
}

exit $rc
