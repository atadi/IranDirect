<#
.SYNOPSIS
    Minimal, NON-destructive proof of the PathVeer certification elevation primitive.

    Exercises the EXACT shared helper (Invoke-GuestElevated from
    PathVeer.Certification.Elevation.ps1) used by every privileged gate (GATE-5/4/6/8/28/2/22).
    Performs NO install, NO route mutation, NO service mutation, NO checkpoint restore.

    The operator authenticates as 'pvcert' via the native local credential prompt
    (PowerShell Direct). The script then:
      * captures the PARENT (PowerShell Direct session) identity + admin role;
      * elevates a trivial harmless child via the shared helper (runs whoami/whoami /groups
        and an admin-role test, writes structured evidence);
      * reports whether the child obtained a genuinely elevated administrative token.

    Required outcome for the primitive to be accepted:
        parentIsAdministrator = False
        childIsAdministrator  = True
        childExitCode         = 0
#>
[CmdletBinding()]
param(
    [string]$VmName = 'PathVeer-Certification',
    [string]$ResultDir = 'C:\pv-cert'
)

$ErrorActionPreference = 'Stop'

# --- operator credential via native local prompt (never printed/stored) ---
$cred = Get-Credential -Message 'Enter the certification guest (pvcert) credential for PowerShell Direct'

# --- load the EXACT shared primitive used by the gates ---
$elevModule = Join-Path $PSScriptRoot 'PathVeer.Certification.Elevation.ps1'
if (-not (Test-Path $elevModule)) { throw "Shared elevation module not found: $elevModule" }
. $elevModule

# --- establish session ---
Write-Host 'Connecting to guest via PowerShell Direct...' -ForegroundColor Cyan
$session = $null
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

    # Elevate a harmless child via the SAME helper the gates use.
    # The command ONLY captures identity + admin role; no privileged product action.
    $childCmd = "powershell.exe -NoProfile -Command `"& { (whoami); (whoami /groups | Select-String 'S-1-5-32-544'); `$id=[System.Security.Principal.WindowsIdentity]::GetCurrent(); `$wp=New-Object System.Security.Principal.WindowsPrincipal(`$id); [PSCustomObject]@{ user=`$id.Name; isAdministrator=`$wp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator) } | ConvertTo-Json }`""

    Write-Host 'Elevating harmless child via shared helper (RunLevel Highest scheduled task)...' -ForegroundColor Cyan
    $elev = Invoke-GuestElevated -Session $session -Cred $cred -Command $childCmd -ResultDir $ResultDir

    $report = [PSCustomObject]@{
        parentUser             = $parent.user
        parentIsAdministrator  = $parent.isAdministrator
        childUser              = $elev.childUser
        childIsAdministrator   = $elev.childIsAdministrator
        childCompleted         = $elev.completed
        childExitCode          = $elev.exitCode
        elevationAvailable     = $elev.elevationAvailable
        elevationSucceeded     = $elev.elevationSucceeded
        error                  = $elev.error
        childLog               = ($elev.logContent | Out-String).Trim()
    }

    $outPath = Join-Path $PWD 'artifacts/certification/elevation-probe.json'
    if (-not (Test-Path (Split-Path $outPath))) { New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null }
    $report | ConvertTo-Json -Depth 6 | Set-Content -Path $outPath -Encoding utf8

    Write-Host ''
    Write-Host '=== ELEVATION PROBE RESULT ===' -ForegroundColor White
    $report | Format-List | Out-String | Write-Host
    Write-Host "Saved: $outPath" -ForegroundColor DarkGray

    $primitivePass = ($parent.isAdministrator -eq $false) -and ($elev.childIsAdministrator -eq $true) -and ($elev.exitCode -eq 0)
    if ($primitivePass) {
        Write-Host 'ELEVATION PRIMITIVE PASS' -ForegroundColor Green
        exit 0
    } else {
        Write-Host 'ELEVATION PRIMITIVE FAIL -- elevation primitive is NOT ready for GATE-5.' -ForegroundColor Red
        exit 1
    }
} finally {
    if ($session) { Remove-PSSession -Session $session -ErrorAction SilentlyContinue }
}
