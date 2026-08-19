[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TrayExe
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
public class NEL2 {
    [DllImport("advapi32.dll", SetLastError=true)] public static extern bool DuplicateTokenEx(IntPtr h, int a, IntPtr la, int il, int tt, out IntPtr nt);
    [DllImport("userenv.dll", SetLastError=true)] public static extern bool CreateEnvironmentBlock(out IntPtr e, IntPtr h, bool b);
    [DllImport("userenv.dll", SetLastError=true)] public static extern bool DestroyEnvironmentBlock(IntPtr e);
    [DllImport("advapi32.dll", SetLastError=true)] public static extern bool CreateProcessAsUser(IntPtr h, string an, string cl, IntPtr pa, IntPtr ta, bool bh, uint f, IntPtr e, string cd, ref SI si, out PI pi);
    [DllImport("advapi32.dll", SetLastError=true)] public static extern bool OpenProcessToken(IntPtr h, int a, out IntPtr t);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(int a, bool i, int id);
    [DllImport("advapi32.dll", SetLastError=true)] public static extern bool GetTokenInformation(IntPtr t, int ti, IntPtr b, int s, out int r);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool CloseHandle(IntPtr h);
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)] public struct SI { public int cb; public string lpReserved; public string lpDesktop; public string lpTitle; public int dwX; public int dwY; public int dwXSize; public int dwYSize; public int dwXCountChars; public int dwYCountChars; public int dwFillAttribute; public int dwFlags; public short wShowWindow; public short cbReserved2; public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError; }
    [StructLayout(LayoutKind.Sequential)] public struct PI { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }
}
'@

Get-Process -Name "PathVeer.Tray" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$TOKEN_QUERY=0x8; $TOKEN_DUPLICATE=0x2
$CREATE_UNICODE_ENVIRONMENT=0x400u; $CREATE_NEW_CONSOLE=0x10u

# Find a non-elevated explorer and borrow its token (same logic as InstallForm).
$user=[IntPtr]::Zero; $primary=[IntPtr]::Zero; $env=[IntPtr]::Zero
$best=[IntPtr]::Zero
foreach ($proc in (Get-Process -Name "explorer" -ErrorAction SilentlyContinue)) {
    $hProc=[NEL2]::OpenProcess(0x400, $false, $proc.Id)
    if ($hProc -eq [IntPtr]::Zero) { continue }
    $hTok=[IntPtr]::Zero
    if (-not [NEL2]::OpenProcessToken($hProc, $TOKEN_QUERY, [ref]$hTok)) { [NEL2]::CloseHandle($hProc); continue }
    $buf=[System.Runtime.InteropServices.Marshal]::AllocHGlobal(4); $ret=0
    [NEL2]::GetTokenInformation($hTok, 20, $buf, 4, [ref]$ret) | Out-Null
    $elev=[System.Runtime.InteropServices.Marshal]::ReadInt32($buf)
    [System.Runtime.InteropServices.Marshal]::FreeHGlobal($buf)
    if ($elev -eq 0) {
        # duplicate to primary and use immediately
        if ([NEL2]::DuplicateTokenEx($hTok, ($TOKEN_QUERY -bor $TOKEN_DUPLICATE -bor 0x1), [IntPtr]::Zero, 2, 1, [ref]$user)) {
            [NEL2]::CloseHandle($hTok); [NEL2]::CloseHandle($hProc)
            break
        }
    } else {
        if ($best -eq [IntPtr]::Zero) { $best=$hTok } else { [NEL2]::CloseHandle($hTok) }
        [NEL2]::CloseHandle($hProc)
    }
}
if ($user -eq [IntPtr]::Zero -and $best -ne [IntPtr]::Zero) {
    if (-not [NEL2]::DuplicateTokenEx($best, ($TOKEN_QUERY -bor $TOKEN_DUPLICATE -bor 0x1), [IntPtr]::Zero, 2, 1, [ref]$user)) { $user=[IntPtr]::Zero }
    [NEL2]::CloseHandle($best)
}
if ($user -eq [IntPtr]::Zero) { "NO_USER_TOKEN_AVAILABLE"; exit 2 }

if (-not [NEL2]::CreateEnvironmentBlock([ref]$env, $user, $false)) { $env=[IntPtr]::Zero }
$si=New-Object NEL2+SI; $si.cb=[System.Runtime.InteropServices.Marshal]::SizeOf($si)
$pi=New-Object NEL2+PI
if (-not [NEL2]::CreateProcessAsUser($user, $TrayExe, $null, [IntPtr]::Zero, [IntPtr]::Zero, $false, ($CREATE_UNICODE_ENVIRONMENT -bor $CREATE_NEW_CONSOLE), $env, $null, [ref]$si, [ref]$pi)) {
    "CREATE_FAILED: $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    if ($env -ne [IntPtr]::Zero) { [NEL2]::DestroyEnvironmentBlock($env) }
    [NEL2]::CloseHandle($user); exit 1
}
"Spawned non-elevated child pid=$($pi.dwProcessId)"

Start-Sleep -Seconds 3
$procs = Get-Process -Name "PathVeer.Tray" -ErrorAction SilentlyContinue
$elevs = @()
foreach ($proc in $procs) {
    $ph=[NEL2]::OpenProcess(0x400, $false, $proc.Id)
    $th=[IntPtr]::Zero; [NEL2]::OpenProcessToken($ph, 0x8, [ref]$th) | Out-Null
    $buf=[System.Runtime.InteropServices.Marshal]::AllocHGlobal(4); $ret=0
    [NEL2]::GetTokenInformation($th, 20, $buf, 4, [ref]$ret) | Out-Null
    $elevs += [System.Runtime.InteropServices.Marshal]::ReadInt32($buf)
    [System.Runtime.InteropServices.Marshal]::FreeHGlobal($buf)
    [NEL2]::CloseHandle($th); [NEL2]::CloseHandle($ph)
}
"Tray processes found: $($procs.Count)"
"Elevation flags: $($elevs -join ',')"
"RESULT_ELEVATED=$(if ($elevs -contains 1) { 'True' } else { 'False' })"
$procs | Stop-Process -Force -ErrorAction SilentlyContinue
