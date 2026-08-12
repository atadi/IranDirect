<#
.SYNOPSIS
    Reversible teardown of the PathVeer certification JEA control plane.

.DESCRIPTION
    Removes ONLY the certification instrumentation this project created:
      - JEA endpoint registration 'PathVeer.Certification'
      - guest module path C:\Program Files\PathVeerCertificationJea
      - generated transcripts under C:\ProgramData\PathVeerCertificationJea\Transcripts

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

# Remove ONLY the certification module we installed (under the standard Windows PowerShell module
# path, matching Enable-PathVeerCertificationJea.ps1). Does not touch unrelated modules.
$modulePath = Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules\PathVeerCertificationJea'
if (Test-Path $modulePath) {
    Remove-Item -Path $modulePath -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Removed certification module: $modulePath" -ForegroundColor Green
} else {
    Write-Host "Certification module not present at $modulePath; nothing to remove." -ForegroundColor Yellow
}

# Remove certification-specific instrumentation under ProgramData (transcripts + protected tree).
# This is certification scaffolding, not PathVeer product state.
$protectedRoot = 'C:\ProgramData\PathVeerCertificationJea'
if (Test-Path $protectedRoot) {
    Remove-Item -Path $protectedRoot -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Removed certification instrumentation: $protectedRoot" -ForegroundColor Green
} else {
    Write-Host "Certification instrumentation not present at $protectedRoot; nothing to remove." -ForegroundColor Yellow
}

Write-Host 'Certification JEA control plane removed. Unrelated Windows/PathVeer state untouched.' -ForegroundColor Cyan
