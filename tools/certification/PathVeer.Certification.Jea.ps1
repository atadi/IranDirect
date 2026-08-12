<#
.SYNOPSIS
    PathVeer certification JEA control-plane bridge (replaces the retired Scheduled-Task
    elevation primitive).

    SINGLE SOURCE OF TRUTH, dot-sourced by:
      - Invoke-PathVeerCertification.ps1 (the gates)
      - Test-PathVeerCertGuestJea.ps1 (the harmless operator probe)

.DESCRIPTION
    The hypervisor PowerShell Direct session for PV-CERT\pvcert carries a UAC-FILTERED
    (non-administrator) token. Windows denies any attempt to bootstrap elevation from it
    (real-VM evidence: "Register-ScheduledTask RunLevel Highest -> Access is denied").

    Instead, the operator registers a narrow JEA endpoint 'PathVeer.Certification' ONCE
    with genuine elevation inside the guest (Enable-PathVeerCertificationJea.ps1). The
    harness then connects to that endpoint over PowerShell Direct. The endpoint runs the
    command as a per-connection virtual account (BUILTIN\Administrators) — full privileged
    execution WITHOUT the filtered parent ever holding an elevated token and WITHOUT any
    stored/passed password.

    These helpers establish the privileged JEA session lazily and run commands through it.
    If the endpoint is absent, they return a structured HARNESS/ENVIRONMENT failure and
    NEVER execute the privileged operation on the filtered session.
#>

function New-GuestJeaSession([System.Management.Automation.PSCredential]$Cred) {
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
    Run a command line elevated through the JEA certification endpoint.
#>
function Invoke-GuestJeaElevated {
    [CmdletBinding()]
    param(
        [System.Management.Automation.Runspaces.PSSession]$Session,   # normal filtered PS Direct session (for diagnostics only)
        [System.Management.Automation.Runspaces.PSSession]$JeaSession, # privileged JEA session (must be provided)
        [System.Management.Automation.PSCredential]$Cred,
        [string]$Command,                                              # command line to run elevated
        [string]$ResultDir = 'C:\pv-cert',
        [int]$TimeoutSeconds = 900
    )
    $out = [PSCustomObject]@{
        started=$false; completed=$false; exitCode=$null
        childUser=$null; childIsAdministrator=$false; elevationSucceeded=$false
        elevationAvailable=$false; error=$null; logContent=$null
    }
    if (-not $JeaSession) {
        $out.error = 'JEA certification session unavailable (endpoint PathVeer.Certification not registered). Run Enable-PathVeerCertificationJea.ps1 in the guest.'
        return $out
    }
    $marker     = [guid]::NewGuid().ToString('N')
    $log        = "$ResultDir\jea-$marker.log"
    $resultJson = "$ResultDir\jea-$marker.result.json"
    $out.started = $true
    try {
        $res = Invoke-Command -Session $JeaSession -ScriptBlock {
            param($cmd,$log,$resultJson,$timeout)
            $r = [PSCustomObject]@{
                completed=$false; exitCode=$null
                childUser=$null; childIsAdministrator=$false; elevationSucceeded=$false; error=$null
            }
            try {
                $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
                $wp = New-Object System.Security.Principal.WindowsPrincipal($id)
                $r.childUser = $id.Name
                $r.childIsAdministrator = $wp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
                & ([scriptblock]::Create($cmd)) *> $log
                $r.exitCode = $LASTEXITCODE
                $r.completed = $true
                $r.elevationSucceeded = $r.childIsAdministrator
            } catch {
                $r.error = $_.Exception.Message
            }
            $r | ConvertTo-Json -Depth 4 | Set-Content -Path $resultJson -Encoding utf8
            return $r
        } -ArgumentList $Command,$log,$resultJson,$TimeoutSeconds
        if ($res) {
            $out.completed=$res.completed; $out.exitCode=$res.exitCode
            $out.childUser=$res.childUser; $out.childIsAdministrator=$res.childIsAdministrator
            $out.elevationSucceeded=$res.elevationSucceeded; $out.error=$res.error
        }
        $out.elevationAvailable = ($out.elevationSucceeded -eq $true)
    } catch {
        $out.error = $_.Exception.Message
    }
    return $out
}

<#
.SYNOPSIS
    Run an entire guest SCRIPTBLOCK elevated through the JEA certification endpoint and
    read back a structured result JSON the script writes to a file.
#>
function Invoke-GuestJeaScriptElevated {
    [CmdletBinding()]
    param(
        [System.Management.Automation.Runspaces.PSSession]$Session,
        [System.Management.Automation.Runspaces.PSSession]$JeaSession,
        [System.Management.Automation.PSCredential]$Cred,
        [scriptblock]$ScriptBlock,
        [hashtable]$ArgumentList = @{},
        [string]$ResultDir = 'C:\pv-cert',
        [int]$TimeoutSeconds = 900
    )
    if (-not $JeaSession) {
        return [PSCustomObject]@{
            elevation = [PSCustomObject]@{ elevationAvailable=$false; error='JEA certification session unavailable (endpoint PathVeer.Certification not registered).' }
            result = $null
        }
    }
    $marker     = [guid]::NewGuid().ToString('N')
    $scriptFile = "$ResultDir\jea-script-$marker.ps1"
    $argJson    = "$ResultDir\jea-script-$marker.args.json"
    $resultJson = "$ResultDir\jea-script-$marker.result.json"

    $guestScript = @'
$argsIn = Get-Content '__ARGJSON__' -Raw | ConvertFrom-Json -AsHashtable
$result = & {
    param($a)
__BODY__
} $argsIn
$result | ConvertTo-Json -Depth 8 | Set-Content -Path '__RESULTJSON__' -Encoding utf8
'@
    $bodyText    = $ScriptBlock.ToString()
    $guestScript = $guestScript.Replace('__ARGJSON__', $argJson).Replace('__RESULTJSON__', $resultJson).Replace('__BODY__', $bodyText)

    Invoke-Command -Session $JeaSession -ScriptBlock {
        param($d,$f,$c,$aj,$a)
        if (-not (Test-Path -LiteralPath $d -PathType Container)) { New-Item -ItemType Directory -Path $d -Force -ErrorAction Stop | Out-Null }
        Set-Content -Path $f -Value $c -Encoding UTF8
        $a | ConvertTo-Json -Depth 8 | Set-Content -Path $aj -Encoding utf8
    } -ArgumentList $ResultDir,$scriptFile,$guestScript,$argJson,$ArgumentList | Out-Null

    $run = Invoke-GuestJeaElevated -Session $Session -JeaSession $JeaSession -Cred $Cred -Command "powershell.exe -File '$scriptFile'" -ResultDir $ResultDir -TimeoutSeconds $TimeoutSeconds

    $rb = Invoke-Command -Session $JeaSession -ScriptBlock {
        param($rj)
        if (Test-Path $rj) { try { return Get-Content $rj -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json } catch {} }
    } -ArgumentList $resultJson

    # Ownership-aware cleanup of the temporary files this helper created.
    try { Invoke-Command -Session $JeaSession -ScriptBlock { param($f) Remove-Item $f -Force -ErrorAction SilentlyContinue } -ArgumentList $scriptFile | Out-Null } catch {}
    try { Invoke-Command -Session $JeaSession -ScriptBlock { param($f) Remove-Item $f -Force -ErrorAction SilentlyContinue } -ArgumentList $argJson | Out-Null } catch {}
    try { Invoke-Command -Session $JeaSession -ScriptBlock { param($f) Remove-Item $f -Force -ErrorAction SilentlyContinue } -ArgumentList $resultJson | Out-Null } catch {}

    return [PSCustomObject]@{ elevation = $run; result = $rb }
}
