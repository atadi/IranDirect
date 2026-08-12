<#
.SYNOPSIS
    Reversible teardown of the PathVeer certification JEA control plane.

.DESCRIPTION
    Removes ONLY the certification instrumentation this project created:
      - JEA endpoint registration 'PathVeer.Certification'
      - guest module path C:\Program Files\PathVeerCertificationJea
      - generated transcripts under C:\pv-cert\jea-transcripts

    It does NOT touch Windows system state, .NET runtimes, or any PathVeer product
    install. Safe to run with or without PathVeer present.

    Must run from an ELEVATED PowerShell session in the guest.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Disable-PathVeerCertificationJea must run from an ELEVATED PowerShell session (Run as Administrator).'
}

$configName = 'PathVeer.Certification'
$existing = Get-PSSessionConfiguration -Name $configName -ErrorAction SilentlyContinue
if ($existing) {
    Unregister-PSSessionConfiguration -Name $configName -Force -ErrorAction Stop
    Write-Host "Unregistered JEA endpoint '$configName'." -ForegroundColor Green
} else {
    Write-Host "JEA endpoint '$configName' not registered; nothing to remove." -ForegroundColor Yellow
}

$modulePath = Join-Path $env:ProgramFiles 'PathVeerCertificationJea'
if (Test-Path $modulePath) { Remove-Item -Path $modulePath -Recurse -Force -ErrorAction SilentlyContinue }

$transcripts = 'C:\pv-cert\jea-transcripts'
if (Test-Path $transcripts) { Remove-Item -Path $transcripts -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host 'Certification JEA control plane removed. Unrelated Windows/PathVeer state untouched.' -ForegroundColor Cyan
