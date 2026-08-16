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
        # NoLanguage-safe invocation: build a LITERAL trusted-function call (function name + validated
        # arguments inlined as literals). The RestrictedRemoteServer / NoLanguage JEA caller forbids the
        # call operator (&), splatting (@), subexpressions ($()), and dynamic invocation -- so we emit a
        # bare command with fixed literal arguments, exactly the pattern the control-plane probe proved
        # works. Every interpolated value is validated against a fixed set (never a caller-supplied path),
        # so this remains a narrow API, not arbitrary execution.
        $sbText = switch ($Function) {
            'Test-PathVeerCertificationAdmin'    { 'Test-PathVeerCertificationAdmin' }
            'Get-PathVeerServiceState'          { 'Get-PathVeerServiceState' }
            'Start-PathVeerService'             { 'Start-PathVeerService' }
            'Stop-PathVeerService'              { 'Stop-PathVeerService' }
            'Stop-PathVeerTray'                 { 'Stop-PathVeerTray' }
            'Get-PathVeerInstallManifest'       { 'Get-PathVeerInstallManifest' }
            'Get-PathVeerInstalledFiles'        { 'Get-PathVeerInstalledFiles' }
            'Get-PathVeerProgramDataState'      { 'Get-PathVeerProgramDataState' }
            'Get-PathVeerCertificationBoundary' { 'Get-PathVeerCertificationBoundary' }
            'Test-PathVeerCertificationHarnessBaseline' { 'Test-PathVeerCertificationHarnessBaseline' }
            'Get-PathVeerRouteState'            {
                $p = [string]($ArgumentList['Prefix'] ?? '')
                if ($p -notmatch '^[0-9./a-fA-F:]{0,45}$') { throw 'Invalid Prefix argument.' }
                "Get-PathVeerRouteState -Prefix '$p'"
            }
            'Invoke-PathVeerCertificationInstall' {
                $a = [string]($ArgumentList['Action'] ?? 'Install')
                $f = [array]($ArgumentList['Feature'] ?? @('RegisterShell','InstallTray'))
                $validActions = @('Install','Upgrade','Repair','Uninstall','PurgeUninstall')
                if ($validActions -notcontains $a) { throw 'Invalid Action argument.' }
                foreach ($x in $f) { if ($x -ne 'RegisterShell' -and $x -ne 'InstallTray') { throw 'Invalid Feature argument.' } }
                # Emit ONE -Feature binding with a comma-joined, validated, quoted literal. The comma is
                # inside the quoted string (data), so no array/comma-operator syntax crosses the NoLanguage
                # boundary. The trusted module splits + validates the value internally.
                $feat = ($f | ForEach-Object { $_ }) -join ','
                "Invoke-PathVeerCertificationInstall -Action '$a' -Feature '$feat'"
            }
            'Invoke-PathVeerCli'                 {
                $v = [string]($ArgumentList['Verb'] ?? 'status')
                $sv = [string]($ArgumentList['SubVerb'] ?? 'list')
                $arg = [string]($ArgumentList['Argument'] ?? '')
                $validVerbs = @('status','repair','doctor','enable','disable','custom-routes')
                $validSub = @('list','add-domain','add-ip','add-cidr','enable','disable','remove','resolve','status','invalidate','invalidate-all')
                if ($validVerbs -notcontains $v) { throw 'Invalid Verb argument.' }
                if ($validSub -notcontains $sv) { throw 'Invalid SubVerb argument.' }
                if ($arg -notmatch '^[\d./\sA-Za-z0-9-]{0,120}$') { throw 'Invalid Argument.' }
                "Invoke-PathVeerCli -Verb '$v' -SubVerb '$sv' -Argument '$arg'"
            }
            default { throw "Function '$Function' is not an allowed certification operation." }
        }
        $sb = [scriptblock]::Create($sbText)
        $res = Invoke-Command -Session $JeaSession -ScriptBlock $sb -ErrorAction Stop
        if ($res) {
            # Child identity is reported by the trusted function (captured in the JEA virtual-account
            # context). A null/blank identity means the operation never reached identity capture.
            if ($res.PSObject.Properties.Match('childUser').Count) { $out.childUser = $res.childUser }
            if ($res.PSObject.Properties.Match('childIsAdministrator').Count) { $out.childIsAdministrator = $res.childIsAdministrator }
            $out.elevationSucceeded = ($out.childIsAdministrator -eq $true)
            $out.result = $res
            $out.error = if ($res.PSObject.Properties.Match('error').Count) { $res.error } else { $null }
            $out.completed = ($null -eq $out.error)
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

function Get-GuestJeaHarnessBaseline([System.Management.Automation.Runspaces.PSSession]$Session,
                                     [System.Management.Automation.Runspaces.PSSession]$JeaSession) {
    return Invoke-GuestJeaFunction -Session $Session -JeaSession $JeaSession -Function 'Test-PathVeerCertificationHarnessBaseline'
}
