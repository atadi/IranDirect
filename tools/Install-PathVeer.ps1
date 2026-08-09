<#
.SYNOPSIS
    Installs, upgrades or uninstalls PathVeer from a versioned package.

.DESCRIPTION
    Phase 36.7 installer/upgrader.

    Installer technology decision: PowerShell. The product currently ships a
    single Windows service plus two small executables, has no COM/driver
    registration, no per-file MSI servicing requirement and no MSI-based
    patching story. WiX/MSIX/Inno would add a toolchain and a build dependency
    without buying anything the upgrade contract needs today. This script is
    written to be signable and is the documented v1 mechanism; moving to MSI
    later is a packaging change, not a redesign, because all the ordering logic
    lives in PathVeer.Core.Installation and is unit tested there.

    THREE ROOTS, never conflated:
        development repo  C:\codespace\PathVeer   (source only)
        installed binaries %ProgramFiles%\PathVeer (this script writes here)
        persistent state   %ProgramData%\PathVeer  (never written here)

    SINGLE AUTHORITY: the legacy IranDirect service and the PathVeer service
    must never both run. Legacy is stopped and disabled BEFORE any binary is
    replaced, and is deleted only AFTER PathVeer is confirmed healthy, so a
    failed upgrade always leaves a rollback target.

    OFFLINE: no network access is required or attempted. There is no update
    check and no call to pathveer.com.

.PARAMETER Action
    install (default), uninstall, or status.

.PARAMETER PackageDirectory
    Package produced by New-PathVeerPackage.ps1. Required for install.

.PARAMETER PurgeState
    Uninstall only. Also deletes %ProgramData%\PathVeer. OFF by default:
    persistent state holds route ownership, the mutation journal and
    configuration. %ProgramData%\IranDirect is never deleted, even with this.

.PARAMETER InstallTray
    Registers the Tray autorun entry for the CURRENT user. Off by default
    because the service is machine-level while the tray is per-user.

.EXAMPLE
    .\tools\Install-PathVeer.ps1 -PackageDirectory C:\pkg\PathVeer-1.0.0

.EXAMPLE
    .\tools\Install-PathVeer.ps1 -Action uninstall
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('install', 'uninstall', 'status')]
    [string]$Action = 'install',

    [Parameter(Mandatory = $false)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $false)]
    [switch]$PurgeState,

    [Parameter(Mandatory = $false)]
    [switch]$InstallTray
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- identity constants (must match PathVeer.Core) -------------------------
$ServiceName        = 'PathVeer'
$ServiceDisplayName = 'PathVeer Service'
$ServiceDescription = 'Routes direct IPv4 prefixes through the ISP gateway while protecting VPN endpoint connectivity.'
$LegacyServiceName  = 'IranDirect'

$InstallRoot     = Join-Path $env:ProgramFiles 'PathVeer'
$ServiceDir      = Join-Path $InstallRoot 'Service'
$CliDir          = Join-Path $InstallRoot 'Cli'
$TrayDir         = Join-Path $InstallRoot 'Tray'
$StagingDir      = Join-Path $InstallRoot '.staging'
$ManifestPath    = Join-Path $InstallRoot 'install-manifest.json'

$ServiceExe = Join-Path $ServiceDir 'PathVeer.Service.exe'
$CliExe     = Join-Path $CliDir     'PathVeer.Cli.exe'
$TrayExe    = Join-Path $TrayDir    'PathVeer.Tray.exe'

$RunKeyPath           = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$TrayRunValueName     = 'PathVeer Tray'
$LegacyTrayRunValue   = 'IranDirect Tray'

$StatusTimeout = [TimeSpan]::FromSeconds(30)

# --- helpers ---------------------------------------------------------------

function Write-Step   { param([string]$m) Write-Host $m -ForegroundColor Cyan }
function Write-Detail { param([string]$m) Write-Host "  $m" -ForegroundColor DarkGray }
function Write-Ok     { param([string]$m) Write-Host $m -ForegroundColor Green }

function Assert-Administrator {
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)

    if (-not $principal.IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script from an elevated (Administrator) PowerShell session. Installing a Windows Service and writing to Program Files both require elevation.'
    }
}

function Test-ServiceExists {
    param([string]$Name)

    return $null -ne (Get-Service -Name $Name -ErrorAction SilentlyContinue)
}

function Get-ServiceBinaryPath {
    param([string]$Name)

    if (-not (Test-ServiceExists $Name)) { return $null }

    $output = & sc.exe qc $Name 2>$null

    foreach ($line in $output) {
        if ($line -match 'BINARY_PATH_NAME\s*:\s*(.+)$') {
            return $Matches[1].Trim().Trim('"')
        }
    }

    return $null
}

function Stop-ServiceAndVerify {
    param([string]$Name, [string]$Label)

    if (-not (Test-ServiceExists $Name)) { return }

    $service = Get-Service -Name $Name -ErrorAction Stop

    if ($service.Status -ne 'Stopped') {
        Write-Step "Stopping $Label service..."
        Stop-Service -Name $Name -Force -ErrorAction Stop

        try {
            $service.WaitForStatus('Stopped', $StatusTimeout)
        } catch {
            throw "The $Label service did not stop within $($StatusTimeout.TotalSeconds)s. Aborting before any binary is replaced so a single route authority is preserved."
        }
    }

    $service.Refresh()

    if ($service.Status -ne 'Stopped') {
        throw "The $Label service could not be stopped (status: $($service.Status)). Aborting to preserve single authority."
    }
}

function Disable-LegacyService {
    if (-not (Test-ServiceExists $LegacyServiceName)) { return }

    # Disabled (not deleted) so it cannot auto-restart mid-upgrade while
    # remaining available as a rollback target.
    & sc.exe config $LegacyServiceName start= disabled | Out-Null

    Write-Detail 'Legacy IranDirect service disabled (kept for rollback).'
}

function Test-PackageIntegrity {
    param([string]$Directory)

    $hashFile = Join-Path $Directory 'package-hashes.sha256'

    if (-not (Test-Path $hashFile)) {
        throw "Package integrity manifest not found: $hashFile. Refusing to install an unverified payload."
    }

    Write-Step 'Verifying package integrity...'

    $checked = 0

    foreach ($line in Get-Content $hashFile) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        $parts = $line -split ' ', 2

        if ($parts.Count -ne 2) {
            throw "Malformed line in package manifest: $line"
        }

        $expected = $parts[0].Trim()
        $relative = $parts[1].Trim()
        $full     = Join-Path $Directory $relative

        if (-not (Test-Path $full)) {
            throw "Package file listed in manifest is missing: $relative"
        }

        $actual = (Get-FileHash -Path $full -Algorithm SHA256).Hash

        if ($actual -ne $expected) {
            throw "Package integrity check FAILED for '$relative'. Installation aborted; the existing installation is untouched."
        }

        $checked++
    }

    Write-Detail "$checked files verified (integrity only; package is not signed)."
}

function Install-Payload {
    param([string]$Directory)

    # Stage -> verify -> swap. The live install directory is never observed
    # half-updated, and a bad payload is discarded before anything is replaced.
    Write-Step 'Staging payload...'

    if (Test-Path $StagingDir) {
        Remove-Item $StagingDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

    foreach ($component in @('Service', 'Cli', 'Tray')) {
        $source = Join-Path $Directory $component

        if (-not (Test-Path $source)) {
            Remove-Item $StagingDir -Recurse -Force
            throw "Package is missing the '$component' component."
        }

        Copy-Item $source -Destination $StagingDir -Recurse -Force
    }

    foreach ($required in @(
            (Join-Path $StagingDir 'Service\PathVeer.Service.exe'),
            (Join-Path $StagingDir 'Cli\PathVeer.Cli.exe'),
            (Join-Path $StagingDir 'Tray\PathVeer.Tray.exe'))) {

        if (-not (Test-Path $required)) {
            Remove-Item $StagingDir -Recurse -Force
            throw "Staged payload is missing $required. Staging discarded; existing installation untouched."
        }
    }

    Write-Step 'Swapping binaries into the install root...'

    foreach ($component in @('Service', 'Cli', 'Tray')) {
        $staged = Join-Path $StagingDir $component
        $live   = Join-Path $InstallRoot $component

        if (Test-Path $live) {
            Remove-Item $live -Recurse -Force
        }

        Move-Item $staged $live
    }

    Remove-Item $StagingDir -Recurse -Force -ErrorAction SilentlyContinue
}

function Set-PathVeerService {
    $quoted = "`"$ServiceExe`""
    $current = Get-ServiceBinaryPath $ServiceName

    if (Test-ServiceExists $ServiceName) {
        if ($current -and ($current -ne $ServiceExe)) {
            Write-Detail "Repointing service from '$current'."
        }

        & sc.exe config $ServiceName binPath= $quoted start= delayed-auto displayName= $ServiceDisplayName | Out-Null
    }
    else {
        Write-Step "Creating Windows Service '$ServiceName'..."
        & sc.exe create $ServiceName binPath= $quoted start= delayed-auto displayName= $ServiceDisplayName | Out-Null
    }

    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failed to configure the service (exit $LASTEXITCODE)."
    }

    & sc.exe description $ServiceName $ServiceDescription | Out-Null
    & sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null
    & sc.exe failureflag $ServiceName 1 | Out-Null
}

function Add-CliToPath {
    # Idempotent: the entry is added only when not already present, so repeated
    # installs cannot grow the machine PATH.
    $current = [Environment]::GetEnvironmentVariable('PATH', 'Machine')
    $entries = @()

    if ($current) {
        $entries = $current -split ';' | Where-Object { $_ -ne '' }
    }

    $normalized = $CliDir.TrimEnd('\')

    $present = $entries | Where-Object {
        $_.Trim().TrimEnd('\') -ieq $normalized
    }

    if ($present) {
        Write-Detail 'PATH already contains the PathVeer CLI directory.'
        return
    }

    $entries += $CliDir

    [Environment]::SetEnvironmentVariable(
        'PATH', ($entries -join ';'), 'Machine')

    Write-Detail "Added '$CliDir' to the machine PATH."
    Write-Detail "Open a NEW terminal for 'pathveer' to resolve."
}

function Remove-CliFromPath {
    $current = [Environment]::GetEnvironmentVariable('PATH', 'Machine')

    if (-not $current) { return }

    $normalized = $CliDir.TrimEnd('\')

    # Removes ONLY PathVeer's own entry; unrelated entries keep their order.
    $entries = $current -split ';' |
        Where-Object { $_ -ne '' } |
        Where-Object { $_.Trim().TrimEnd('\') -ine $normalized }

    [Environment]::SetEnvironmentVariable(
        'PATH', ($entries -join ';'), 'Machine')
}

function New-CliCompatibilityShim {
    # Legacy CLI compatibility (Phase 36.7 decision: option B, a shim).
    # 'irandirect' forwards to the ONE PathVeer CLI implementation and prints a
    # deprecation notice to stderr. It is not a second CLI.
    $shimPath = Join-Path $CliDir 'irandirect.cmd'

    $shim = @(
        '@echo off'
        'REM Deprecated compatibility shim (Phase 36.7).'
        'REM Forwards to the PathVeer CLI. Remove after operators migrate.'
        'echo [deprecated] ''irandirect'' is now ''pathveer''. 1>&2'
        '"%~dp0PathVeer.Cli.exe" %*'
    )

    Set-Content -Path $shimPath -Value $shim -Encoding ASCII

    Write-Detail "Legacy 'irandirect' shim installed (forwards to pathveer)."
}

function Set-TrayStartupEntry {
    # Per-user autorun. The service is machine-level; the tray is not.
    if (-not (Test-Path $RunKeyPath)) {
        New-Item -Path $RunKeyPath -Force | Out-Null
    }

    Set-ItemProperty -Path $RunKeyPath -Name $TrayRunValueName -Value "`"$TrayExe`""

    Write-Detail "Tray autorun registered for user '$env:USERNAME'."
}

function Remove-TrayStartupEntry {
    if (-not (Test-Path $RunKeyPath)) { return }

    foreach ($name in @($TrayRunValueName, $LegacyTrayRunValue)) {
        $existing = Get-ItemProperty -Path $RunKeyPath -Name $name -ErrorAction SilentlyContinue

        if ($existing) {
            Remove-ItemProperty -Path $RunKeyPath -Name $name -ErrorAction SilentlyContinue
            Write-Detail "Removed autorun entry '$name'."
        }
    }
}

function Test-PathVeerReadiness {
    # SCM 'Running' alone is not accepted as proof of a successful upgrade.
    $service = Get-Service -Name $ServiceName -ErrorAction Stop

    if ($service.Status -ne 'Running') {
        return @{ Healthy = $false; Reason = "service status is $($service.Status)" }
    }

    # The primary control pipe must actually be served.
    $pipes = [System.IO.Directory]::GetFiles('\\.\pipe\')
    $pipeUp = $pipes | Where-Object { $_ -like '*PathVeer.Control.v1*' }

    if (-not $pipeUp) {
        return @{ Healthy = $false; Reason = 'primary control pipe PathVeer.Control.v1 is not available' }
    }

    # And the CLI must get a real answer back over it.
    if (Test-Path $CliExe) {
        & $CliExe status *> $null

        if ($LASTEXITCODE -ne 0) {
            return @{ Healthy = $false; Reason = "CLI status command failed (exit $LASTEXITCODE)" }
        }
    }

    return @{ Healthy = $true; Reason = 'process running, pipe available, status command succeeded' }
}

function Write-InstallManifest {
    param(
        [string]$Version,
        [bool]$LegacyMigrated,
        [string]$PreviousVersion
    )

    $manifest = [ordered]@{
        schemaVersion                   = 1
        productVersion                  = $Version
        installRoot                     = $InstallRoot
        serviceExecutablePath           = $ServiceExe
        cliExecutablePath               = $CliExe
        trayExecutablePath              = $TrayExe
        installedAtUtc                  = (Get-Date).ToUniversalTime().ToString('o')
        legacyServiceMigrationCompleted = $LegacyMigrated
        upgradedFromVersion             = $PreviousVersion
    }

    $manifest |
        ConvertTo-Json -Depth 4 |
        Set-Content -Path $ManifestPath -Encoding UTF8
}

function Get-PackageVersion {
    param([string]$Directory)

    $metadataPath = Join-Path $Directory 'package.json'

    if (-not (Test-Path $metadataPath)) {
        throw "package.json not found in $Directory. Build the package with New-PathVeerPackage.ps1."
    }

    return (Get-Content $metadataPath -Raw | ConvertFrom-Json).version
}

function Get-InstalledVersion {
    if (-not (Test-Path $ManifestPath)) { return $null }

    try {
        return (Get-Content $ManifestPath -Raw | ConvertFrom-Json).productVersion
    } catch {
        return $null
    }
}

# --- actions ---------------------------------------------------------------

function Invoke-Install {
    Assert-Administrator

    if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
        throw 'Specify -PackageDirectory (produced by New-PathVeerPackage.ps1).'
    }

    if (-not (Test-Path $PackageDirectory)) {
        throw "Package directory not found: $PackageDirectory"
    }

    $version         = Get-PackageVersion $PackageDirectory
    $previousVersion = Get-InstalledVersion

    Write-Host ''
    Write-Step "PathVeer $version -> $InstallRoot"

    if ($previousVersion) {
        Write-Detail "Upgrading from installed version $previousVersion."
    } else {
        Write-Detail 'Fresh installation.'
    }

    # 1. Verify BEFORE any teardown, so a corrupt package cannot strand the box.
    Test-PackageIntegrity $PackageDirectory

    # 2. Legacy authority down first.
    $legacyExisted = Test-ServiceExists $LegacyServiceName

    if ($legacyExisted) {
        Write-Detail 'Legacy IranDirect service detected.'
        Stop-ServiceAndVerify -Name $LegacyServiceName -Label 'legacy IranDirect'
        Disable-LegacyService
    }

    # 3. Then the current service, so binaries are replaceable.
    Stop-ServiceAndVerify -Name $ServiceName -Label 'PathVeer'

    # 4. Replace binaries.
    Install-Payload $PackageDirectory

    # 5. Point SCM at the canonical install root (never a dev checkout).
    Set-PathVeerService

    # 6. CLI discoverability.
    Add-CliToPath
    New-CliCompatibilityShim

    if ($InstallTray) {
        Set-TrayStartupEntry
    } else {
        Write-Detail 'Tray autorun not registered (pass -InstallTray to enable).'
    }

    # 7. Start and prove health.
    Write-Step 'Starting PathVeer service...'
    Start-Service -Name $ServiceName -ErrorAction Stop

    $service = Get-Service -Name $ServiceName
    try {
        $service.WaitForStatus('Running', $StatusTimeout)
    } catch {
        throw "PathVeer did not reach Running. The legacy service remains stopped and disabled, so no second authority is active."
    }

    Start-Sleep -Seconds 2
    $readiness = Test-PathVeerReadiness

    if (-not $readiness.Healthy) {
        Write-Warning "Readiness validation failed: $($readiness.Reason)"
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue

        throw "PathVeer started but failed readiness validation. It has been stopped and the legacy service was NOT removed, so rollback remains possible."
    }

    Write-Detail "Readiness: $($readiness.Reason)"

    # 8. Only now retire the legacy service.
    $legacyRemoved = $false

    if ($legacyExisted) {
        & sc.exe delete $LegacyServiceName | Out-Null
        Start-Sleep -Seconds 1
        $legacyRemoved = -not (Test-ServiceExists $LegacyServiceName)

        if ($legacyRemoved) {
            Write-Detail 'Legacy IranDirect service removed.'
        } else {
            Write-Warning 'Legacy service is marked for deletion but still present. It is stopped and disabled, so single authority holds. It will clear on reboot.'
        }
    }

    Write-InstallManifest `
        -Version $version `
        -LegacyMigrated $legacyExisted `
        -PreviousVersion $previousVersion

    Write-Host ''
    Write-Ok "SUCCESS: PathVeer $version installed."
    Write-Detail "Service binary : $ServiceExe"
    Write-Detail "CLI            : $CliExe"
    Write-Detail "State root     : $env:ProgramData\PathVeer (untouched by this installer)"
    Write-Host ''

    if ($legacyExisted) {
        Write-Detail 'Legacy state at %ProgramData%\IranDirect was preserved for rollback.'
    }
}

function Invoke-Uninstall {
    Assert-Administrator

    Write-Host ''
    Write-Step 'Uninstalling PathVeer...'

    # Release managed routes while an authority still exists to do it.
    if ((Test-ServiceExists $ServiceName) -and
        ((Get-Service $ServiceName).Status -eq 'Running') -and
        (Test-Path $CliExe)) {

        Write-Step 'Releasing managed routes before removing the service...'

        & $CliExe disable *> $null

        if ($LASTEXITCODE -eq 0) {
            Write-Detail 'Managed routes released.'
        } else {
            Write-Warning 'Route release failed. Routes may remain applied; persistent state is being retained so a reinstall can reconcile them.'
        }
    }

    if (Test-ServiceExists $ServiceName) {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        & sc.exe delete $ServiceName | Out-Null
        Write-Detail 'PathVeer service removed.'
    }

    Remove-CliFromPath
    Write-Detail 'PATH entry removed.'

    Remove-TrayStartupEntry

    if (Test-Path $InstallRoot) {
        Remove-Item $InstallRoot -Recurse -Force -ErrorAction SilentlyContinue
        Write-Detail "Installed binaries removed from $InstallRoot."
    }

    if ($PurgeState) {
        $stateRoot = Join-Path $env:ProgramData 'PathVeer'

        if (Test-Path $stateRoot) {
            Remove-Item $stateRoot -Recurse -Force
            Write-Warning "EXPLICIT PURGE: deleted $stateRoot."
        }
    }
    else {
        Write-Host ''
        Write-Detail "Persistent state RETAINED at $env:ProgramData\PathVeer."
        Write-Detail 'It holds route ownership, the mutation journal and configuration.'
        Write-Detail 'Re-run with -PurgeState to delete it.'
    }

    Write-Detail "Legacy state at $env:ProgramData\IranDirect was not touched."

    Write-Host ''
    Write-Ok 'SUCCESS: PathVeer uninstalled.'
}

function Invoke-Status {
    Write-Host ''
    Write-Step 'PathVeer installation status'

    $installedVersion = Get-InstalledVersion

    Write-Detail "Install root      : $InstallRoot $(if (Test-Path $InstallRoot) { '(present)' } else { '(absent)' })"
    Write-Detail "Installed version : $(if ($installedVersion) { $installedVersion } else { '<none>' })"

    if (Test-ServiceExists $ServiceName) {
        $service = Get-Service $ServiceName
        Write-Detail "PathVeer service  : $($service.Status) [$($service.StartType)]"
        Write-Detail "Service binary    : $(Get-ServiceBinaryPath $ServiceName)"
    } else {
        Write-Detail 'PathVeer service  : not installed'
    }

    if (Test-ServiceExists $LegacyServiceName) {
        $legacy = Get-Service $LegacyServiceName
        Write-Warning "Legacy IranDirect service is STILL PRESENT: $($legacy.Status) [$($legacy.StartType)]"
    } else {
        Write-Detail 'Legacy service    : absent (good)'
    }

    Write-Detail "State root        : $env:ProgramData\PathVeer $(if (Test-Path (Join-Path $env:ProgramData 'PathVeer')) { '(present)' } else { '(absent)' })"
    Write-Detail "Legacy state root : $env:ProgramData\IranDirect $(if (Test-Path (Join-Path $env:ProgramData 'IranDirect')) { '(present, retained)' } else { '(absent)' })"
    Write-Host ''
}

switch ($Action) {
    'install'   { Invoke-Install }
    'uninstall' { Invoke-Uninstall }
    'status'    { Invoke-Status }
}
