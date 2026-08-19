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

# Reproduce the FIXED InstallForm.LaunchTray(): launch through explorer.exe
# (medium IL) from this ELEVATED process. The Tray must come up NON-elevated.
Start-Process -FilePath "explorer.exe" -ArgumentList ("`"" + $TrayExe + "`"") -PassThru | Out-Null
Start-Sleep -Seconds 3

$procs = Get-Process -Name "PathVeer.Tray" -ErrorAction SilentlyContinue
$elevs = @()
foreach ($proc in $procs) {
    $ph = [Tok]::OpenProcess(0x400, $false, $proc.Id)
    $th = [IntPtr]::Zero
    [Tok]::OpenProcessToken($ph, 0x8, [ref]$th) | Out-Null
    $buf = [System.Runtime.InteropServices.Marshal]::AllocHGlobal(4)
    $ret = 0
    [Tok]::GetTokenInformation($th, 20, $buf, 4, [ref]$ret) | Out-Null
    $elevs += [System.Runtime.InteropServices.Marshal]::ReadInt32($buf)
    [System.Runtime.InteropServices.Marshal]::FreeHGlobal($buf)
    [Tok]::CloseHandle($th)
    [Tok]::CloseHandle($ph)
}

"Tray processes found: $($procs.Count)"
"Elevation flags: $($elevs -join ',')"
"RESULT_ELEVATED=$(if ($elevs -contains 1) { 'True' } else { 'False' })"

$procs | Stop-Process -Force -ErrorAction SilentlyContinue
