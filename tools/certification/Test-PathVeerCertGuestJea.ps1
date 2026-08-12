<#
.SYNOPSIS
    Minimal, NON-destructive proof of the PathVeer certification JEA control plane.

.DESCRIPTION
    Exercises the EXACT shared bridge (New-GuestJeaSession from PathVeer.Certification.Jea.ps1)
    used by every privileged gate (GATE-5/2/4/6/8/22/28). Performs NO install, NO route mutation,
    NO service mutation, NO checkpoint restore.

    The operator authenticates as PV-CERT\pvcert via the native local credential prompt
    (PowerShell Direct). The script then:
      * captures the PARENT (filtered PowerShell Direct session) identity + admin role;
      * connects to the JEA endpoint 'PathVeer.Certification' with the SAME credential;
      * runs a harmless identity + admin-role check inside the JEA session;
      * PROVES the restricted boundary: commands that MUST NOT be available in the JEA
        session fail Get-Command / capability inspection (powershell.exe, Start-Process,
        Invoke-Expression, Invoke-Command, New-ScheduledTask, Set-Content, etc.).

    Required outcome for the control plane to be accepted (BOTH conditions):
        parentIsAdministrator = False
        jeaIsAdministrator    = True
        restrictedBoundaryOk  = True   (forbidden arbitrary-execution surface absent)

    Prerequisites (operator-performed in the guest, once):
      - Enable-PathVeerCertificationJea.ps1 (elevated) has registered the endpoint.
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
$cred = Get-Credential -UserName $GuestUser -Message "Enter the certification guest ($GuestUser) password for PowerShell Direct"

# --- load the EXACT shared bridge used by the gates ---
$jeaModule = Join-Path $PSScriptRoot 'PathVeer.Certification.Jea.ps1'
if (-not (Test-Path $jeaModule)) { throw "Shared JEA module not found: $jeaModule" }
. $jeaModule

# Forbidden arbitrary-execution / broad-write primitives that must NOT be exposed.
$forbiddenCommands = @(
    'powershell.exe', 'cmd.exe', 'pwsh.exe', 'wscript.exe', 'cscript.exe',
    'Start-Process', 'Invoke-Expression', 'Invoke-Command', 'Invoke-WebRequest',
    'New-ScheduledTask', 'Register-ScheduledTask', 'Set-Content', 'Set-Item',
    'New-Item', 'Invoke-Item', 'Get-CimInstance'
)

Write-Host 'Connecting to guest via PowerShell Direct (filtered parent)...' -ForegroundColor Cyan
$session = $null; $jeaSession = $null
try {
    $session = New-PSSession -VMName $VmName -Credential $cred -ErrorAction Stop

    # Parent identity (the non-elevated PowerShell Direct session).
    $parent = Invoke-Command -Session $session -ScriptBlock {
        $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $wp = New-Object System.Security.Principal.WindowsPrincipal($id)
        [PSCustomObject]@{
            user = $id.Name
            isAdministrator = $wp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
        }
    }

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
            restrictedBoundaryOk = $false
            forbiddenAvailable = @()
            error = "JEA endpoint '$ConfigurationName' unavailable. Register it in the guest via Enable-PathVeerCertificationJea.ps1 (run elevated)."
        }
        Save-Probe $report
        exit 1
    }

    # Harmless identity/admin-role check inside the JEA session (no privileged product action).
    $jea = Invoke-Command -Session $jeaSession -ScriptBlock {
        $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $wp = New-Object System.Security.Principal.WindowsPrincipal($id)
        [PSCustomObject]@{ user = $id.Name; isAdministrator = $wp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator) }
    }

    # Prove the restricted boundary: forbidden commands must be ABSENT from the JEA session.
    $forbiddenAvailable = Invoke-Command -Session $jeaSession -ScriptBlock {
        param($forbidden)
        $found = @()
        foreach ($name in $forbidden) {
            # Compare by base name (executable) or cmdlet name.
            $base = if ($name -like '*.exe') { [System.IO.Path]::GetFileNameWithoutExtension($name) } else { $name }
            if (Get-Command -Name $base -ErrorAction SilentlyContinue) { $found += $name }
        }
        return $found
    } -ArgumentList $forbiddenCommands

    $restrictedBoundaryOk = ($forbiddenAvailable.Count -eq 0)

    $report = [PSCustomObject]@{
        parentUser = $parent.user
        parentIsAdministrator = $parent.isAdministrator
        jeaUser = $jea.user
        jeaIsAdministrator = $jea.isAdministrator
        configurationName = $ConfigurationName
        completed = $true
        elevationAvailable = $jea.isAdministrator
        elevationSucceeded = $jea.isAdministrator
        restrictedBoundaryOk = $restrictedBoundaryOk
        forbiddenAvailable = $forbiddenAvailable
        error = $null
    }

    Save-Probe $report

    $pass = ($parent.isAdministrator -eq $false) -and ($jea.isAdministrator -eq $true) -and $restrictedBoundaryOk
    if ($pass) {
        Write-Host 'JEA CONTROL PLANE PASS (privileged context + restricted boundary)' -ForegroundColor Green
        exit 0
    } else {
        Write-Host 'JEA CONTROL PLANE FAIL' -ForegroundColor Red
        if ($parent.isAdministrator) { Write-Host '  - parent was unexpectedly administrator' -ForegroundColor Red }
        if (-not $jea.isAdministrator) { Write-Host '  - JEA session not genuinely elevated' -ForegroundColor Red }
        if (-not $restrictedBoundaryOk) { Write-Host "  - forbidden commands available: $($forbiddenAvailable -join ', ')" -ForegroundColor Red }
        exit 1
    }
} finally {
    if ($jeaSession) { Remove-PSSession -Session $jeaSession -ErrorAction SilentlyContinue }
    if ($session) { Remove-PSSession -Session $session -ErrorAction SilentlyContinue }
}

function Save-Probe($report) {
    $outPath = Join-Path $PWD 'artifacts/certification/jea-probe.json'
    if (-not (Test-Path (Split-Path $outPath))) { New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null }
    $report | ConvertTo-Json -Depth 6 | Set-Content -Path $outPath -Encoding utf8
    Write-Host ''
    Write-Host '=== JEA PROBE RESULT ===' -ForegroundColor White
    $report | Format-List | Out-String | Write-Host
    Write-Host "Saved: $outPath" -ForegroundColor DarkGray
}
