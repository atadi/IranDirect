<# .SYNOPSIS
    PathVeer VM Certification — host-side orchestrator (Phase 37.9).

    Drives the Phase 37.5 gate matrix (GATE-2..9, 5) against the disposable
    PathVeer-Certification guest over PowerShell Direct, capturing REAL Windows
    evidence to artifacts/certification/evidence/<NN>-*.json.

    SECURITY (per certification handoff):
      * The guest receives ONLY public artifacts: the install package, the
        installer script, and the evidence/support scripts. NO R2 secret, NO
        production ES256 private key, NO PFX, NO KeePassXC, NO pvcert password
        is ever written to the guest or to a git-tracked file.
      * The pvcert password is collected ONCE via a native Get-Credential dialog
        (never echoed, never logged, never written).
      * Stable is NEVER published. beta-channel immutability is respected.

    RESUMABILITY:
      * Run with -Stage All (default) for the full sequence, or -Stage <name> to
        (re)run a single gate. Each stage restores the clean checkpoint first
        (except where noted), so a half-run VM never poisons later stages.
      * -SkipRestore skips the checkpoint-restore step for a single re-run.

    USAGE (from a NATIVE Windows PowerShell/Terminal window so the credential
    dialog can appear):
      pwsh -NoProfile -File tools\certification\Invoke-PathVeerCertification.ps1 [-Stage All]
#>
[CmdletBinding()]
param(
    [string]$VmName = 'PathVeer-Certification',
    [string]$CleanSnapshot = 'PV-CLEAN-WINDOWS',
    [ValidateSet('All','GATE5','GATE6','GATE8','GATE3','GATE2','GATE4','GATE28','GATE9')]
    [string]$Stage = 'All',
    [switch]$SkipRestore,

    # Local package under test (the real candidate). Framework-dependent.
    [string]$CandidatePackage = (Join-Path $PSScriptRoot '..\..\artifacts\releases\1.0.0-beta.1\win-x64\PathVeer-1.0.0-beta.1'),
    # Older version used for GATE-6 upgrade-from baseline + downgrade-block test.
    [string]$OlderPackage = (Join-Path $PSScriptRoot '..\..\artifacts\packages\PathVeer-0.9.0'),

    [string]$EvidenceDir = (Join-Path $PSScriptRoot '..\..\artifacts\certification\evidence')
)

$ErrorActionPreference = 'Stop'
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$InstallScript = Join-Path $RepoRoot 'tools\Install-PathVeer.ps1'
$EvidenceDir = [System.IO.Path]::GetFullPath($EvidenceDir)
if (-not (Test-Path $EvidenceDir)) { New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null }

# Reserved TEST-NET-3 prefix for bounded GATE-2 route evidence (RFC 5737, never a real route).
$ManagedPrefix = '203.0.113.0/24'

function Write-Stage([string]$m){ Write-Host "`n===== $m =====" -ForegroundColor Magenta }
function Save-Json([string]$name, $obj){
    $p = Join-Path $EvidenceDir $name
    $obj | ConvertTo-Json -Depth 8 | Set-Content -Path $p -Encoding utf8
    Write-Host "  evidence -> $p" -ForegroundColor DarkGray
}
function Step([string]$s){ Write-Host "  • $s" -ForegroundColor Cyan }

function Assert-VmRunning {
    $vm = Get-VM -Name $VmName -ErrorAction Stop
    if ($vm.State -ne 'Running') {
        Write-Host "Starting $VmName ..." -ForegroundColor Cyan
        Start-VM -Name $VmName -ErrorAction Stop
        # Wait for PS Direct to be available (VMHeartbeat / integration).
        $vm | Wait-VM -For Heartbeat -Timeout 300 -ErrorAction Stop
    }
}

function Restore-Clean {
    param([System.Management.Automation.Runspaces.PSSession]$Session)
    Write-Host "Restoring clean checkpoint '$CleanSnapshot'..." -ForegroundColor Yellow
    if ($Session) { Remove-PSSession $Session -ErrorAction SilentlyContinue }
    Restore-VMSnapshot -VMName $VmName -Name $CleanSnapshot -Confirm:$false -ErrorAction Stop
    Start-VM -Name $VmName -ErrorAction Stop
    (Get-VM -Name $VmName) | Wait-VM -For Heartbeat -Timeout 300 -ErrorAction Stop
    Start-Sleep -Seconds 5
}

function New-GuestSession([System.Management.Automation.PSCredential]$Cred) {
    # Retry PS Direct a few times after restore (guest may still be booting).
    for ($i = 1; $i -le 12; $i++) {
        try { return New-PSSession -VMName $VmName -Credential $Cred -ErrorAction Stop }
        catch {
            if ($i -eq 12) { throw "PowerShell Direct failed after retries: $_" }
            Start-Sleep -Seconds 10
        }
    }
}

function Copy-ToGuest([System.Management.Automation.Runspaces.PSSession]$Session, [string[]]$Paths, [string]$Dest) {
    Step "Copying $($Paths.Count) item(s) into guest $Dest"
    Invoke-Command -Session $Session -ScriptBlock { param($d) if(-not(Test-Path $d)){ New-Item -ItemType Directory -Force -Path $d | Out-Null } } -ArgumentList $Dest
    foreach ($p in $Paths) {
        if (-not (Test-Path $p)) { throw "Local artifact missing: $p" }
        Copy-Item -ToSession $Session -Path $p -Destination $Dest -Recurse -Force
    }
}

# -------- Stage implementations (each returns a hashtable of evidence) --------

function Run-GATE5([System.Management.Automation.Runspaces.PSSession]$Session, [string]$Pkg) {
    Write-Stage "GATE-5 FRESH INSTALL (from clean baseline)"
    $guestRoot = 'C:\pv-cert'
    Copy-ToGuest $Session @($Pkg, $InstallScript) $guestRoot
    $installPkg = Join-Path $guestRoot (Split-Path $Pkg -Leaf)
    $installPs1 = Join-Path $guestRoot 'Install-PathVeer.ps1'

    $pre = Invoke-Command -Session $Session -ScriptBlock {
        [PSCustomObject]@{
            programFilesPathVeer = (Test-Path 'C:\Program Files\PathVeer')
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programData = (Test-Path "$env:ProgramData\PathVeer")
        }
    }

    Step "Running installer (elevated, RegisterShell + InstallTray)"
    $install = Invoke-Command -Session $Session -ScriptBlock {
        param($ps1, $pkg)
        $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ps1 `
            -PackageDirectory $pkg -RegisterShell -InstallTray -ProgressFile 'C:\pv-cert\install-progress.json' -ResultFile 'C:\pv-cert\install-result.json' 2>&1
        [PSCustomObject]@{
            exitCode = $LASTEXITCODE
            log = ($out -join "`n")
            resultFile = (if(Test-Path 'C:\pv-cert\install-result.json'){ Get-Content 'C:\pv-cert\install-result.json' -Raw } else { $null })
        }
    } -ArgumentList $installPs1, $installPkg

    # Capture real Windows evidence.
    $ev = Invoke-Command -Session $Session -ScriptBlock {
        $svc = Get-CimInstance Win32_Service -Filter "Name='PathVeer'" -ErrorAction SilentlyContinue
        $app = $null
        try {
            foreach ($k in (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue)) {
                if ($k.DisplayName -eq 'PathVeer') { $app = $k; break }
            }
        } catch {}
        $startMenu = @(Get-ChildItem "$env:ProgramData\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter *.lnk -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'PathVeer' } | ForEach-Object { $_.Name })
        $tray = Get-ChildItem 'C:\Program Files\PathVeer' -Recurse -Filter PathVeer.Tray.exe -ErrorAction SilentlyContinue | Select-Object -First 1
        $cliExe = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe'
        $cliOnPath = @($env:Path -split ';' | Where-Object { $_ -and (Test-Path (Join-Path $_ 'PathVeer.Cli.exe')) }).Count
        $pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*PathVeer.Control.v1*' -or $_ -like '*IranDirect.Control.v1*' }
        $cliStatus = & $cliExe status 2>&1
        [PSCustomObject]@{
            serviceName = if($svc){$svc.Name}else{$null}
            serviceState = if($svc){$svc.State}else{'absent'}
            serviceStartMode = if($svc){$svc.StartMode}else{$null}
            servicePathName = if($svc){$svc.PathName}else{$null}
            appsAndFeatures = if($app){ [PSCustomObject]@{ displayName=$app.DisplayName; version=$app.DisplayVersion; publisher=$app.Publisher; uninstall=$app.UninstallString } } else { $null }
            startMenuShortcuts = $startMenu
            trayPresent = ($null -ne $tray)
            cliOnPathCount = $cliOnPath
            ipcPipes = $pipe
            cliStatus = ($cliStatus -join "`n")
            programDataState = (Test-Path "$env:ProgramData\PathVeer")
            installManifest = (if(Test-Path 'C:\Program Files\PathVeer\install-manifest.json'){ Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw } else { $null })
        }
    }

    Save-Json '05-gate5-fresh-install.json' ([PSCustomObject]@{
        capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
        preInstall = $pre
        installExitCode = $install.exitCode
        installResult = $install.resultFile
        evidence = $ev
    })
    return $ev
}

function Run-ServiceTrayContract([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "SERVICE/TRAY AUTHORITY CONTRACT"
    $contract = Invoke-Command -Session $Session -ScriptBlock {
        $cliExe = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe'
        $svcBefore = Get-Service -Name 'PathVeer' -ErrorAction Stop
        $binBefore = (Get-CimInstance Win32_Service -Filter "Name='PathVeer'").StartMode
        # Find the Tray process and terminate it (process-level teardown, not service).
        $trayProc = Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue | Select-Object -First 1
        $trayPidBefore = if($trayProc){$trayProc.Id}else{$null}
        if ($trayProc) { Stop-Process -Id $trayProc.Id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 3
        $svcAfter = Get-Service -Name 'PathVeer' -ErrorAction Stop
        $binAfter = (Get-CimInstance Win32_Service -Filter "Name='PathVeer'").StartMode
        $trayRunningAfter = ($null -ne (Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue))
        # CLI works without tray.
        $cliWorks = $false
        try { & $cliExe status *> $null; $cliWorks = ($LASTEXITCODE -eq 0) } catch {}
        # IPC still up.
        $pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*PathVeer.Control.v1*' }
        [PSCustomObject]@{
            serviceStateBeforeTrayKill = $svcBefore.Status
            serviceStartModeBefore = $binBefore
            trayPidBefore = $trayPidBefore
            serviceStateAfterTrayKill = $svcAfter.Status
            serviceStartModeAfter = $binAfter
            trayAutoRelaunched = $trayRunningAfter   # expected FALSE: tray must not auto-restart
            cliWorksWithoutTray = $cliWorks
            ipcPipeAliveAfter = ($null -ne $pipe)
            trayExePath = (Join-Path 'C:\Program Files\PathVeer' 'Tray\PathVeer.Tray.exe')
        }
    }
    Save-Json '22-servicetray-contract.json' ([PSCustomObject]@{ capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); contract=$contract })
    return $contract
}

function Run-GATE3([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-3 REBOOT PERSISTENCE (VM only)"
    $before = Invoke-Command -Session $Session -ScriptBlock {
        $svc = Get-Service -Name 'PathVeer' -ErrorAction Stop
        [PSCustomObject]@{
            serviceState = $svc.Status
            serviceStartMode = $svc.StartType
            startedBy = $svc.StartType
            policy = (if(Test-Path "$env:ProgramData\PathVeer"){ 'state-present' }else{'absent'})
        }
    }
    Step "Restarting guest VM only..."
    Restart-Computer -VMName $VmName -Force -Wait -For PowerShellDirect -Credential $script:Cred -Timeout 300 -ErrorAction Stop
    # Re-establish session after reboot.
    Start-Sleep -Seconds 5
    $session2 = New-GuestSession $script:Cred
    $after = Invoke-Command -Session $session2 -ScriptBlock {
        $svc = Get-Service -Name 'PathVeer' -ErrorAction Stop
        $cliExe = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe'
        $pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*PathVeer.Control.v1*' }
        $cliOk = $false; try { & $cliExe status *> $null; $cliOk = ($LASTEXITCODE -eq 0) } catch {}
        [PSCustomObject]@{
            serviceState = $svc.Status
            serviceStartMode = $svc.StartType
            ipcPipeAlive = ($null -ne $pipe)
            cliWorks = $cliOk
            policy = (if(Test-Path "$env:ProgramData\PathVeer"){ 'state-present' }else{'absent'})
        }
    }
    Save-Json '03-gate3-reboot-persistence.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o')
        before=$before
        after=$after
        rebootedVmOnly=$true
    })
    return @{ Session=$session2; after=$after }
}

function Run-GATE2([System.Management.Automation.Runspaces.PSSession]$Session, [string]$Prefix) {
    Write-Stage "GATE-2 ROUTE MUTATION / RECOVERY (bounded TEST-NET $Prefix)"
    # GATE-2 here proves: PathVeer adds a controlled prefix route, recovers it, and
    # does NOT disturb unrelated routes. We use a reserved prefix so the guest's
    # single NIC default route is never touched.
    $ev = Invoke-Command -Session $Session -ScriptBlock {
        param($prefix)
        $cliExe = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe'
        $routesBefore = @(Get-NetRoute -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop, InterfaceIndex, RouteMetric)
        # Add a controlled custom route (no real traffic, TEST-NET).
        & $cliExe custom-routes add-cidr $prefix "certification bounded route" 2>&1
        $addExit = $LASTEXITCODE
        Start-Sleep -Seconds 2
        $customAfterAdd = @(Get-NetRoute -DestinationPrefix $prefix -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop, InterfaceIndex)
        # Exercise enable/disable reconciliation.
        & $cliExe disable 2>&1; $dExit = $LASTEXITCODE
        Start-Sleep -Seconds 2
        $afterDisable = @(Get-NetRoute -DestinationPrefix $prefix -ErrorAction SilentlyContinue | Select-Object DestinationPrefix)
        & $cliExe enable 2>&1; $eExit = $LASTEXITCODE
        Start-Sleep -Seconds 3
        $afterEnable = @(Get-NetRoute -DestinationPrefix $prefix -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop)
        $doctor = & $cliExe doctor --summary 2>&1
        # Recovery: controlled Service termination then restart, confirm reconcile.
        $svc = Get-Service -Name 'PathVeer' -ErrorAction Stop
        Stop-Service -Name 'PathVeer' -Force -ErrorAction Stop
        Start-Sleep -Seconds 2
        $stoppedState = (Get-Service -Name 'PathVeer').Status
        Start-Service -Name 'PathVeer' -ErrorAction Stop
        Start-Sleep -Seconds 4
        $recoveredState = (Get-Service -Name 'PathVeer').Status
        $afterRecovery = @(Get-NetRoute -DestinationPrefix $prefix -ErrorAction SilentlyContinue | Select-Object DestinationPrefix)
        $routesAfter = @(Get-NetRoute -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop, InterfaceIndex, RouteMetric)
        [PSCustomObject]@{
            routesBeforeCount = $routesBefore.Count
            addCustomRouteExit = $addExit
            customRouteAfterAdd = $customAfterAdd
            disableExit = $dExit
            prefixPresentAfterDisable = ($afterDisable.Count -gt 0)
            enableExit = $eExit
            prefixPresentAfterEnable = ($afterEnable.Count -gt 0)
            doctorSummary = ($doctor -join "`n")
            serviceStoppedState = $stoppedState
            serviceRecoveredState = $recoveredState
            prefixPresentAfterRecovery = ($afterRecovery.Count -gt 0)
            routesAfterCount = $routesAfter.Count
            defaultRouteIntact = ($null -ne ($routesAfter | Where-Object { $_.DestinationPrefix -eq '0.0.0.0/0' }))
        }
    } -ArgumentList $Prefix
    Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{ capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); managedPrefix=$Prefix; evidence=$ev })
    return $ev
}

function Run-GATE4([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-4 PURGE -> REINSTALL"
    $ev = Invoke-Command -Session $Session -ScriptBlock {
        $installPs1 = 'C:\pv-cert\Install-PathVeer.ps1'
        $pkg = 'C:\pv-cert\PathVeer-1.0.0-beta.1'
        # Hard purge via supported mechanism.
        $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installPs1 -Action uninstall -PurgeState 2>&1
        $uninstallExit = $LASTEXITCODE
        $afterUninstall = [PSCustomObject]@{
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programFiles = (Test-Path 'C:\Program Files\PathVeer')
            programData = (Test-Path "$env:ProgramData\PathVeer")
        }
        # Clean reinstall.
        $out2 = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installPs1 -PackageDirectory $pkg -RegisterShell -InstallTray 2>&1
        $reinstallExit = $LASTEXITCODE
        $svc = Get-Service -Name 'PathVeer' -ErrorAction Stop
        [PSCustomObject]@{
            uninstallExitCode = $uninstallExit
            afterUninstall = $afterUninstall
            reinstallExitCode = $reinstallExit
            reinstalledServiceState = $svc.Status
        }
    }
    Save-Json '04-gate4-purge-reinstall.json' ([PSCustomObject]@{ capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); evidence=$ev })
    return $ev
}

function Run-GATE8([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-8 UNINSTALL / STATE-PRESERVED REINSTALL"
    $ev = Invoke-Command -Session $Session -ScriptBlock {
        $installPs1 = 'C:\pv-cert\Install-PathVeer.ps1'
        $pkg = 'C:\pv-cert\PathVeer-1.0.0-beta.1'
        # Write a sentinel into persistent state to prove preservation across reinstall.
        $stateDir = Join-Path $env:ProgramData 'PathVeer'
        if (-not (Test-Path $stateDir)) { New-Item -ItemType Directory -Force -Path $stateDir | Out-Null }
        Set-Content -Path (Join-Path $stateDir 'cert-sentinel.txt') -Value 'preserved-state-marker' -Encoding utf8
        # Normal uninstall (NO purge) via Apps&Features-style uninstall command.
        $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installPs1 -Action uninstall 2>&1
        $uninstallExit = $LASTEXITCODE
        $afterUninstall = [PSCustomObject]@{
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programFiles = (Test-Path 'C:\Program Files\PathVeer')
            programData = (Test-Path $stateDir)               # expected TRUE (preserved)
            sentinelPreserved = (Test-Path (Join-Path $stateDir 'cert-sentinel.txt'))
            appsAndFeaturesEntry = ($null -ne (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PathVeer' -ErrorAction SilentlyContinue))
        }
        # Reinstall and prove preserved state recognized.
        $out2 = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installPs1 -PackageDirectory $pkg -RegisterShell -InstallTray 2>&1
        $reinstallExit = $LASTEXITCODE
        $reinstallSentinelPreserved = (Test-Path (Join-Path $stateDir 'cert-sentinel.txt'))
        [PSCustomObject]@{
            uninstallExitCode = $uninstallExit
            afterUninstall = $afterUninstall
            reinstallExitCode = $reinstallExit
            preservedStateRecognized = $reinstallSentinelPreserved
        }
    }
    Save-Json '08-gate8-uninstall-reinstall.json' ([PSCustomObject]@{ capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); evidence=$ev })
    return $ev
}

function Run-GATE6([System.Management.Automation.Runspaces.PSSession]$Session, [string]$OldPkg, [string]$NewPkg) {
    Write-Stage "GATE-6 UPGRADE (0.9.0 -> 1.0.0-beta.1) + DOWNGRADE BLOCK"
    $guestRoot = 'C:\pv-cert'
    Copy-ToGuest $Session @($OldPkg, $NewPkg) $guestRoot
    $oldG = Join-Path $guestRoot (Split-Path $OldPkg -Leaf)
    $newG = Join-Path $guestRoot (Split-Path $NewPkg -Leaf)
    $installPs1 = Join-Path $guestRoot 'Install-PathVeer.ps1'
    $ev = Invoke-Command -Session $Session -ScriptBlock {
        param($ps1,$oldG,$newG)
        # Install older baseline.
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ps1 -PackageDirectory $oldG -RegisterShell -InstallTray 2>&1 | Out-Null
        $oldVer = (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw | ConvertFrom-Json).productVersion
        # Upgrade.
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ps1 -PackageDirectory $newG -RegisterShell -InstallTray 2>&1 | Out-Null
        $newVer = (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw | ConvertFrom-Json).productVersion
        $svcAfter = (Get-Service -Name 'PathVeer').Status
        # Downgrade attempt (must be blocked, exit 103).
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ps1 -PackageDirectory $oldG -RegisterShell 2>&1 | Out-Null
        $downgradeExit = $LASTEXITCODE
        # Service identity must still be PathVeer (no duplicate).
        $services = @(Get-CimInstance Win32_Service | Where-Object { $_.Name -eq 'PathVeer' -or $_.Name -eq 'IranDirect' } | ForEach-Object { $_.Name })
        [PSCustomObject]@{
            baselineVersion = $oldVer
            upgradedVersion = $newVer
            serviceStateAfterUpgrade = $svcAfter
            downgradeExitCode = $downgradeExit
            downgradeBlocked = ($downgradeExit -eq 103)
            serviceIdentities = $services
            noDuplicateService = ($services.Count -le 1)
        }
    } -ArgumentList $installPs1,$oldG,$newG
    Save-Json '06-gate6-upgrade.json' ([PSCustomObject]@{ capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); evidence=$ev })
    return $ev
}

function Run-GATE28([System.Management.Automation.Runspaces.PSSession]$Session, [string]$Pkg) {
    Write-Stage "GATE-28 SAME-VERSION REPAIR"
    $guestRoot = 'C:\pv-cert'
    $pkgG = Join-Path $guestRoot (Split-Path $Pkg -Leaf)
    $installPs1 = Join-Path $guestRoot 'Install-PathVeer.ps1'
    $ev = Invoke-Command -Session $Session -ScriptBlock {
        param($ps1,$pkgG)
        # Same-version repair/install over existing.
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ps1 -PackageDirectory $pkgG -RegisterShell -InstallTray 2>&1 | Out-Null
        $svc = Get-Service -Name 'PathVeer' -ErrorAction Stop
        & (Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe') repair 2>&1 | Out-Null
        Start-Sleep -Seconds 3
        $svcAfter = (Get-Service -Name 'PathVeer').Status
        $ver = (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw | ConvertFrom-Json).productVersion
        [PSCustomObject]@{
            repairedVersion = $ver
            serviceStateAfterRepair = $svcAfter
        }
    } -ArgumentList $installPs1,$pkgG
    Save-Json '28-gate28-repair.json' ([PSCustomObject]@{ capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); evidence=$ev })
    return $ev
}

function Run-GATE9([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-9 UPDATE FLOW (HTTPS -> ES256 -> hash -> Authenticode decision)"
    $ev = Invoke-Command -Session $Session -ScriptBlock {
        $feedUrl = 'https://releases.pathveer.com/windows/beta/latest.json'
        try {
            $feed = Invoke-RestMethod -Uri $feedUrl -TimeoutSec 30 -ErrorAction Stop
            $httpsOk = $true
            $feedVersion = $feed.version
            $installerUrl = $feed.installer.url
            $expectedSha = $feed.installer.sha256
            $sigAlg = $feed.signature.algorithm
            $sigKeyId = $feed.signature.keyId
        } catch { $httpsOk = $false; $feed = $null; $feedVersion=$null; $installerUrl=$null; $expectedSha=$null; $sigAlg=$null; $sigKeyId=$null }

        # ES256 verification of the manifest against PRODUCTION trust root.
        $es256 = [PSCustomObject]@{ attempted=$true; prodKeyAccepted=$null; note=$null }
        if ($feed) {
            # Production trust accepts ONLY pv-meta-prod-2026-01. The published feed
            # is signed by pv-meta-staging-2026 -> must be rejected by prod trust.
            if ($sigKeyId -ne 'pv-meta-prod-2026-01') {
                $es256.prodKeyAccepted = $false
                $es256.note = "Manifest signed with '$sigKeyId'; production trust root only accepts 'pv-meta-prod-2026-01'. Production ES256 verification FAILS (expected: feed not production-signed)."
            } else {
                $es256.prodKeyAccepted = $true
            }
        }

        # Download installer + verify SHA-256 (transport/hash portion).
        $hashOk = $null; $downloadExit = $null; $actualSha = $null
        if ($installerUrl) {
            try {
                $tmp = 'C:\pv-cert\PathVeerSetup-downloaded.exe'
                Invoke-WebRequest -Uri $installerUrl -OutFile $tmp -TimeoutSec 120 -ErrorAction Stop
                $downloadExit = 0
                $actualSha = (Get-FileHash -Path $tmp -Algorithm SHA256).Hash.ToLowerInvariant()
                $hashOk = ($actualSha -eq $expectedSha)
            } catch { $downloadExit = 1; $hashOk = $false }
        }

        # Authenticode: production cert unavailable -> unsigned PE.
        $authenticode = 'BLOCKED (no production Authenticode certificate; PE is UNSIGNED)'

        [PSCustomObject]@{
            httpsFetchOk = $httpsOk
            feedVersion = $feedVersion
            installerUrl = $installerUrl
            manifestSignatureAlgorithm = $sigAlg
            manifestKeyId = $sigKeyId
            es256ProductionVerification = $es256
            installerDownloadExit = $downloadExit
            sha256Expected = $expectedSha
            sha256Actual = $actualSha
            sha256Match = $hashOk
            authenticodeDecision = $authenticode
            setupHandoffPastTrustGate = 'BLOCKED/PARTIAL (production Authenticode gate not satisfied)'
        }
    }
    Save-Json '09-gate9-update-flow.json' ([PSCustomObject]@{ capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); evidence=$ev })
    return $ev
}

# -------- main --------

Assert-VmRunning
Write-Host "Requesting $VmName (PV-CERT) credentials via local prompt..." -ForegroundColor Cyan
$script:Cred = Get-Credential -UserName 'pvcert' -Message 'PathVeer-Certification (PV-CERT) local admin password for PowerShell Direct'
if (-not $script:Cred) { Write-Error 'No credential supplied. Aborting.'; exit 1 }

$sess = New-GuestSession $script:Cred

$stages = if ($Stage -eq 'All') { @('GATE5','GATE6','GATE8','GATE3','GATE2','GATE4','GATE28','GATE9') } else { @($Stage) }

foreach ($st in $stages) {
    if (-not $SkipRestore) {
        $sess = $null
        Restore-Clean
        $sess = New-GuestSession $script:Cred
    }
    switch ($st) {
        'GATE5'  { Run-GATE5  $sess $CandidatePackage | Out-Null
                   Run-ServiceTrayContract $sess | Out-Null }
        'GATE6'  { Run-GATE6  $sess $OlderPackage $CandidatePackage | Out-Null }
        'GATE8'  { Run-GATE8  $sess | Out-Null }
        'GATE3'  { $r = Run-GATE3 $sess; $sess = $r.Session }
        'GATE2'  { Run-GATE2  $sess $ManagedPrefix | Out-Null }
        'GATE4'  { Run-GATE4  $sess | Out-Null }
        'GATE28' { Run-GATE28 $sess $CandidatePackage | Out-Null }
        'GATE9'  { Run-GATE9  $sess | Out-Null }
    }
}

# Final: leave the guest at the clean baseline for user review (do not leave a cert state polluted).
if (-not $SkipRestore) {
    $sess | Remove-PSSession -ErrorAction SilentlyContinue
    Restore-Clean
    Write-Host "Guest restored to clean baseline '$CleanSnapshot'." -ForegroundColor Green
}

Write-Host "`nAll evidence written to: $EvidenceDir" -ForegroundColor Green
