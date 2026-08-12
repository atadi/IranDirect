<# .SYNOPSIS
    PathVeer VM certification — guest install-state capture (post-failure inspection).

    Captures the REAL installed state of the PathVeer-Certification guest AFTER a
    failed/partial GATE-5 run, so the harness defect vs installer-defect question can
    be resolved from Windows evidence (not source assumptions).

    SECRETS: none. Prompts the operator locally for the guest 'pvcert' credential via
    the native Windows credential dialog (never logged, never passed as an argument).

    OUTPUT: writes artifacts/certification/guest-state/<timestamp>-guest-install-state.json
    on the HOST (the operator should paste the printed summary if the JSON cannot be read
    back by the builder shell).

.NOTES
    Does NOT restore any checkpoint. Read-only inspection only.
#>
[CmdletBinding()]
param(
    [string]$VMName = 'PathVeer-Certification',
    [string]$GuestUser = 'PV-CERT\pvcert'
)

$ErrorActionPreference = 'Stop'

# --- credential (user-local native prompt) ---
$cred = Get-Credential -UserName $GuestUser -Message "Enter the PathVeer-Certification guest password for '$GuestUser'"
if (-not $cred) { Write-Error 'No credential supplied. Aborting.'; exit 1 }

# --- PowerShell Direct session (with retry) ---
$session = $null
for ($i = 1; $i -le 12; $i++) {
    try {
        $session = New-PSSession -VMName $VMName -Credential $cred -ErrorAction Stop
        break
    } catch {
        if ($i -eq 12) { throw "PowerShell Direct failed after retries: $_" }
        Start-Sleep -Seconds 5
    }
}

try {
    $state = Invoke-Command -Session $session -ScriptBlock {
        $installRoot = Join-Path $env:ProgramFiles 'PathVeer'

        # A. Service
        $svc = Get-CimInstance Win32_Service -Filter "Name='PathVeer'" -ErrorAction SilentlyContinue
        $service = $null
        if ($svc) {
            $service = [PSCustomObject]@{
                name      = $svc.Name
                state     = $svc.State
                startMode = $svc.StartMode
                pathName  = $svc.PathName
            }
        }

        # B. Apps & Features
        $roots = @(
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
        )
        $apps = @(Get-ItemProperty $roots -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like '*PathVeer*' } |
            ForEach-Object {
                [PSCustomObject]@{
                    displayName     = $_.DisplayName
                    displayVersion  = $_.DisplayVersion
                    installLocation = $_.InstallLocation
                    uninstallString = $_.UninstallString
                }
            })

        # C. Installed file tree under Program Files\PathVeer
        $tree = @(Get-ChildItem $installRoot -Recurse -Force -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty FullName)

        # D. Locate the CLI objectively (search Program Files roots)
        $cliHits = @(Get-ChildItem 'C:\Program Files', 'C:\Program Files (x86)' `
            -Filter 'PathVeer.Cli.exe' -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object FullName, Length, LastWriteTime)
        $whereCli = & where.exe PathVeer.Cli.exe 2>$null

        # E. Tray location
        $trayHits = @(Get-ChildItem 'C:\Program Files', 'C:\Program Files (x86)' `
            -Filter 'PathVeer.Tray.exe' -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object FullName, Length, LastWriteTime)

        # F. Install manifest (authoritative paths)
        $manifestPath = Join-Path $installRoot 'install-manifest.json'
        $manifest = $null
        if (Test-Path $manifestPath) {
            try { $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json } catch {}
        }

        # G. Staging dir remnants?
        $staging = Join-Path $installRoot '.staging'
        $stagingPresent = Test-Path $staging

        # H. ProgramData state
        $programData = Test-Path "$env:ProgramData\PathVeer"

        [PSCustomObject]@{
            installRoot   = $installRoot
            service       = $service
            appsAndFeatures = $apps
            fileTree      = $tree
            cliHits       = $cliHits
            whereCli      = $whereCli
            trayHits      = $trayHits
            installManifest = $manifest
            stagingDirPresent = $stagingPresent
            programDataState = $programData
            osVersion     = [System.Environment]::OSVersion.VersionString
            dotnetCoreRuntimes = @(Get-ChildItem 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\shared\Microsoft.NETCore.App' -ErrorAction SilentlyContinue | ForEach-Object { $_.PSChildName })
            dotnetDesktopRuntimes = @(Get-ChildItem 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\shared\WindowsDesktop.App' -ErrorAction SilentlyContinue | ForEach-Object { $_.PSChildName })
        }
    }

    $outDir = Join-Path $PSScriptRoot '..\..\artifacts\certification\guest-state'
    $outDir = [System.IO.Path]::GetFullPath($outDir)
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $outFile = Join-Path $outDir "$stamp-guest-install-state.json"
    $state | ConvertTo-Json -Depth 8 | Set-Content -Path $outFile -Encoding UTF8

    Write-Host ''
    Write-Host "Guest install-state captured -> $outFile" -ForegroundColor Green
    Write-Host ''
    Write-Host ($state | Format-List | Out-String)
} finally {
    if ($session) { Remove-PSSession $session -ErrorAction SilentlyContinue }
}
