[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TrayExe
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Tok {
    [DllImport("advapi32.dll")] public static extern bool OpenProcessToken(IntPtr h, int a, out IntPtr t);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a, bool i, int id);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll")] public static extern bool GetTokenInformation(IntPtr t, int ti, IntPtr b, int s, out int r);
}
'@

# Reproduce InstallForm.LaunchTray(): UseShellExecute=true, no runas verb, from
# this ELEVATED process (the released installer self-elevates before reaching
# LaunchTray on success).
$p = Start-Process -FilePath $TrayExe -PassThru
Start-Sleep -Seconds 2

$proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
if (-not $proc) { Write-Error "Tray did not start"; exit 1 }

$ph = [Tok]::OpenProcess(0x400, $false, $proc.Id)          # PROCESS_QUERY_LIMITED_INFORMATION
$th = [IntPtr]::Zero
[Tok]::OpenProcessToken($ph, 0x8, [ref]$th) | Out-Null      # TOKEN_QUERY
$buf = [System.Runtime.InteropServices.Marshal]::AllocHGlobal(4)
$ret = 0
[Tok]::GetTokenInformation($th, 20, $buf, 4, [ref]$ret) | Out-Null  # TokenElevation = 20
$elev = [System.Runtime.InteropServices.Marshal]::ReadInt32($buf)
[System.Runtime.InteropServices.Marshal]::FreeHGlobal($buf)
[Tok]::CloseHandle($th)
[Tok]::CloseHandle($ph)

"ChildTray_TokenElevationFlag=$elev (1=elevated, 0=not)"
"RESULT_ELEVATED=$(if ($elev -eq 1) { 'True' } else { 'False' })"

$proc | Stop-Process -Force -ErrorAction SilentlyContinue
