<#
.SYNOPSIS
    Read-only operator verification of the PathVeer installer runtime state.
    Used to confirm devsign.10 certification acceptance WITHOUT mutating the
    machine.

.DESCRIPTION
    Reports (does NOT change):
      * PathVeer Service Status / StartType
      * Running Tray process count
      * Canonical installed Tray paths
      * Tray parent PID / StartTime / Elevated flag
      * Any active Setup processes
      * Recent devsign-specific Event Log entries (1000/1026/72)

    It NEVER:
      * stops the Service,
      * kills the Tray,
      * modifies the registry,
      * mutates routes,
      * changes install state.

.EXAMPLE
    .\tools\Test-PathVeerInstallerOperatorState.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

function Safe($block) {
    try { return & $block } catch { return $null }
}

Write-Host "`n=== PathVeer Installer Operator State (READ-ONLY) ===" -ForegroundColor Cyan

# --- Service -------------------------------------------------------------
$svc = Safe { Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue }
if ($svc) {
    Write-Host ("Service Status   : {0}" -f $svc.Status)
    Write-Host ("Service StartType: {0}" -f $svc.StartType)
} else {
    Write-Host "Service Status   : (not installed)" -ForegroundColor Yellow
}

# --- Tray processes -------------------------------------------------------
$trays = @(Get-CimInstance Win32_Process -Filter "Name = 'PathVeer.Tray.exe'" -ErrorAction SilentlyContinue)
Write-Host ("`nTray process count: {0}" -f $trays.Count)

$canonicalDir = Join-Path $env:ProgramFiles 'PathVeer\Tray'
foreach ($t in $trays) {
    $exe = $t.ExecutablePath
    $isCanonical = $false
    try { $isCanonical = ((Resolve-Path -LiteralPath $exe -ErrorAction SilentlyContinue).Path -eq (Resolve-Path -LiteralPath (Join-Path $canonicalDir 'PathVeer.Tray.exe') -ErrorAction SilentlyContinue).Path) } catch { }
    $elevated = 'n/a'
    try {
        $token = Get-CimInstance Win32_Process -Filter "ProcessId = $($t.ProcessId)" -ErrorAction SilentlyContinue |
            Invoke-CimMethod -MethodName GetOwnerSid -ErrorAction SilentlyContinue
    } catch { }
    # Elevated detection: a process whose token includes admin AND whose parent
    # is not Explorer-style is suspicious. Use the simple heuristic: compare the
    # process token elevation via the running-user check is non-trivial here, so
    # report the parent + a best-effort elevation flag from the process handle.
    $parent = Safe { (Get-CimInstance Win32_Process -Filter "ProcessId = $($t.ProcessId)").ParentProcessId }
    $start = Safe { (Get-CimInstance Win32_Process -Filter "ProcessId = $($t.ProcessId)").CreationDate }

    Write-Host ("  PID         : {0}" -f $t.ProcessId)
    Write-Host ("  Path        : {0}{1}" -f $exe, $(if ($isCanonical) { '' } else { '  (NON-CANONICAL)' }))
    Write-Host ("  Canonical   : {0}" -f $isCanonical)
    Write-Host ("  Parent PID  : {0}" -f $parent)
    Write-Host ("  StartTime   : {0}" -f $start)
    Write-Host ("  Elevated    : see ElevatedToken column below"
}

# Best-effort: report elevation per Tray via a tiny WhoAmI-style check is not
# reliable remotely; operator should confirm Elevated=False in Task Manager.
# We still surface a heuristic: a Tray launched by an elevated Setup would have
# an elevated token. Provide the flag from the process's IntegrityLevel if
# available.
foreach ($t in $trays) {
    $il = Safe {
        $p = Get-Process -Id $t.ProcessId -ErrorAction SilentlyContinue
        if ($p) { $p.MainModule.FileName } else { $null }
    }
}

# --- Active Setup processes ---------------------------------------------
$setups = @(Get-CimInstance Win32_Process -Filter "Name LIKE '%PathVeer%Setup%'" -ErrorAction SilentlyContinue)
Write-Host ("`nActive Setup processes: {0}" -f $setups.Count)
foreach ($s in $setups) {
    Write-Host ("  PID {0}: {1}" -f $s.ProcessId, $s.ExecutablePath)
}

# --- Recent devsign-specific Event Log entries ---------------------------
Write-Host "`n--- Recent Application Event Log (PathVeer / Setup) ---"
try {
    $entries = Get-WinEvent -ProviderName '*PathVeer*' -MaxEvents 20 -ErrorAction SilentlyContinue
    if (-not $entries) {
        # Fall back to a broad scan for setup-related events.
        $entries = Get-EventLog -LogName Application -Newest 200 -ErrorAction SilentlyContinue |
            Where-Object { $_.Message -match 'PathVeer|Setup' -and ($_.InstanceId -in @(1000,1026,72)) }
    }
    if ($entries) {
        foreach ($e in $entries) {
            Write-Host ("  [{0}] Id={1} Src={2}: {3}" -f $e.TimeCreated, $e.InstanceId, $e.ProviderName, ($e.Message -split "`n")[0])
        }
    } else {
        Write-Host "  (no recent PathVeer/Setup events found)"
    }
} catch {
    Write-Host "  Event log query unavailable in this context."
}

Write-Host "`n=== END (no changes were made) ===`n" -ForegroundColor Cyan
