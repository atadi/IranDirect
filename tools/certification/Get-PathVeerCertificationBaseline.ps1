<# .SYNOPSIS
    Phase 37.6 — capture the certification VM baseline snapshot metadata.

.DESCRIPTION
    Records the disposable VM's environment into a structured JSON file used as
    certification evidence (Gate matrix §18). Pure read-only inspection; never
    mutates the VM. Safe to run before any destructive certification scenario.

.PARAMETER OutFile
    Path for the baseline JSON evidence (default: ./cert-baseline.json).
#>
[CmdletBinding()]
param(
    [string]$OutFile = './cert-baseline.json'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$os = Get-CimInstance Win32_OperatingSystem
$cs = Get-CimInstance Win32_ComputerSystem
$netRTC = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
$dotnetRuntimes = & dotnet --list-runtimes 2>$null
$psVer = $PSVersionTable.PSVersion.ToString()

$baseline = [ordered]@{
    capturedUtc     = (Get-Date).ToUniversalTime().ToString('o')
    computerName    = $cs.Name
    windowsEdition  = $os.Caption.Trim()
    windowsVersion  = $os.Version
    windowsBuild    = $os.BuildNumber
    architecture    = $env:PROCESSOR_ARCHITECTURE
    isServer        = ($os.ProductType -ne 1)   # 1=Workstation
    totalRAMGB      = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
    dotnetSdkRuntimes = @($dotnetRuntimes)
    netFramework48  = if ($netRTC) { $netRTC } else { $null }
    powershellVersion = $psVer
    admin           = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    networkAdapters  = @(Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' } | ForEach-Object { $_.Name })
    notes           = 'Disposable certification VM baseline. Never commit PII/secret material.'
}

$baseline | ConvertTo-Json -Depth 4 | Set-Content -Path $OutFile -Encoding utf8
Write-Host "Baseline captured -> $OutFile" -ForegroundColor Cyan
$baseline | Format-List
