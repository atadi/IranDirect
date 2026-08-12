<#
.SYNOPSIS
    Operator-authorized bootstrap of the PathVeer certification JEA control plane.

.DESCRIPTION
    Registers a narrow, certification-only Just Enough Administration endpoint
    'PathVeer.Certification' in THIS guest. The endpoint runs privileged certification
    commands as a per-connection virtual account (BUILTIN\Administrators) while the
    connecting user keeps a filtered standard token.

    This MUST run ONCE with genuine local elevation (UAC/admin PowerShell) inside the
    PV-CERT-WINDOWS guest, NOT from the filtered PowerShell Direct session. The filtered
    parent cannot register the endpoint itself — that is the exact boundary that made the
    old Scheduled-Task design impossible ("Access is denied").

    After this succeeds and the JEA probe passes, take a new checkpoint PV-CERT-HARNESS
    derived from PV-CLEAN-WINDOWS. Never modify or overwrite PV-CLEAN-WINDOWS.

    SECURITY: does not create an unrestricted admin endpoint, does not disable UAC,
    does not set LocalAccountTokenFilterPolicy, does not enable autologon, and does not
    install a persistent privileged service. It registers only a documented, reversible
    certification control plane.

.PARAMETER SourceDir
    Directory containing PathVeer.Certification.pssc and PathVeerCertificationRole.psrc.
    Defaults to the directory of this script.
#>
[CmdletBinding()]
param(
    [string]$SourceDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Enable-PathVeerCertificationJea must run from an ELEVATED PowerShell session (Run as Administrator).'
}

$pssc   = Join-Path $SourceDir 'PathVeer.Certification.pssc'
$role   = Join-Path $SourceDir 'PathVeerCertificationRole.psrc'
if (-not (Test-Path $pssc))  { throw "Session config not found: $pssc" }
if (-not (Test-Path $role))  { throw "Role capability not found: $role" }

# Where the guest keeps certification instrumentation.
$modulePath = Join-Path $env:ProgramFiles 'PathVeerCertificationJea'
$roleDir    = Join-Path $modulePath 'RoleCapabilities'
New-Item -ItemType Directory -Force -Path $roleDir | Out-Null

# Stage the role capability + session config in the guest module path so
# Register-PSSessionConfiguration can resolve PathVeerCertificationRole by name.
Copy-Item -Path $role -Destination (Join-Path $roleDir 'PathVeerCertificationRole.psrc') -Force
Copy-Item -Path $pssc -Destination (Join-Path $modulePath 'PathVeer.Certification.pssc') -Force

# Make the module discoverable for role-capability resolution.
$moduleManifest = Join-Path $modulePath 'PathVeerCertificationJea.psd1'
if (-not (Test-Path $moduleManifest)) {
    New-ModuleManifest -Path $moduleManifest -RootModule 'PathVeerCertificationJea.psm1' `
        -Description 'PathVeer certification JEA module' `
        -TypesToProcess @() -FormatsToProcess @() -FunctionsToExport @()
}

# Register the endpoint (genuine admin action, performed by the operator here).
$configName = 'PathVeer.Certification'
$existing = Get-PSSessionConfiguration -Name $configName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Endpoint '$configName' already registered; re-registering." -ForegroundColor Yellow
    Unregister-PSSessionConfiguration -Name $configName -Force -ErrorAction Stop
}
Register-PSSessionConfiguration -Path $pssc -Name $configName -Force -ErrorAction Stop

Write-Host "Registered JEA endpoint '$configName'." -ForegroundColor Green
Write-Host "Next: from the HOST run Test-PathVeerCertGuestJea.ps1 to prove the privileged context." -ForegroundColor Cyan
