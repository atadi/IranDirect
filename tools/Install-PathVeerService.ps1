param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("install", "uninstall")]
    [string]$Action = "install"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# New PathVeer external identity (Phase 36.4).
$ServiceName = "PathVeer"
$DisplayName = "PathVeer Service"
$ServiceDescription = "Routes direct IPv4 prefixes through the ISP gateway while protecting VPN endpoint connectivity."

# Legacy identity retained ONLY as a migration input: the installer stops the
# old IranDirect service so it can never run concurrently with PathVeer (the
# single-authority rule). It is NOT deleted here — Phase 36.7 owns the final
# removal contract once PathVeer is confirmed operational.
$LegacyServiceName = "IranDirect"

$RepoRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..")
)

$ProjectPath = Join-Path `
    $RepoRoot `
    "PathVeer.Service\PathVeer.Service.csproj"

$PublishPath = Join-Path `
    $RepoRoot `
    "artifacts\PathVeer.Service\publish"

$BinPath = Join-Path `
    $PublishPath `
    "PathVeer.Service.exe"

function Assert-Administrator {
    $currentIdentity =
        [Security.Principal.WindowsIdentity]::GetCurrent()

    $principal =
        [Security.Principal.WindowsPrincipal]::new(
            $currentIdentity
        )

    $isAdministrator =
        $principal.IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator
        )

    if (-not $isAdministrator) {
        throw "Run this script from an Administrator PowerShell session."
    }
}

function Invoke-Sc {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $false)]
        [switch]$IgnoreFailure
    )

    & sc.exe @Arguments

    $exitCode = $LASTEXITCODE

    if (-not $IgnoreFailure -and $exitCode -ne 0) {
        throw "sc.exe failed with exit code $exitCode. Arguments: $($Arguments -join ' ')"
    }

    return $exitCode
}

function Test-ServiceExists {
    param([string]$Name)

    $service =
        Get-Service `
            -Name $Name `
            -ErrorAction SilentlyContinue

    return $null -ne $service
}

function Stop-ServiceByName {
    param([string]$Name)

    if (-not (Test-ServiceExists $Name)) {
        return
    }

    $service =
        Get-Service `
            -Name $Name `
            -ErrorAction Stop

    if ($service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        return
    }

    Write-Host "Stopping existing service '$Name'..." `
        -ForegroundColor Cyan

    Stop-Service `
        -Name $Name `
        -Force `
        -ErrorAction Stop

    $service.WaitForStatus(
        [System.ServiceProcess.ServiceControllerStatus]::Stopped,
        [TimeSpan]::FromSeconds(20))
}

function Publish-PathVeerService {
    if (-not (Test-Path $ProjectPath)) {
        throw "Project file not found: $ProjectPath"
    }

    Write-Host "Publishing PathVeer.Service..." `
        -ForegroundColor Cyan

    if (Test-Path $PublishPath) {
        Remove-Item `
            $PublishPath `
            -Recurse `
            -Force
    }

    New-Item `
        -ItemType Directory `
        -Path $PublishPath `
        -Force |
        Out-Null

    & dotnet publish `
        $ProjectPath `
        --configuration Release `
        --runtime win-x64 `
        --self-contained false `
        --output $PublishPath

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path $BinPath)) {
        throw "Published service executable not found: $BinPath"
    }

    Write-Host "Published executable:" `
        -ForegroundColor DarkGray

    Write-Host $BinPath `
        -ForegroundColor DarkGray
}

function Configure-PathVeerService {
    $quotedBinPath = "`"$BinPath`""

    if (Test-ServiceExists $ServiceName) {
        Write-Host "Updating existing service '$ServiceName'..." `
            -ForegroundColor Cyan

        Invoke-Sc -Arguments @(
            "config",
            $ServiceName,
            "binPath=",
            $quotedBinPath,
            "start=",
            "delayed-auto",
            "displayName=",
            $DisplayName
        )
    }
    else {
        Write-Host "Creating Windows Service '$ServiceName'..." `
            -ForegroundColor Cyan

        Invoke-Sc -Arguments @(
            "create",
            $ServiceName,
            "binPath=",
            $quotedBinPath,
            "start=",
            "delayed-auto",
            "displayName=",
            $DisplayName
        )
    }

    Invoke-Sc -Arguments @(
        "description",
        $ServiceName,
        $ServiceDescription
    )

    Invoke-Sc -Arguments @(
        "failure",
        $ServiceName,
        "reset=",
        "86400",
        "actions=",
        "restart/10000/restart/30000/restart/60000"
    )

    Invoke-Sc -Arguments @(
        "failureflag",
        $ServiceName,
        "1"
    )
}

function Start-PathVeerService {
    Write-Host "Starting service '$ServiceName'..." `
        -ForegroundColor Cyan

    Start-Service `
        -Name $ServiceName `
        -ErrorAction Stop

    $service =
        Get-Service `
            -Name $ServiceName `
            -ErrorAction Stop

    $service.WaitForStatus(
        [System.ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(20))

    $service.Refresh()

    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
        throw "Service failed to reach the Running state. Current status: $($service.Status)"
    }

    Write-Host "SUCCESS: $ServiceName is running." `
        -ForegroundColor Green

    Write-Host ""
    Write-Host "Service details:" `
        -ForegroundColor Cyan

    Get-Service `
        -Name $ServiceName |
        Format-List `
            Name,
            DisplayName,
            Status,
            StartType

    Write-Host "Executable: $BinPath" `
        -ForegroundColor DarkGray
}

function Install-Service {
    Assert-Administrator

    # Single-authority migration: the legacy IranDirect service MUST be stopped
    # before PathVeer becomes the route-mutation authority. A running legacy
    # service that also owns the pipe/state would violate the no-concurrent-
    # authority rule. It is stopped, not deleted (Phase 36.7 owns removal).
    Stop-ServiceByName -Name $LegacyServiceName

    # Also stop a development console instance if one is running.
    Get-Process `
        -Name "IranDirect.Service" `
        -ErrorAction SilentlyContinue |
        Stop-Process `
            -Force `
            -ErrorAction SilentlyContinue

    Get-Process `
        -Name "PathVeer.Service" `
        -ErrorAction SilentlyContinue |
        Stop-Process `
            -Force `
            -ErrorAction SilentlyContinue

    Publish-PathVeerService
    Configure-PathVeerService
    Start-PathVeerService
}

function Uninstall-Service {
    Assert-Administrator

    if (-not (Test-ServiceExists $ServiceName)) {
        Write-Host "Service '$ServiceName' is not installed." `
            -ForegroundColor Yellow

        return
    }

    Write-Host "Stopping service '$ServiceName'..." `
        -ForegroundColor Cyan

    Stop-Service `
        -Name $ServiceName `
        -Force `
        -ErrorAction SilentlyContinue

    Start-Sleep -Seconds 2

    Write-Host "Removing service '$ServiceName'..." `
        -ForegroundColor Cyan

    Invoke-Sc -Arguments @(
        "delete",
        $ServiceName
    )

    $timeout = [DateTimeOffset]::UtcNow.AddSeconds(15)

    while (
        (Test-ServiceExists $ServiceName) -and
        [DateTimeOffset]::UtcNow -lt $timeout
    ) {
        Start-Sleep -Milliseconds 500
    }

    if (Test-ServiceExists $ServiceName) {
        Write-Host "Service was marked for deletion but still exists. Close Services.msc or restart Windows if necessary." `
            -ForegroundColor Yellow
    }
    else {
        Write-Host "SUCCESS: $ServiceName removed." `
            -ForegroundColor Green
    }
}

switch ($Action.ToLowerInvariant()) {
    "install" {
        Install-Service
    }

    "uninstall" {
        Uninstall-Service
    }
}
