param(
    [Parameter(Mandatory = $false)]
    [string]$Action = "install"
)

$ServiceName = "IranDirect"
$BinPath = Join-Path $PSScriptRoot "..\IranDirect.Service\bin\Release\net10.0-windows\IranDirect.Service.exe"

function Install-Service {
    Write-Host "Publishing IranDirect.Service..." -ForegroundColor Cyan
    & dotnet publish ..\IranDirect.Service -c Release -o (Join-Path $PSScriptRoot "..\publish") --self-contained false

    if (-not (Test-Path $BinPath)) {
        Write-Host "ERROR: $BinPath not found. Build failed." -ForegroundColor Red
        exit 1
    }

    Write-Host "Creating Windows Service '$ServiceName'..." -ForegroundColor Cyan

    sc.exe create $ServiceName `
        binPath= "$BinPath" `
        start= delayed-auto `
        displayName= "IranDirect Service"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to create service. It may already exist (sc.exe error $LASTEXITCODE)." -ForegroundColor Yellow
        Write-Host "Updating existing service configuration..." -ForegroundColor Cyan
        sc.exe config $ServiceName binPath= "$BinPath" start= delayed-auto
    }

    sc.exe description $ServiceName "Routes Iranian IPv4 prefixes directly through the ISP gateway while protecting VPN endpoint connectivity."

    sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/30000/restart/60000

    Write-Host "Starting service '$ServiceName'..." -ForegroundColor Cyan
    sc.exe start $ServiceName

    Start-Sleep -Seconds 3

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq 'Running') {
        Write-Host "SUCCESS: $ServiceName is running." -ForegroundColor Green
    } else {
        Write-Host "WARNING: Service status: $($service.Status). Check 'sc query $ServiceName'." -ForegroundColor Yellow
    }
}

function Uninstall-Service {
    Write-Host "Stopping service '$ServiceName'..." -ForegroundColor Cyan
    sc.exe stop $ServiceName
    Start-Sleep -Seconds 2

    Write-Host "Removing service '$ServiceName'..." -ForegroundColor Cyan
    sc.exe delete $ServiceName

    if ($LASTEXITCODE -eq 0) {
        Write-Host "SUCCESS: $ServiceName removed." -ForegroundColor Green
    } else {
        Write-Host "Service removal failed (sc.exe error $LASTEXITCODE)." -ForegroundColor Yellow
    }
}

switch ($Action.ToLower()) {
    "install" { Install-Service }
    "uninstall" { Uninstall-Service }
    default {
        Write-Host "Usage: .\Install-IranDirectService.ps1 [-Action install|uninstall]"
        Write-Host "  install   (default) Publish, create, and start the Windows Service."
        Write-Host "  uninstall Stop and remove the Windows Service."
    }
}
