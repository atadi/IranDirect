<#
.SYNOPSIS
    Read-only operator verification of the PathVeer installer runtime state.
    Used to confirm devsign.10 certification acceptance WITHOUT mutating the
    machine.

.DESCRIPTION
    Reports (does NOT change):
      * PathVeer Service Status / StartType
      * Running Tray process count
      * Per-Tray: PID, Parent PID, executable path, StartTime, canonical flag,
        and the real TokenElevation (when process-open access permits).
      * Any active Setup processes
      * Recent devsign-specific Event Log entries (1000/1026/72)

    It NEVER:
      * stops the Service,
      * kills the Tray,
      * modifies the registry,
      * mutates routes,
      * changes install state.

    TokenElevation is obtained via the same Win32 technique used in the live
    operator proof (OpenProcessToken + GetTokenInformation(TokenElevation)).
    When the current process lacks open rights on a Tray (e.g. a protected
    owner), the value is reported as 'n/a' — read-only, never mutated.

.EXAMPLE
    .\tools\Test-PathVeerInstallerOperatorState.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

# Real token-elevation read via Win32, exactly the technique the operator used
# manually. Returns $true (elevated), $false (not elevated), or $null (access
# denied / not openable). This is a read-only query; no mutation occurs.
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class TokenInfo
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenProcessToken(IntPtr hProcess, uint dwDesiredAccess, out IntPtr hToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetTokenInformation(IntPtr hToken, int tokenInformationClass, IntPtr pTokenInformation, int tokenInformationLength, out int returnLength);

    public static bool? IsElevated(int pid)
    {
        const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        const uint TOKEN_QUERY = 0x0008;
        const int TokenElevation = 20;

        IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc == IntPtr.Zero) return null;

        try
        {
            if (!OpenProcessToken(hProc, TOKEN_QUERY, out IntPtr hToken)) return null;
            try
            {
                int elevation = 0;
                int outLen;
                IntPtr p = Marshal.AllocHGlobal(4);
                try
                {
                    if (!GetTokenInformation(hToken, TokenElevation, p, 4, out outLen)) return null;
                    elevation = Marshal.ReadInt32(p);
                }
                finally { Marshal.FreeHGlobal(p); }
                return elevation != 0;
            }
            finally { CloseHandle(hToken); }
        }
        finally { CloseHandle(hProc); }
    }
}
'@

function Get-Elevation([int]$Pid) {
    try { return [TokenInfo]::IsElevated($Pid) } catch { return $null }
}

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

# --- Tray processes ------------------------------------------------------
$trays = @(Get-CimInstance Win32_Process -Filter "Name = 'PathVeer.Tray.exe'" -ErrorAction SilentlyContinue)
Write-Host ("`nTray process count: {0}" -f $trays.Count)

$canonicalDir = Join-Path $env:ProgramFiles 'PathVeer\Tray'
$canonicalExe = Join-Path $canonicalDir 'PathVeer.Tray.exe'
try { $canonicalResolved = (Resolve-Path -LiteralPath $canonicalExe -ErrorAction SilentlyContinue).Path } catch { $canonicalResolved = $null }

foreach ($t in $trays) {
    $exe = $t.ExecutablePath
    $isCanonical = $false
    try {
        $resolved = (Resolve-Path -LiteralPath $exe -ErrorAction SilentlyContinue).Path
        if ($resolved -and $canonicalResolved) {
            $isCanonical = ([string]::Equals($resolved, $canonicalResolved, [System.StringComparison]::OrdinalIgnoreCase))
        }
    } catch { }

    $parent = Safe { (Get-CimInstance Win32_Process -Filter "ProcessId = $($t.ProcessId)").ParentProcessId }
    $start = Safe { (Get-CimInstance Win32_Process -Filter "ProcessId = $($t.ProcessId)").CreationDate }
    $elevated = Get-Elevation ([int]$t.ProcessId)
    if ($null -eq $elevated) { $elevatedStr = 'n/a (access denied)' } else { $elevatedStr = $elevated.ToString() }

    Write-Host ("  PID         : {0}" -f $t.ProcessId)
    Write-Host ("  Path        : {0}{1}" -f $exe, $(if ($isCanonical) { '' } else { '  (NON-CANONICAL)' }))
    Write-Host ("  Canonical   : {0}" -f $isCanonical)
    Write-Host ("  Parent PID  : {0}" -f $parent)
    Write-Host ("  StartTime   : {0}" -f $start)
    Write-Host ("  Elevated    : {0}" -f $elevatedStr)
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
            Where-Object { $_.Message -match 'PathVeer|Setup' -and ($_.InstanceId -in @(1000, 1026, 72)) }
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
