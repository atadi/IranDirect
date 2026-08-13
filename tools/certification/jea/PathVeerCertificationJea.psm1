# PathVeer certification JEA trusted module.
#
# This module is installed ONLY inside the guest certification control plane
# (C:\Program Files\PathVeerCertificationJea) and is exposed through the narrow
# 'PathVeerCertificationRole' role capability. Every function here is CALLER-FACING but
# strictly validated: no arbitrary command, ScriptBlock, executable path, filesystem path,
# registry path, or service name may be supplied by the connecting user. The module may use
# privileged PowerShell internally, but only against KNOWN PathVeer certification targets.
#
# TRUST BOUNDARY (corrected): the privileged installer script and the package payload are
# executed ONLY from the PROTECTED certification tree
#   C:\ProgramData\PathVeerCertificationJea\Trusted     (installer script)
#   C:\ProgramData\PathVeerCertificationJea\Payloads    (package payload)
# These directories are created by the operator-elevated bootstrap with ACLs that DENY the
# ordinary/filtered PV-CERT\pvcert account write access. The untrusted incoming staging area
#   C:\pv-cert\incoming
# is pvcert-writable but is NEVER executed by privileged code. The bootstrap performs the
# one-way promotion (copy + ACL) from incoming -> protected; no JEA function promotes.

$script:BaseDir         = Join-Path $env:ProgramData 'PathVeerCertificationJea'
$script:TrustedDir      = Join-Path $script:BaseDir 'Trusted'
$script:PayloadDir      = Join-Path $script:BaseDir 'Payloads'
# PROTECTED, operator-promoted copies — never caller-supplied, never from C:\pv-cert at runtime.
$script:InstallScript   = Join-Path $script:TrustedDir 'Install-PathVeer.ps1'
$script:InstallPackage  = Join-Path $script:PayloadDir 'PathVeer-1.0.0-beta.1'
$script:CliExe          = 'C:\Program Files\PathVeer\Cli\PathVeer.Cli.exe'
$script:ServiceName     = 'PathVeer'
$script:InstallRoot     = 'C:\Program Files\PathVeer'
$script:StateRoot       = Join-Path $env:ProgramData 'PathVeer'
# C:\pv-cert is explicitly UNTRUSTED incoming staging. It is referenced here ONLY to explain
# that it must NOT be executed; no executable content is ever read from it by privileged code.
$script:UntrustedIncoming = 'C:\pv-cert\incoming'

function Test-PathVeerCertificationAdmin {
    <#
    .SYNOPSIS
        Harmless capability probe: report the JEA session identity + admin role. No product action.
    #>
    [CmdletBinding()]
    param()
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $wp = New-Object System.Security.Principal.WindowsPrincipal($id)
    [PSCustomObject]@{
        user = $id.Name
        isAdministrator = $wp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
        configurationName = 'PathVeer.Certification'
    }
}

function Get-PathVeerServiceState {
    [CmdletBinding()]
    param()
    $svc = Get-Service -Name $script:ServiceName -ErrorAction SilentlyContinue
    if (-not $svc) { return [PSCustomObject]@{ exists=$false; status='absent'; startType=$null } }
    $bin = (Get-CimInstance Win32_Service -Filter "Name='$($script:ServiceName)'" -ErrorAction SilentlyContinue).StartMode
    [PSCustomObject]@{
        exists = $true
        status = $svc.Status
        startType = $bin
        name = $svc.Name
    }
}

function Start-PathVeerService {
    [CmdletBinding()]
    param()
    Start-Service -Name $script:ServiceName -ErrorAction Stop
    return Get-PathVeerServiceState
}

function Stop-PathVeerService {
    [CmdletBinding()]
    param()
    Stop-Service -Name $script:ServiceName -Force -ErrorAction Stop
    return Get-PathVeerServiceState
}

function Stop-PathVeerTray {
    [CmdletBinding()]
    param()
    $proc = Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    [PSCustomObject]@{ trayWasRunning = ($null -ne $proc); trayRunningAfter = ($null -ne (Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue)) }
}

function Get-PathVeerInstallManifest {
    [CmdletBinding()]
    param()
    $p = Join-Path $script:InstallRoot 'install-manifest.json'
    if (-not (Test-Path -LiteralPath $p)) { return $null }
    try { return (Get-Content -LiteralPath $p -Raw -ErrorAction Stop | ConvertFrom-Json) } catch { return $null }
}

function Get-PathVeerInstalledFiles {
    [CmdletBinding()]
    param()
    if (-not (Test-Path -LiteralPath $script:InstallRoot)) { return @() }
    return @(Get-ChildItem -LiteralPath $script:InstallRoot -Recurse -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
}

function Get-PathVeerRouteState {
    [CmdletBinding()]
    param(
        [ValidatePattern('^[\d./]{0,40}$')]
        [string]$Prefix = ''
    )
    if ($Prefix) {
        return @(Get-NetRoute -DestinationPrefix $Prefix -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop, InterfaceIndex, RouteMetric)
    }
    return @(Get-NetRoute -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop, InterfaceIndex, RouteMetric)
}

<#
.SYNOPSIS
    Trusted installer wrapper. Invokes ONLY the known certification installer at a fixed path,
    with a closed set of supported switches. Never exposes a generic shell.
#>
function Invoke-PathVeerCertificationInstall {
    [CmdletBinding()]
    param(
        [ValidateSet('Install','Upgrade','Repair','Uninstall','PurgeUninstall')]
        [string]$Action = 'Install',

        # Single comma-joined string of allowed features. The Desktop bridge emits exactly ONE -Feature
        # binding with a quoted comma-joined literal (NoLanguage-safe: named param + quoted string, no
        # array/comma-operator syntax). The trusted module splits + validates internally against the
        # fixed set, so no arbitrary feature name or filesystem path can reach the installer.
        [string]$Feature = 'RegisterShell,InstallTray'
    )
    $exitCode = $null; $logFile = $null; $errorMsg = $null
    try {
        if (-not (Test-Path -LiteralPath $script:InstallScript)) {
            throw "Certification installer not staged at $($script:InstallScript). Stage the package first."
        }
        # Build a fixed, validated argument list. No caller-supplied paths/strings reach the process.
        $psiArgs = @('-File', $script:InstallScript)
        switch ($Action) {
            'Install'         { $psiArgs += '-PackageDirectory'; $psiArgs += $script:InstallPackage }
            'Upgrade'         { $psiArgs += '-PackageDirectory'; $psiArgs += $script:InstallPackage }
            'Repair'          { $psiArgs += '-PackageDirectory'; $psiArgs += $script:InstallPackage }
            'Uninstall'       { $psiArgs += '-Action'; $psiArgs += 'uninstall' }
            'PurgeUninstall'  { $psiArgs += '-Action'; $psiArgs += 'uninstall'; $psiArgs += '-PurgeState' }
        }
        # Split + validate the comma-joined feature string against the fixed set (trusted code).
        $featList = @()
        foreach ($tok in ($Feature -split ',')) {
            $t = $tok.Trim()
            if ($t -eq 'RegisterShell' -or $t -eq 'InstallTray') { $featList += $t }
        }
        if ($Action -in @('Install','Upgrade','Repair')) {
            foreach ($f in $featList) { $psiArgs += "-$f" }
        }
        # Use the SAME powershell host that is already running the JEA session (trusted, fixed).
        # This is NOT exposed to the caller as an arbitrary-execution primitive; the path and
        # arguments are entirely fixed/validated here.
        & (Get-Process -Id $pid).Path @psiArgs
        $exitCode = $LASTEXITCODE
    } catch {
        $errorMsg = $_.Exception.Message
    }
    # Capture the JEA virtual-account child identity. This function runs INSIDE the elevated
    # RunAsVirtualAccount context, so GetCurrent() reports the virtual account (proven admin).
    # The harness reads these to report genuine elevation without re-probing; a null/blank value
    # means the operation never reached identity capture (e.g. invocation rejected upstream).
    $childId = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $childWp = New-Object System.Security.Principal.WindowsPrincipal($childId)
    [PSCustomObject]@{
        action = $Action
        feature = $featList
        exitCode = $exitCode
        error = $errorMsg
        childUser = $childId.Name
        childIsAdministrator = $childWp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
        serviceState = (Get-PathVeerServiceState)
        manifest = (Get-PathVeerInstallManifest)
    }
}

<#
.SYNOPSIS
    Trusted CLI wrapper. Invokes ONLY the known PathVeer.Cli.exe with a closed verb set.
#>
function Invoke-PathVeerCli {
    [CmdletBinding()]
    param(
        [ValidateSet('status','repair','doctor','enable','disable','custom-routes')]
        [string]$Verb = 'status',

        [ValidateSet('add-cidr','list')]
        [string]$SubVerb = 'list',

        [ValidatePattern('^[\d./\sA-Za-z0-9-]{0,120}$')]
        [string]$Argument = ''
    )
    if (-not (Test-Path -LiteralPath $script:CliExe)) {
        return [PSCustomObject]@{ available=$false; exitCode=$null; output=$null; error='PathVeer.Cli.exe not present (install GATE-5 first).' }
    }
    $cliArgs = @($Verb)
    if ($Verb -eq 'custom-routes') { $cliArgs += $SubVerb; if ($Argument) { $cliArgs += $Argument } }
    $out = & $script:CliExe @cliArgs 2>&1
    return [PSCustomObject]@{ available=$true; exitCode=$LASTEXITCODE; output=($out -join "`n"); verb=$Verb; subVerb=$SubVerb }
}

function Get-PathVeerCertificationBoundary {
    <#
    .SYNOPSIS
        Trusted declaration of the canonical forbidden-command set for the certification JEA
        boundary. Runs as trusted module code (FullLanguage virtual account).

        IMPORTANT: this function does NOT measure caller-visible command availability itself.
        Measuring forbidden availability from inside the trusted module context is INVALID: the
        trusted context resolves the full system PATH and FullLanguage, so Get-Command finds host
        executables (powershell.exe, cmd.exe, wscript.exe, ...) that are NOT exposed to the
        restricted NoLanguage caller -> false positives.

        The authoritative caller-visible surface is measured by the DESKTOP probe via a bare
        Get-Command executed in the restricted JEA session ($jeaSession), then filtered Desktop-side
        against this canonical forbidden set. This function supplies only the trusted definition
        (fact A). The caller-visible measurement (fact B) is never taken from the trusted context.
    #>
    [CmdletBinding()]
    param()
    [PSCustomObject]@{
        # Canonical dangerous-command definition (trusted fact). The probe compares the restricted
        # session's OWN Get-Command output against this list Desktop-side.
        forbiddenDefined = @(
            'powershell.exe', 'cmd.exe', 'pwsh.exe', 'wscript.exe', 'cscript.exe', 'mshta.exe',
            'Start-Process', 'Invoke-Expression', 'Invoke-Command', 'Invoke-WebRequest',
            'New-ScheduledTask', 'Register-ScheduledTask', 'Set-Content', 'Set-Item',
            'New-Item', 'Invoke-Item', 'Get-CimInstance'
        )
    }
}

function Get-PathVeerProgramDataState {
    [CmdletBinding()]
    param()
    [PSCustomObject]@{
        installRootExists = (Test-Path -LiteralPath $script:InstallRoot)
        programDataExists = (Test-Path -LiteralPath $script:StateRoot)
        serviceExists = ($null -ne (Get-Service -Name $script:ServiceName -ErrorAction SilentlyContinue))
    }
}

Export-ModuleMember -Function @(
    'Test-PathVeerCertificationAdmin',
    'Get-PathVeerServiceState',
    'Start-PathVeerService',
    'Stop-PathVeerService',
    'Stop-PathVeerTray',
    'Get-PathVeerInstallManifest',
    'Get-PathVeerInstalledFiles',
    'Get-PathVeerRouteState',
    'Invoke-PathVeerCertificationInstall',
    'Invoke-PathVeerCli',
    'Get-PathVeerProgramDataState',
    'Get-PathVeerCertificationBoundary'
)
