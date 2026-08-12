<#
.SYNOPSIS
    Operator-authorized bootstrap of the PathVeer certification JEA control plane.

.DESCRIPTION
    Registers a narrow, certification-only Just Enough Administration endpoint
    'PathVeer.Certification' in THIS guest. Privileged certification commands run as a
    per-connection virtual account while the connecting user keeps a filtered standard token.

    Must run ONCE with genuine local elevation (Run as Administrator) inside the PV-CERT-WINDOWS
    guest, NOT from the filtered PowerShell Direct session (Windows denies bootstrapping elevation
    from the filtered parent — that is why the old Scheduled-Task design failed with "Access denied").

    Security-critical setup performed here:
      - Installs the trusted module (PathVeerCertificationJea) with ONLY validated wrapper functions.
      - Creates C:\ProgramData\PathVeerCertificationJea\Transcripts with ACLs so that the ordinary /
        filtered PV-CERT\pvcert account CANNOT modify or delete audit transcripts (SYSTEM + local
        Administrators have full control; pvcert gets no access).
      - Registers the endpoint with RoleDefinitions limited to PV-CERT\pvcert (not all local admins).

    After this succeeds and Test-PathVeerCertGuestJea.ps1 proves both privileged execution AND the
    restricted command boundary, take a new checkpoint PV-CERT-HARNESS. Never modify PV-CLEAN-WINDOWS.

    SECURITY: no unrestricted admin endpoint, no disabled UAC, no token-filter registry change, no
    automatic logon, no persistent privileged service beyond the reversible JEA endpoint.
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
$psm1   = Join-Path $SourceDir 'PathVeerCertificationJea.psm1'
$psd1   = Join-Path $SourceDir 'PathVeerCertificationJea.psd1'
foreach ($f in @($pssc,$role,$psm1,$psd1)) {
    if (-not (Test-Path $f)) { throw "Required file not found: $f" }
}

# Install the trusted module under Program Files.
$modulePath = Join-Path $env:ProgramFiles 'PathVeerCertificationJea'
$roleDir    = Join-Path $modulePath 'RoleCapabilities'
New-Item -ItemType Directory -Force -Path $roleDir | Out-Null
Copy-Item -Path $psm1 -Destination (Join-Path $modulePath 'PathVeerCertificationJea.psm1') -Force
Copy-Item -Path $psd1 -Destination (Join-Path $modulePath 'PathVeerCertificationJea.psd1') -Force
Copy-Item -Path $role -Destination (Join-Path $roleDir 'PathVeerCertificationRole.psrc') -Force
Copy-Item -Path $pssc -Destination (Join-Path $modulePath 'PathVeer.Certification.pssc') -Force

# Protected transcript directory. SYSTEM + Administrators full control; filtered pvcert: no access.
$transcriptDir = 'C:\ProgramData\PathVeerCertificationJea\Transcripts'
New-Item -ItemType Directory -Force -Path $transcriptDir | Out-Null
$acl = Get-Acl -Path $transcriptDir
$acl.SetAccessRuleProtection($true, $false)   # disable inheritance; remove inherited entries
$acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
$sysSid = [System.Security.Principal.SecurityIdentifier]'S-1-5-18'      # SYSTEM
$admSid = [System.Security.Principal.SecurityIdentifier]'S-1-5-32-544'  # BUILTIN\Administrators
$adminGroup = [System.Security.Principal.NTAccount]'BUILTIN\Administrators'
$full = [System.Security.AccessControl.FileSystemRights]::FullControl
$inherit = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
$propagate = [System.Security.AccessControl.PropagationFlags]::None
$acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sysSid, $full, $inherit, $propagate, 'Allow')))
$acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($adminGroup, $full, $inherit, $propagate, 'Allow')))
Set-Acl -Path $transcriptDir -AclObject $acl
Write-Host "Protected transcript directory created: $transcriptDir (SYSTEM + Administrators only)." -ForegroundColor Cyan

# Register the endpoint (genuine admin action, performed by the operator here).
$configName = 'PathVeer.Certification'
$existing = Get-PSSessionConfiguration -Name $configName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Endpoint '$configName' already registered; re-registering." -ForegroundColor Yellow
    Unregister-PSSessionConfiguration -Name $configName -Force -ErrorAction Stop
}
Register-PSSessionConfiguration -Path $pssc -Name $configName -Force -ErrorAction Stop

Write-Host "Registered JEA endpoint '$configName' (role limited to PV-CERT\pvcert)." -ForegroundColor Green
Write-Host "Next: from the HOST run Test-PathVeerCertGuestJea.ps1 to prove privileged context AND restricted boundary." -ForegroundColor Cyan
