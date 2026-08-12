<#
.SYNOPSIS
    Minimal, NON-destructive proof of the PathVeer certification JEA control plane.

.DESCRIPTION
    Exercises the EXACT shared bridge (New-GuestJeaSession / Invoke-GuestJeaElevated from
    PathVeer.Certification.Jea.ps1) used by every privileged gate (GATE-5/2/4/6/8/22/28).
    Performs NO install, NO route mutation, NO service mutation, NO checkpoint restore.

    The operator authenticates as PV-CERT\pvcert via the native local credential prompt
    (PowerShell Direct). The script then:
      * captures the PARENT (filtered PowerShell Direct session) identity + admin role;
      * connects to the JEA endpoint 'PathVeer.Certification' with the SAME credential;
      * runs a harmless identity + admin-role check inside the JEA session;
      * reports whether the JEA session obtained a genuinely elevated administrative token
        (virtual account in BUILTIN\Administrators) WITHOUT the parent ever being elevated.

    Prerequisites (operator-performed in the guest, once):
      - Enable-PathVeerCertificationJea.ps1 (elevated) has registered the endpoint.

    Required outcome for the control plane to be accepted:
        parentIsAdministrator = False
        jeaIsAdministrator    = True
        completed             = True
#>
[CmdletBinding()]
param(
    [string]$VmName = 'PathVeer-Certification',
    [string]$GuestUser = 'PV-CERT\pvcert',
    [string]$ConfigurationName = 'PathVeer.Certification',
    [string]$ResultDir = 'C:\pv-cert'
)

$ErrorActionPreference = 'Stop'

# --- operator credential via native local prompt (never printed/stored) ---
# Authoritative local-admin identity for this certification VM is the domain-qualified
# local account 'PV-CERT\pvcert'. Provided via -UserName so the operator only enters the password.
$cred = Get-Credential -UserName $GuestUser -Message "Enter the certification guest ($GuestUser) password for PowerShell Direct"

# --- load the EXACT shared bridge used by the gates ---
$jeaModule = Join-Path $PSScriptRoot 'PathVeer.Certification.Jea.ps1'
if (-not (Test-Path $jeaModule)) { throw "Shared JEA module not found: $jeaModule" }
. $jeaModule

# --- establish sessions ---
Write-Host 'Connecting to guest via PowerShell Direct (filtered parent)...' -ForegroundColor Cyan
$session = $null; $jeaSession = $null
try {
    $session = New-PSSession -VMName $VmName -Credential $cred -ErrorAction Stop
} catch {
    throw "PowerShell Direct connection failed: $($_.Exception.Message)"
}

try {
    # Parent identity (the non-elevated PowerShell Direct session).
    $parent = Invoke-Command -Session $session -ScriptBlock {
        $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $wp = New-Object System.Security.Principal.WindowsPrincipal($id)
        [PSCustomObject]@{
            user = $id.Name
            isAdministrator = $wp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
        }
    }

    # Connect to the JEA certification endpoint (same credential; virtual-account execution).
    Write-Host "Connecting to JEA endpoint '$ConfigurationName'..." -ForegroundColor Cyan
    $jeaSession = New-GuestJeaSession -Cred $cred -VMName $VmName -ConfigurationName $ConfigurationName
    if (-not $jeaSession) {
        $report = [PSCustomObject]@{
            parentUser = $parent.user
            parentIsAdministrator = $parent.isAdministrator
            jeaUser = $null
            jeaIsAdministrator = $false
            configurationName = $ConfigurationName
            completed = $false
            elevationAvailable = $false
            elevationSucceeded = $false
            error = "JEA endpoint '$ConfigurationName' unavailable. Register it in the guest via Enable-PathVeerCertificationJea.ps1 (run elevated)."
        }
        $outPath = Join-Path $PWD 'artifacts/certification/jea-probe.json'
        if (-not (Test-Path (Split-Path $outPath))) { New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null }
        $report | ConvertTo-Json -Depth 6 | Set-Content -Path $outPath -Encoding utf8
        Write-Host ''
        Write-Host '=== JEA PROBE RESULT ===' -ForegroundColor White
        $report | Format-List | Out-String | Write-Host
        Write-Host "Saved: $outPath" -ForegroundColor DarkGray
        Write-Host 'JEA CONTROL PLANE FAIL -- endpoint not registered in guest.' -ForegroundColor Red
        exit 1
    }

    # Harmless identity/admin-role check inside the JEA session (no privileged product action).
    $jea = Invoke-Command -Session $jeaSession -ScriptBlock {
        $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $wp = New-Object System.Security.Principal.WindowsPrincipal($id)
        [PSCustomObject]@{
            user = $id.Name
            isAdministrator = $wp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
        }
    }

    $report = [PSCustomObject]@{
        parentUser = $parent.user
        parentIsAdministrator = $parent.isAdministrator
        jeaUser = $jea.user
        jeaIsAdministrator = $jea.isAdministrator
        configurationName = $ConfigurationName
        completed = $true
        elevationAvailable = $jea.isAdministrator
        elevationSucceeded = $jea.isAdministrator
        error = $null
    }

    $outPath = Join-Path $PWD 'artifacts/certification/jea-probe.json'
    if (-not (Test-Path (Split-Path $outPath))) { New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null }
    $report | ConvertTo-Json -Depth 6 | Set-Content -Path $outPath -Encoding utf8

    Write-Host ''
    Write-Host '=== JEA PROBE RESULT ===' -ForegroundColor White
    $report | Format-List | Out-String | Write-Host
    Write-Host "Saved: $outPath" -ForegroundColor DarkGray

    $pass = ($parent.isAdministrator -eq $false) -and ($jea.isAdministrator -eq $true)
    if ($pass) {
        Write-Host 'JEA CONTROL PLANE PASS' -ForegroundColor Green
        exit 0
    } else {
        Write-Host 'JEA CONTROL PLANE FAIL -- privileged context not established.' -ForegroundColor Red
        exit 1
    }
} finally {
    if ($jeaSession) { Remove-PSSession -Session $jeaSession -ErrorAction SilentlyContinue }
    if ($session) { Remove-PSSession -Session $session -ErrorAction SilentlyContinue }
}
