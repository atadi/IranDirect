# PathVeer certification JEA trusted module.
#
# This module is installed ONLY inside the guest certification control plane
# (C:\Program Files\PathVeerCertificationJea) and is exposed through the narrow
# 'PathVeerCertificationRole' role capability. Every function here is CALLER-FACING but
# strictly validated: no arbitrary command, ScriptBlock, executable path, filesystem path,
# registry path, or service name may be supplied by the connecting user. The module may use
# privileged PowerShell internally, but only against KNOWN PathVeer certification targets.
#
# SECURITY CONTRACT:
#   - The installer location and CLI location are fixed constants. They are never parameters.
#   - Permitted service identity is fixed ('PathVeer').
#   - Permitted installer actions are a closed ValidateSet.
#   - No generic shell (powershell.exe/cmd.exe), no Invoke-Expression, no Start-Process with
#     caller input, no New-ScheduledTask, no arbitrary filesystem/registry write primitives.
#   - Functions return structured evidence; they do not silently mask failure.

$script:InstallScript   = 'C:\pv-cert\Install-PathVeer.ps1'
$script:InstallPackage  = 'C:\pv-cert\PathVeer-1.0.0-beta.1'
$script:CliExe          = 'C:\Program Files\PathVeer\Cli\PathVeer.Cli.exe'
$script:ServiceName     = 'PathVeer'
$script:InstallRoot     = 'C:\Program Files\PathVeer'
$script:StateRoot       = Join-Path $env:ProgramData 'PathVeer'
$script:ResultRoot      = 'C:\pv-cert'

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

        [ValidateSet('RegisterShell','InstallTray')]
        [string[]]$Feature = @('RegisterShell','InstallTray')
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
        if ($Action -in @('Install','Upgrade','Repair')) {
            foreach ($f in $Feature) { $psiArgs += "-$f" }
        }
        # Use the SAME powershell host that is already running the JEA session (trusted, fixed).
        # This is NOT exposed to the caller as an arbitrary-execution primitive; the path and
        # arguments are entirely fixed/validated here.
        & (Get-Process -Id $pid).Path @psiArgs
        $exitCode = $LASTEXITCODE
    } catch {
        $errorMsg = $_.Exception.Message
    }
    [PSCustomObject]@{
        action = $Action
        feature = $Feature
        exitCode = $exitCode
        error = $errorMsg
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
    'Get-PathVeerProgramDataState'
)
