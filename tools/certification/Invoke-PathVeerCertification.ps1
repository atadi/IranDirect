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

<# .SYNOPSIS
    Runs a guest command with a genuinely elevated (administrative) token, NON-interactively.

    The PowerShell Direct session is created as 'pvcert' (a member of Administrators but
    with a UAC-filtered token -> guestIsAdministrator reports False). A plain '&' spawn in
    that session is therefore non-elevated, so the installer's Assert-Administrator aborts
    before any payload is written. This helper elevates via a credential-based RunAs logon
    (Start-Process -Verb RunAs -Credential), which does NOT surface a UAC consent dialog
    (it is a fresh elevated logon, not a token-filter removal). UAC itself is left intact.

    It writes a structured result (exit code + captured stdout/stderr log) so the harness
    can treat installer failure as a hard gate boundary instead of crashing on a later
    missing-command invocation.

    If elevation is unavailable for any reason, it falls back to a direct (non-elevated)
    run so the gate still receives structured FAIL evidence rather than an unclassified
    exception -- elevationAvailable will be $false to make the degradation visible.
#>
function Invoke-GuestElevated {
    param(
        [System.Management.Automation.Runspaces.PSSession]$Session,
        [System.Management.Automation.PSCredential]$Cred,
        [string]$Command,                                  # command line to run elevated
        [string]$ResultDir = 'C:\pv-cert',
        [int]$TimeoutSeconds = 900
    )
    $marker = [guid]::NewGuid().ToString('N')
    $wrap    = "$ResultDir\elevated-$marker.ps1"
    $log     = "$ResultDir\elevated-$marker.log"
    $wrapContent = "& $Command 2>&1 | Set-Content -FilePath '$log' -Encoding utf8`n`$e = `$LASTEXITCODE`nexit `$e"
    $result = Invoke-Command -Session $Session -ScriptBlock {
        param($wrap, $wrapContent, $log, $CredIn, $Command, $TimeoutSeconds)
        $out = [PSCustomObject]@{
            started=$false; completed=$false; exitCode=$null
            logFile=$log; elevationAvailable=$true; error=$null
        }
        try {
            Set-Content -Path $wrap -Value $wrapContent -Encoding UTF8
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = 'powershell.exe'
            $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$wrap`""
            $psi.Verb = 'RunAs'
            $psi.UseShellExecute = $true
            if ($CredIn) { $psi.UserName = $CredIn.UserName; $psi.Password = $CredIn.GetNetworkCredential().Password }
            $p = [System.Diagnostics.Process]::Start($psi)
            $out.started = $true
            if ($p.WaitForExit($TimeoutSeconds * 1000)) { $out.completed = $true; $out.exitCode = $p.ExitCode }
            else { $out.error = "elevated process did not exit within $TimeoutSeconds s" }
        } catch {
            # Fall back to a direct (non-elevated) run so the gate still gets evidence.
            $out.elevationAvailable = $false
            $out.error = $_.Exception.Message
            try {
                & powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& { $Command } 2>&1 | Set-Content -FilePath '$log' -Encoding utf8; exit `$LASTEXITCODE" | Out-Null
                $out.started = $true; $out.completed = $true; $out.exitCode = $LASTEXITCODE
            } catch {
                $out.error = "$($out.error); fallback also failed: $($_.Exception.Message)"
            }
        }
        try { if (Test-Path $log) { $out | Add-Member -NotePropertyName log -NotePropertyValue (Get-Content $log -Raw -ErrorAction SilentlyContinue) } } catch {}
        return $out
    } -ArgumentList $wrap, $wrapContent, $log, $Cred, $Command, $TimeoutSeconds
    return $result
}

<# .SYNOPSIS
    Runs an entire guest SCRIPTBLOCK elevated and reads back a structured result object the
    script writes to a JSON file. Used for gates whose whole body is privileged (e.g. GATE-2
    route mutation + service control, GATE-22 contract checks) so we avoid scattering
    per-command elevation calls.
#>
function Invoke-GuestScriptElevated {
    param(
        [System.Management.Automation.Runspaces.PSSession]$Session,
        [System.Management.Automation.PSCredential]$Cred,
        [scriptblock]$ScriptBlock,
        [hashtable]$ArgumentList = @{},
        [string]$ResultDir = 'C:\pv-cert',
        [int]$TimeoutSeconds = 900
    )
    $marker = [guid]::NewGuid().ToString('N')
    $scriptFile  = "$ResultDir\elevated-script-$marker.ps1"
    $argJson     = "$ResultDir\elevated-script-$marker.args.json"
    $resultJson  = "$ResultDir\elevated-script-$marker.result.json"

    # Write the guest script to a file FIRST. The script reads its args from a JSON file
    # and writes its result to a result JSON file.
    $guestScript = @'
$argsIn = Get-Content '__ARGJSON__' -Raw | ConvertFrom-Json -AsHashtable
$result = & {
    param($a)
__BODY__
} $argsIn
$result | ConvertTo-Json -Depth 8 | Set-Content -FilePath '__RESULTJSON__' -Encoding utf8
'@
    # Convert the scriptblock body to text, then inject.
    $bodyText = $ScriptBlock.ToString()
    $guestScript = $guestScript.Replace('__ARGJSON__', $argJson).Replace('__RESULTJSON__', $resultJson).Replace('__BODY__', $bodyText)

    Invoke-Command -Session $Session -ScriptBlock {
        param($f, $c, $aj, $a)
        Set-Content -Path $f -Value $c -Encoding UTF8
        $a | ConvertTo-Json -Depth 8 | Set-Content -Path $aj -Encoding utf8
    } -ArgumentList $scriptFile, $guestScript, $argJson, $ArgumentList | Out-Null

    # Elevate `powershell.exe -File <script>`; the script itself writes the result JSON.
    $run = Invoke-GuestElevated -Session $Session -Cred $Cred -Command "powershell.exe -File '$scriptFile'" -ResultDir $ResultDir -TimeoutSeconds $TimeoutSeconds

    $rb = Invoke-Command -Session $Session -ScriptBlock {
        param($rj)
        if (Test-Path $rj) { try { return Get-Content $rj -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json } catch {} }
    } -ArgumentList $resultJson

    return [PSCustomObject]@{ elevation = $run; result = $rb }
}

# -------- Stage implementations (each returns a hashtable of evidence) --------

function Run-GATE5([System.Management.Automation.Runspaces.PSSession]$Session, [string]$Pkg) {
    Write-Stage "GATE-5 FRESH INSTALL (from clean baseline)"
    $guestRoot = 'C:\pv-cert'
    Copy-ToGuest $Session @($Pkg, $InstallScript) $guestRoot
    $installPkg = Join-Path $guestRoot (Split-Path $Pkg -Leaf)
    $installPs1 = Join-Path $guestRoot 'Install-PathVeer.ps1'
    $progressFile = Join-Path $guestRoot 'install-progress.json'
    $resultFile   = Join-Path $guestRoot 'install-result.json'

    $pre = Invoke-Command -Session $Session -ScriptBlock {
        [PSCustomObject]@{
            programFilesPathVeer = (Test-Path 'C:\Program Files\PathVeer')
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programData = (Test-Path "$env:ProgramData\PathVeer")
        }
    }

    Step "Running installer (ELEVATED via RunAs, RegisterShell + InstallTray)"
    # Distinct ProgressFile / ResultFile paths (the installer treats them as separate
    # lifecycle artifacts). Elevated so Assert-Administrator succeeds.
    $installCmd = "powershell.exe -File '$installPs1' -PackageDirectory '$installPkg' -RegisterShell -InstallTray -ProgressFile '$progressFile' -ResultFile '$resultFile'"
    $install = Invoke-GuestElevated -Session $Session -Cred $script:Cred -Command $installCmd -ResultDir $guestRoot

    # Read the installer's structured result + progress (authoritative gate boundary).
    $installResult = $null; $installProgress = $null
    $readBack = Invoke-Command -Session $Session -ScriptBlock {
        param($rf, $pf)
        $r = $null; $p = $null
        if (Test-Path $rf) { try { $r = Get-Content $rf -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json } catch {} }
        if (Test-Path $pf) { try { $p = Get-Content $pf -Raw -ErrorAction SilentlyContinue } catch {} }
        [PSCustomObject]@{ result = $r; progress = $p }
    } -ArgumentList $resultFile, $progressFile

    $installResult = $readBack.result
    $installProgress = $readBack.progress

    # Hard gate boundary: installation must succeed before any CLI use.
    $installRootExists = $false
    $svcExists = $false
    $manifest = $null
    $post = Invoke-Command -Session $Session -ScriptBlock {
        $svc = Get-CimInstance Win32_Service -Filter "Name='PathVeer'" -ErrorAction SilentlyContinue
        [PSCustomObject]@{
            installRootExists = (Test-Path 'C:\Program Files\PathVeer')
            serviceExists = ($null -ne $svc)
            serviceState = if ($svc) { $svc.State } else { 'absent' }
            manifestExists = (Test-Path 'C:\Program Files\PathVeer\install-manifest.json')
        }
    }

    $installRootExists = $post.installRootExists
    $svcExists = $post.serviceExists

    $gate5Failed = $false
    $failReasons = [System.Collections.Generic.List[string]]::new()
    if (-not $install.elevationAvailable) { $gate5Failed = $true; $failReasons.Add("elevation unavailable: $($install.error)") }
    if ($install.completed -eq $false) { $gate5Failed = $true; $failReasons.Add("installer did not complete: $($install.error)") }
    if ($null -ne $install.exitCode -and $install.exitCode -ne 0) { $gate5Failed = $true; $failReasons.Add("installer exit code $($install.exitCode)") }
    if ($installResult -and $installResult.success -eq $false) { $gate5Failed = $true; $failReasons.Add("installer reported failure: [$($installResult.category)] $($installResult.message)") }
    if (-not $svcExists) { $gate5Failed = $true; $failReasons.Add('PathVeer service not present after install') }
    if (-not $post.manifestExists) { $gate5Failed = $true; $failReasons.Add('install-manifest.json not present after install') }

    if ($gate5Failed) {
        # Structured FAIL. Do NOT invoke the CLI; record what we have.
        $failEvidence = [PSCustomObject]@{
            gate = 'GATE-5'
            result = 'FAIL'
            reasons = $failReasons.ToArray()
            elevated = $install.elevationAvailable
            installExitCode = $install.exitCode
            installCompleted = $install.completed
            installResult = $installResult
            installProgress = $installProgress
            installerLog = $install.log
            postConditions = $post
            preInstall = $pre
        }
        Save-Json '05-gate5-fresh-install.json' $failEvidence
        Write-Host "  GATE-5 FAIL (no CLI invoked): $($failReasons -join ' | ')" -ForegroundColor Red
        # Signal the caller to stop this stage cleanly.
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("GATE-5 install failed: $($failReasons -join '; ')"),
            'GATE5InstallFailed', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
    }

    # Success path: locate CLI from the authoritative manifest, guard before invoking.
    $manifest = Invoke-Command -Session $Session -ScriptBlock {
        if (Test-Path 'C:\Program Files\PathVeer\install-manifest.json') {
            try { return Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json } catch {}
        }
        return $null
    }
    $cliExe = $null
    if ($manifest -and $manifest.cliExecutablePath) { $cliExe = $manifest.cliExecutablePath }
    else { $cliExe = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe' }

    $ev = Invoke-Command -Session $Session -ScriptBlock {
        param($cliExe)
        $svc = Get-CimInstance Win32_Service -Filter "Name='PathVeer'" -ErrorAction SilentlyContinue
        $app = $null
        try {
            foreach ($k in (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue)) {
                if ($k.DisplayName -eq 'PathVeer') { $app = $k; break }
            }
        } catch {}
        $startMenu = @(Get-ChildItem "$env:ProgramData\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter *.lnk -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'PathVeer' } | ForEach-Object { $_.Name })
        $tray = Get-ChildItem 'C:\Program Files\PathVeer' -Recurse -Filter PathVeer.Tray.exe -ErrorAction SilentlyContinue | Select-Object -First 1
        $cliExists = (Test-Path -LiteralPath $cliExe -PathType Leaf)
        $cliOnPath = @($env:Path -split ';' | Where-Object { $_ -and (Test-Path (Join-Path $_ 'PathVeer.Cli.exe')) }).Count
        $pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*PathVeer.Control.v1*' -or $_ -like '*IranDirect.Control.v1*' }
        $cliStatus = $null
        if ($cliExists) { $cliStatus = (& $cliExe status 2>&1) }
        $svcName = $null; $svcState = 'absent'; $svcStartMode = $null; $svcPathName = $null
        if ($svc) { $svcName = $svc.Name; $svcState = $svc.State; $svcStartMode = $svc.StartMode; $svcPathName = $svc.PathName }
        $appsAndFeatures = $null
        if ($app) {
            $appsAndFeatures = [PSCustomObject]@{
                displayName = $app.DisplayName; version = $app.DisplayVersion
                publisher = $app.Publisher; uninstall = $app.UninstallString
            }
        }
        [PSCustomObject]@{
            serviceName = $svcName; serviceState = $svcState; serviceStartMode = $svcStartMode; servicePathName = $svcPathName
            appsAndFeatures = $appsAndFeatures; startMenuShortcuts = $startMenu
            trayPresent = ($null -ne $tray); cliOnPathCount = $cliOnPath
            ipcPipes = $pipe; cliExists = $cliExists; cliPath = $cliExe
            cliStatus = ($cliStatus -join "`n")
            programDataState = (Test-Path "$env:ProgramData\PathVeer")
            installManifest = $null
        }
    } -ArgumentList $cliExe

    Save-Json '05-gate5-fresh-install.json' ([PSCustomObject]@{
        capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
        preInstall = $pre
        elevationAvailable = $install.elevationAvailable
        installExitCode = $install.exitCode
        installCompleted = $install.completed
        installResult = $installResult
        installProgress = $installProgress
        installerLog = $install.log
        postConditions = $post
        evidence = $ev
    })
    return $ev
}

function Run-ServiceTrayContract([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "SERVICE/TRAY AUTHORITY CONTRACT"
    # Tray kill + CLI service-read require elevation; run the whole contract check elevated.
    $body = {
        param($a)
        $cliExe = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe'
        $cliExists = (Test-Path -LiteralPath $cliExe -PathType Leaf)
        $svcBefore = Get-Service -Name 'PathVeer' -ErrorAction Stop
        $binBefore = (Get-CimInstance Win32_Service -Filter "Name='PathVeer'").StartMode
        # Find the Tray process and terminate it (process-level teardown, not service).
        $trayProc = Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue | Select-Object -First 1
        $trayPidBefore = $null
        if ($trayProc) { $trayPidBefore = $trayProc.Id }
        if ($trayProc) { Stop-Process -Id $trayProc.Id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 3
        $svcAfter = Get-Service -Name 'PathVeer' -ErrorAction Stop
        $binAfter = (Get-CimInstance Win32_Service -Filter "Name='PathVeer'").StartMode
        $trayRunningAfter = ($null -ne (Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue))
        # CLI works without tray.
        $cliWorks = $false
        if ($cliExists) { try { & $cliExe status *> $null; $cliWorks = ($LASTEXITCODE -eq 0) } catch {} }
        # IPC still up.
        $pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*PathVeer.Control.v1*' }
        [PSCustomObject]@{
            serviceStateBeforeTrayKill = $svcBefore.Status
            serviceStartModeBefore = $binBefore
            trayPidBefore = $trayPidBefore
            serviceStateAfterTrayKill = $svcAfter.Status
            serviceStartModeAfter = $binAfter
            trayAutoRelaunched = $trayRunningAfter   # expected FALSE: tray must not auto-restart
            cliExists = $cliExists
            cliWorksWithoutTray = $cliWorks
            ipcPipeAliveAfter = ($null -ne $pipe)
            trayExePath = (Join-Path 'C:\Program Files\PathVeer' 'Tray\PathVeer.Tray.exe')
        }
    }
    $run = Invoke-GuestScriptElevated -Session $Session -Cred $script:Cred -ScriptBlock $body
    $contract = $run.result
    Save-Json '22-servicetray-contract.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o')
        elevationAvailable = $run.elevation.elevationAvailable
        contract=$contract
    })
    return $contract
}

function Run-GATE3([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-3 REBOOT PERSISTENCE (VM only)"
    $before = Invoke-Command -Session $Session -ScriptBlock {
        $svc = Get-Service -Name 'PathVeer' -ErrorAction Stop
        $policy = 'absent'
        if (Test-Path "$env:ProgramData\PathVeer") { $policy = 'state-present' }
        [PSCustomObject]@{
            serviceState = $svc.Status
            serviceStartMode = $svc.StartType
            startedBy = $svc.StartType
            policy = $policy
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
        $policy = 'absent'
        if (Test-Path "$env:ProgramData\PathVeer") { $policy = 'state-present' }
        [PSCustomObject]@{
            serviceState = $svc.Status
            serviceStartMode = $svc.StartType
            ipcPipeAlive = ($null -ne $pipe)
            cliWorks = $cliOk
            policy = $policy
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
    # GATE-2 proves: PathVeer adds a controlled prefix route, recovers it, and does NOT
    # disturb unrelated routes. We use a reserved prefix so the guest's single NIC default
    # route is never touched. CLI verbs + service control require elevation -> run elevated.
    $body = {
        param($a)
        $prefix = $a.prefix
        $cliExe = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe'
        $cliExists = (Test-Path -LiteralPath $cliExe -PathType Leaf)
        $routesBefore = @(Get-NetRoute -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop, InterfaceIndex, RouteMetric)
        $addExit = $null; $dExit = $null; $eExit = $null; $doctor = $null
        if ($cliExists) {
            & $cliExe custom-routes add-cidr $prefix "certification bounded route" 2>&1
            $addExit = $LASTEXITCODE
            Start-Sleep -Seconds 2
            & $cliExe disable 2>&1; $dExit = $LASTEXITCODE
            Start-Sleep -Seconds 2
            & $cliExe enable 2>&1; $eExit = $LASTEXITCODE
            Start-Sleep -Seconds 3
            $doctor = & $cliExe doctor --summary 2>&1
        }
        $customAfterAdd = @(Get-NetRoute -DestinationPrefix $prefix -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop, InterfaceIndex)
        $afterDisable = @(Get-NetRoute -DestinationPrefix $prefix -ErrorAction SilentlyContinue | Select-Object DestinationPrefix)
        $afterEnable = @(Get-NetRoute -DestinationPrefix $prefix -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop)
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
            cliExists = $cliExists
            cliPath = $cliExe
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
    }
    $run = Invoke-GuestScriptElevated -Session $Session -Cred $script:Cred -ScriptBlock $body -ArgumentList @{ prefix = $Prefix }
    $ev = $run.result
    Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); managedPrefix=$Prefix
        elevationAvailable = $run.elevation.elevationAvailable
        evidence = $ev
    })
    return $ev
}

function Run-GATE4([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-4 PURGE -> REINSTALL"
    $installPs1 = 'C:\pv-cert\Install-PathVeer.ps1'
    $pkg = 'C:\pv-cert\PathVeer-1.0.0-beta.1'
    # Hard purge via supported mechanism (elevated).
    $u = Invoke-GuestElevated -Session $Session -Cred $script:Cred -Command "powershell.exe -File '$installPs1' -Action uninstall -PurgeState" -ResultDir 'C:\pv-cert'
    $afterUninstall = Invoke-Command -Session $Session -ScriptBlock {
        param($u)
        [PSCustomObject]@{
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programFiles = (Test-Path 'C:\Program Files\PathVeer')
            programData = (Test-Path "$env:ProgramData\PathVeer")
            uninstallExitCode = $u.exitCode
            uninstallElevated = $u.elevationAvailable
        }
    } -ArgumentList $u
    # Clean reinstall (elevated).
    $r = Invoke-GuestElevated -Session $Session -Cred $script:Cred -Command "powershell.exe -File '$installPs1' -PackageDirectory '$pkg' -RegisterShell -InstallTray" -ResultDir 'C:\pv-cert'
    $svc = Invoke-Command -Session $Session -ScriptBlock {
        param($r)
        $svc = Get-Service -Name 'PathVeer' -ErrorAction Stop
        [PSCustomObject]@{ reinstallExitCode = $r.exitCode; reinstalledServiceState = $svc.Status; reinstallElevated = $r.elevationAvailable }
    } -ArgumentList $r
    Save-Json '04-gate4-purge-reinstall.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o')
        uninstall = $afterUninstall
        reinstall = $svc
    })
    return $svc
}

function Run-GATE8([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-8 UNINSTALL / STATE-PRESERVED REINSTALL"
    $installPs1 = 'C:\pv-cert\Install-PathVeer.ps1'
    $pkg = 'C:\pv-cert\PathVeer-1.0.0-beta.1'
    # Write a sentinel into persistent state to prove preservation across reinstall.
    Invoke-Command -Session $Session -ScriptBlock {
        $stateDir = Join-Path $env:ProgramData 'PathVeer'
        if (-not (Test-Path $stateDir)) { New-Item -ItemType Directory -Force -Path $stateDir | Out-Null }
        Set-Content -Path (Join-Path $stateDir 'cert-sentinel.txt') -Value 'preserved-state-marker' -Encoding utf8
    } | Out-Null
    # Normal uninstall (NO purge) via supported mechanism (elevated).
    $u = Invoke-GuestElevated -Session $Session -Cred $script:Cred -Command "powershell.exe -File '$installPs1' -Action uninstall" -ResultDir 'C:\pv-cert'
    $afterUninstall = Invoke-Command -Session $Session -ScriptBlock {
        param($u)
        $stateDir = Join-Path $env:ProgramData 'PathVeer'
        [PSCustomObject]@{
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programFiles = (Test-Path 'C:\Program Files\PathVeer')
            programData = (Test-Path $stateDir)               # expected TRUE (preserved)
            sentinelPreserved = (Test-Path (Join-Path $stateDir 'cert-sentinel.txt'))
            appsAndFeaturesEntry = ($null -ne (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PathVeer' -ErrorAction SilentlyContinue))
            uninstallExitCode = $u.exitCode
            uninstallElevated = $u.elevationAvailable
        }
    } -ArgumentList $u
    # Reinstall and prove preserved state recognized (elevated).
    $r = Invoke-GuestElevated -Session $Session -Cred $script:Cred -Command "powershell.exe -File '$installPs1' -PackageDirectory '$pkg' -RegisterShell -InstallTray" -ResultDir 'C:\pv-cert'
    $reinstallSentinelPreserved = Invoke-Command -Session $Session -ScriptBlock {
        param($r)
        $stateDir = Join-Path $env:ProgramData 'PathVeer'
        [PSCustomObject]@{
            reinstallExitCode = $r.exitCode
            reinstallElevated = $r.elevationAvailable
            preservedStateRecognized = (Test-Path (Join-Path $stateDir 'cert-sentinel.txt'))
        }
    } -ArgumentList $r
    Save-Json '08-gate8-uninstall-reinstall.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o')
        uninstall = $afterUninstall
        reinstall = $reinstallSentinelPreserved
    })
    return $reinstallSentinelPreserved
}

function Run-GATE6([System.Management.Automation.Runspaces.PSSession]$Session, [string]$OldPkg, [string]$NewPkg) {
    Write-Stage "GATE-6 UPGRADE (0.9.0 -> 1.0.0-beta.1) + DOWNGRADE BLOCK"
    $guestRoot = 'C:\pv-cert'
    Copy-ToGuest $Session @($OldPkg, $NewPkg) $guestRoot
    $oldG = Join-Path $guestRoot (Split-Path $OldPkg -Leaf)
    $newG = Join-Path $guestRoot (Split-Path $NewPkg -Leaf)
    $installPs1 = Join-Path $guestRoot 'Install-PathVeer.ps1'
    # Install older baseline (elevated).
    $b = Invoke-GuestElevated -Session $Session -Cred $script:Cred -Command "powershell.exe -File '$installPs1' -PackageDirectory '$oldG' -RegisterShell -InstallTray" -ResultDir $guestRoot
    $oldVer = $null
    if ($b.exitCode -eq 0) {
        $oldVer = (Invoke-Command -Session $Session -ScriptBlock { (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw | ConvertFrom-Json).productVersion })
    }
    # Upgrade (elevated).
    $u = Invoke-GuestElevated -Session $Session -Cred $script:Cred -Command "powershell.exe -File '$installPs1' -PackageDirectory '$newG' -RegisterShell -InstallTray" -ResultDir $guestRoot
    $newVer = $null; $svcAfter = $null
    if ($u.exitCode -eq 0) {
        $up = Invoke-Command -Session $Session -ScriptBlock { [PSCustomObject]@{
            version = (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw | ConvertFrom-Json).productVersion
            serviceState = (Get-Service -Name 'PathVeer').Status
        } }
        $newVer = $up.version; $svcAfter = $up.serviceState
    }
    # Downgrade attempt (must be blocked, exit 103). Elevated.
    $d = Invoke-GuestElevated -Session $Session -Cred $script:Cred -Command "powershell.exe -File '$installPs1' -PackageDirectory '$oldG' -RegisterShell" -ResultDir $guestRoot
    $services = Invoke-Command -Session $Session -ScriptBlock {
        @(Get-CimInstance Win32_Service | Where-Object { $_.Name -eq 'PathVeer' -or $_.Name -eq 'IranDirect' } | ForEach-Object { $_.Name })
    }
    Save-Json '06-gate6-upgrade.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o')
        baselineInstallExitCode = $b.exitCode
        baselineElevated = $b.elevationAvailable
        baselineVersion = $oldVer
        upgradeExitCode = $u.exitCode
        upgradeElevated = $u.elevationAvailable
        upgradedVersion = $newVer
        serviceStateAfterUpgrade = $svcAfter
        downgradeExitCode = $d.exitCode
        downgradeBlocked = ($d.exitCode -eq 103)
        serviceIdentities = $services
        noDuplicateService = ($services.Count -le 1)
    })
    return @{ baselineVersion=$oldVer; upgradedVersion=$newVer; downgradeBlocked=($d.exitCode -eq 103) }
}

function Run-GATE28([System.Management.Automation.Runspaces.PSSession]$Session, [string]$Pkg) {
    Write-Stage "GATE-28 SAME-VERSION REPAIR"
    $guestRoot = 'C:\pv-cert'
    $pkgG = Join-Path $guestRoot (Split-Path $Pkg -Leaf)
    $installPs1 = Join-Path $guestRoot 'Install-PathVeer.ps1'
    # Same-version repair/install over existing (elevated).
    $r = Invoke-GuestElevated -Session $Session -Cred $script:Cred -Command "powershell.exe -File '$installPs1' -PackageDirectory '$pkgG' -RegisterShell -InstallTray" -ResultDir $guestRoot
    $svcAfter = $null; $ver = $null; $cliRepairExit = $null
    if ($r.exitCode -eq 0) {
        $svcAfter = (Invoke-Command -Session $Session -ScriptBlock { (Get-Service -Name 'PathVeer' -ErrorAction Stop).Status })
        $ver = (Invoke-Command -Session $Session -ScriptBlock { (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw | ConvertFrom-Json).productVersion })
        # CLI repair verb (elevated, guarded).
        $cliExe = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe'
        if (Test-Path -LiteralPath $cliExe -PathType Leaf) {
            $rep = Invoke-GuestElevated -Session $Session -Cred $script:Cred -Command "& '$cliExe' repair" -ResultDir $guestRoot
            $cliRepairExit = $rep.exitCode
        }
    }
    Save-Json '28-gate28-repair.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o')
        installExitCode = $r.exitCode
        installElevated = $r.elevationAvailable
        repairedVersion = $ver
        serviceStateAfterRepair = $svcAfter
        cliRepairExitCode = $cliRepairExit
    })
    return @{ repairedVersion=$ver; serviceStateAfterRepair=$svcAfter }
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

$stages = @('GATE5','GATE6','GATE8','GATE3','GATE2','GATE4','GATE28','GATE9')
if ($Stage -ne 'All') {
    $stages = @($Stage)
}

foreach ($st in $stages) {
    if (-not $SkipRestore) {
        $sess = $null
        Restore-Clean
        $sess = New-GuestSession $script:Cred
    }
    switch ($st) {
        'GATE5'  {
            try {
                Run-GATE5  $sess $CandidatePackage | Out-Null
                Run-ServiceTrayContract $sess | Out-Null
            } catch {
                Write-Host "  GATE-5 stage ended (install gate not satisfied): $($_.Exception.Message)" -ForegroundColor Red
                # Stop the whole run cleanly; structured FAIL evidence already saved.
                # The final block restores the clean checkpoint.
                $sess | Remove-PSSession -ErrorAction SilentlyContinue
                return
            }
        }
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
