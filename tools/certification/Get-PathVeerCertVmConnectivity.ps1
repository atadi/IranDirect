<# .SYNOPSIS
    PathVeer VM certification — Phase 0 connectivity + guest-state probe.

    Establishes PowerShell Direct to the PathVeer-Certification guest and writes a
    JSON evidence file to the host repo (artifacts/certification/evidence/00-connectivity.json).

    This is the UNGATED prerequisite probe. It confirms the automation channel
    (PowerShell Direct as the pvcert local admin) works BEFORE the full gate
    harness runs, and it reports the guest facts the harness needs:
      * OS / build / PS edition
      * installed .NET runtimes (the package is framework-dependent net10.0-windows)
      * network egress (can the guest reach releases.pathveer.com for the runtime)
      * default route / interface (needed for GATE-2 route evidence)

    No host PathVeer install is touched. Nothing is installed in the guest yet.

    Run from a NATIVE Windows PowerShell / Terminal window (so Get-Credential can
    pop a dialog), as the host user who owns the VM:
      pwsh -NoProfile -File tools\certification\Get-PathVeerCertVmConnectivity.ps1
#>
[CmdletBinding()]
param(
    [string]$VmName = 'PathVeer-Certification',
    [string]$EvidenceDir = (Join-Path $PSScriptRoot '..\..\artifacts\certification\evidence')
)
$ErrorActionPreference = 'Stop'

$OutDir = [System.IO.Path]::GetFullPath($EvidenceDir)
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
$OutFile = Join-Path $OutDir '00-connectivity.json'

Write-Host "Requesting PathVeer-Certification (PV-CERT) credentials via local prompt..." -ForegroundColor Cyan
$cred = Get-Credential -UserName 'PV-CERT\pvcert' -Message 'PathVeer-Certification (PV-CERT) local admin password for PowerShell Direct'
if (-not $cred) { Write-Error 'No credential supplied. Aborting.'; exit 1 }

Write-Host "Connecting to $VmName ..." -ForegroundColor Cyan
$session = $null
try {
    $session = New-PSSession -VMName $VmName -Credential $cred -ErrorAction Stop
} catch {
    Write-Error "PowerShell Direct failed: $_"
    exit 2
}

try {
    $result = Invoke-Command -Session $session -ScriptBlock {
        $ci = Get-ComputerInfo -ErrorAction SilentlyContinue |
            Select-Object WindowsProductName, WindowsVersion, OsBuildNumber, OsHardwareAbstractionLayer, CsName

        $runtimes = @(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue) +
                    @(Get-ChildItem 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue)
        $dotnetRuntimeKeys = @(Get-ChildItem 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\shared\Microsoft.NETCore.App' -ErrorAction SilentlyContinue |
            Get-ItemProperty -ErrorAction SilentlyContinue | ForEach-Object { $_.PSChildName })
        $dotnetDesktopKeys = @(Get-ChildItem 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\shared\Microsoft.WindowsDesktop.App' -ErrorAction SilentlyContinue |
            Get-ItemProperty -ErrorAction SilentlyContinue | ForEach-Object { $_.PSChildName })

        # Egress test (read-only HEAD).
        $egressOk = $false; $egressCode = $null
        try {
            $resp = Invoke-WebRequest -Uri 'https://releases.pathveer.com/windows/beta/latest.json' -Method Head -TimeoutSec 20 -UseBasicParsing -ErrorAction Stop
            $egressOk = $true; $egressCode = [int]$resp.StatusCode
        } catch { $egressCode = $_.Exception.Response.StatusCode.value__ }

        $adapters = @(Get-NetAdapter -ErrorAction SilentlyContinue | Select-Object Name, Status, InterfaceIndex, InterfaceDescription)
        $ip = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object InterfaceIndex, IPAddress)
        $routes = @(Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Select-Object InterfaceIndex, NextHop, RouteMetric)

        [PSCustomObject]@{
            whoami            = (whoami)
            hostname         = (hostname)
            computerInfo     = $ci
            psEdition        = $PSVersionTable.PSEdition
            psVersion        = $PSVersionTable.PSVersion.ToString()
            dotnetCoreRuntimes   = $dotnetRuntimeKeys
            dotnetDesktopRuntimes = $dotnetDesktopKeys
            egressReleasesPathveer = [PSCustomObject]@{ ok = $egressOk; httpStatus = $egressCode }
            netAdapters      = $adapters
            ipAddresses      = $ip
            defaultRoutes    = $routes
        }
    }

    $result | Add-Member -NotePropertyName 'capturedUtc' -NotePropertyValue (Get-Date).ToUniversalTime().ToString('o')
    $result | Add-Member -NotePropertyName 'psDirect' -NotePropertyValue 'OK'
    $result | ConvertTo-Json -Depth 6 | Set-Content -Path $OutFile -Encoding utf8
    Write-Host "Connectivity probe OK -> $OutFile" -ForegroundColor Green
    $result | Format-List
} finally {
    if ($session) { Remove-PSSession $session -ErrorAction SilentlyContinue }
}
