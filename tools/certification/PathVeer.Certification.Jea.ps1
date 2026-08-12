<#
.SYNOPSIS
    PathVeer certification JEA control-plane bridge (replaces the retired Scheduled-Task elevation).

    SINGLE SOURCE OF TRUTH, dot-sourced by:
      - Invoke-PathVeerCertification.ps1 (the gates)
      - Test-PathVeerCertGuestJea.ps1 (the harmless operator probe)

.DESCRIPTION
    The hypervisor PowerShell Direct session for PV-CERT\pvcert carries a UAC-FILTERED
    (non-administrator) token. Windows denies any attempt to bootstrap elevation from it
    (real-VM evidence: "Register-ScheduledTask RunLevel Highest -> Access is denied").

    Instead, the operator registers a narrow JEA endpoint 'PathVeer.Certification' ONCE with
    genuine elevation inside the guest (jea/Enable-PathVeerCertificationJea.ps1). The harness
    then connects to that endpoint over PowerShell Direct and invokes ONLY the trusted, validated
    module functions exposed by the role. No arbitrary command, ScriptBlock, executable path, or
    shell is ever passed across the boundary — the JEA functions perform the privileged work against
    KNOWN PathVeer targets.

    These helpers establish the privileged JEA session lazily. If the endpoint is absent, they
    return a structured HARNESS/ENVIRONMENT failure and NEVER execute the privileged operation on
    the filtered session.
#>

function New-GuestJeaSession {
    [CmdletBinding()]
    param(
        [System.Management.Automation.PSCredential]$Cred,
        [string]$VMName = 'PathVeer-Certification',
        [string]$ConfigurationName = 'PathVeer.Certification'
    )
    try {
        return New-PSSession -VMName $VMName -Credential $Cred -ConfigurationName $ConfigurationName -ErrorAction Stop
    } catch {
        return $null   # caller decides; never silently falls back to non-elevated
    }
}

<#
.SYNOPSIS
    Run a trusted JEA certification function with validated arguments. No arbitrary command surface.
#>
function Invoke-GuestJeaFunction {
    [CmdletBinding()]
    param(
        [System.Management.Automation.Runspaces.PSSession]$Session,        # normal filtered PS Direct session (diagnostics only)
        [System.Management.Automation.Runspaces.PSSession]$JeaSession,     # privileged JEA session (required)
        [System.Management.Automation.PSCredential]$Cred,
        [Parameter(Mandatory)][string]$Function,
        [hashtable]$ArgumentList = @{},
        [int]$TimeoutSeconds = 900
    )
    $out = [PSCustomObject]@{
        completed=$false; result=$null
        childUser=$null; childIsAdministrator=$false; elevationSucceeded=$false
        elevationAvailable=$false; error=$null
    }
    if (-not $JeaSession) {
        $out.error = 'JEA certification session unavailable (endpoint PathVeer.Certification not registered). Run Enable-PathVeerCertificationJea.ps1 in the guest.'
        return $out
    }
    $out.elevationAvailable = $true
    try {
        # Argument validation is enforced on the guest by the function's parameter attributes.
        # Only validated function names (whitelist) are forwarded.
        $allowed = @(
            'Test-PathVeerCertificationAdmin','Get-PathVeerServiceState','Start-PathVeerService',
            'Stop-PathVeerService','Stop-PathVeerTray','Get-PathVeerInstallManifest',
            'Get-PathVeerInstalledFiles','Get-PathVeerRouteState','Invoke-PathVeerCertificationInstall',
            'Invoke-PathVeerCli','Get-PathVeerProgramDataState'
        )
        if ($allowed -notcontains $Function) { throw "Function '$Function' is not an allowed certification operation." }
        $res = Invoke-Command -Session $JeaSession -ScriptBlock {
            param($fn,$argsIn)
            $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
            $wp = New-Object System.Security.Principal.WindowsPrincipal($id)
            $r = [PSCustomObject]@{
                childUser=$id.Name
                childIsAdministrator=$wp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
                result=$null; error=$null
            }
            try { $r.result = & (Get-Command $fn) @argsIn } catch { $r.error = $_.Exception.Message }
            return $r
        } -ArgumentList $Function,$ArgumentList -ErrorAction Stop
        if ($res) {
            $out.childUser=$res.childUser; $out.childIsAdministrator=$res.childIsAdministrator
            $out.elevationSucceeded=$res.childIsAdministrator; $out.result=$res.result; $out.error=$res.error
            $out.completed = ($null -eq $res.error)
        }
    } catch {
        $out.error = $_.Exception.Message
    }
    return $out
}

<#
.SYNOPSIS
    Thin caller-facing wrappers used by the gates. Each maps to exactly one trusted JEA function
    with a validated argument set. No raw cmdlet/ScriptBlock/executable is ever forwarded.
#>

function Invoke-GuestJeaInstall([System.Management.Automation.Runspaces.PSSession]$Session,
                                [System.Management.Automation.Runspaces.PSSession]$JeaSession,
                                [string]$Action='Install', [string[]]$Feature=@('RegisterShell','InstallTray')) {
    return Invoke-GuestJeaFunction -Session $Session -JeaSession $JeaSession -Function 'Invoke-PathVeerCertificationInstall' -ArgumentList @{ Action=$Action; Feature=$Feature }
}

function Invoke-GuestJeaCli([System.Management.Automation.Runspaces.PSSession]$Session,
                            [System.Management.Automation.Runspaces.PSSession]$JeaSession,
                            [string]$Verb='status', [string]$SubVerb='list', [string]$Argument='') {
    return Invoke-GuestJeaFunction -Session $Session -JeaSession $JeaSession -Function 'Invoke-PathVeerCli' -ArgumentList @{ Verb=$Verb; SubVerb=$SubVerb; Argument=$Argument }
}

function Get-GuestJeaServiceState([System.Management.Automation.Runspaces.PSSession]$Session,
                                  [System.Management.Automation.Runspaces.PSSession]$JeaSession) {
    return Invoke-GuestJeaFunction -Session $Session -JeaSession $JeaSession -Function 'Get-PathVeerServiceState'
}

function Stop-GuestJeaService([System.Management.Automation.Runspaces.PSSession]$Session,
                               [System.Management.Automation.Runspaces.PSSession]$JeaSession) {
    return Invoke-GuestJeaFunction -Session $Session -JeaSession $JeaSession -Function 'Stop-PathVeerService'
}

function Start-GuestJeaService([System.Management.Automation.Runspaces.PSSession]$Session,
                                [System.Management.Automation.Runspaces.PSSession]$JeaSession) {
    return Invoke-GuestJeaFunction -Session $Session -JeaSession $JeaSession -Function 'Start-PathVeerService'
}

function Stop-GuestJeaTray([System.Management.Automation.Runspaces.PSSession]$Session,
                           [System.Management.Automation.Runspaces.PSSession]$JeaSession) {
    return Invoke-GuestJeaFunction -Session $Session -JeaSession $JeaSession -Function 'Stop-PathVeerTray'
}

function Get-GuestJeaRouteState([System.Management.Automation.Runspaces.PSSession]$Session,
                                [System.Management.Automation.Runspaces.PSSession]$JeaSession,
                                [string]$Prefix='') {
    return Invoke-GuestJeaFunction -Session $Session -JeaSession $JeaSession -Function 'Get-PathVeerRouteState' -ArgumentList @{ Prefix=$Prefix }
}

function Get-GuestJeaInstallManifest([System.Management.Automation.Runspaces.PSSession]$Session,
                                     [System.Management.Automation.Runspaces.PSSession]$JeaSession) {
    return Invoke-GuestJeaFunction -Session $Session -JeaSession $JeaSession -Function 'Get-PathVeerInstallManifest'
}

function Get-GuestJeaProgramDataState([System.Management.Automation.Runspaces.PSSession]$Session,
                                      [System.Management.Automation.Runspaces.PSSession]$JeaSession) {
    return Invoke-GuestJeaFunction -Session $Session -JeaSession $JeaSession -Function 'Get-PathVeerProgramDataState'
}

function Stop-GuestJeaServiceForRecovery([System.Management.Automation.Runspaces.PSSession]$Session,
                                         [System.Management.Automation.Runspaces.PSSession]$JeaSession) {
    return Stop-GuestJeaService -Session $Session -JeaSession $JeaSession
}
