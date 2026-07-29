param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("install", "uninstall")]
    [string]$Action = "install"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ServiceName = "IranDirect"
$DisplayName = "IranDirect Service"
$ServiceDescription = "Routes Iranian IPv4 prefixes directly through the ISP gateway while protecting VPN endpoint connectivity."

$RepoRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..")
)

$ProjectPath = Join-Path `
    $RepoRoot `
    "IranDirect.Service\IranDirect.Service.csproj"

$PublishPath = Join-Path `
    $RepoRoot `
    "artifacts\IranDirect.Service\publish"

$BinPath = Join-Path `
    $PublishPath `
    "IranDirect.Service.exe"

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
    $service =
        Get-Service `
            -Name $ServiceName `
            -ErrorAction SilentlyContinue

    return $null -ne $service
}

function Stop-IranDirectService {
    if (-not (Test-ServiceExists)) {
        return
    }

    $service =
        Get-Service `
            -Name $ServiceName `
            -ErrorAction Stop

    if ($service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        return
    }

    Write-Host "Stopping existing service '$ServiceName'..." `
        -ForegroundColor Cyan

    Stop-Service `
        -Name $ServiceName `
        -Force `
        -ErrorAction Stop

    $service.WaitForStatus(
        [System.ServiceProcess.ServiceControllerStatus]::Stopped,
        [TimeSpan]::FromSeconds(20)
    )
}

function Publish-IranDirectService {
    if (-not (Test-Path $ProjectPath)) {
        throw "Project file not found: $ProjectPath"
    }

    Write-Host "Publishing IranDirect.Service..." `
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

function Configure-IranDirectService {
    $quotedBinPath = "`"$BinPath`""

    if (Test-ServiceExists) {
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

function Start-IranDirectService {
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
        [TimeSpan]::FromSeconds(20)
    )

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

    # Stop the installed service before replacing published binaries.
    Stop-IranDirectService

    # Also stop a development console instance if one is running.
    Get-Process `
        -Name "IranDirect.Service" `
        -ErrorAction SilentlyContinue |
        Stop-Process `
            -Force `
            -ErrorAction SilentlyContinue

    Publish-IranDirectService
    Configure-IranDirectService
    Start-IranDirectService
}

function Uninstall-Service {
    Assert-Administrator

    if (-not (Test-ServiceExists)) {
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
        (Test-ServiceExists) -and
        [DateTimeOffset]::UtcNow -lt $timeout
    ) {
        Start-Sleep -Milliseconds 500
    }

    if (Test-ServiceExists) {
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