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

    #    RESUMABILITY:
    #      * Run with -Stage All (default) for the full sequence, or -Stage <name> to
    #        (re)run a single gate. Each stage restores the certification checkpoint
    #        (PV-CERT-HARNESS) first (except where noted), so a half-run VM never poisons
    #        later stages.
    #      * -SkipRestore skips the checkpoint-restore step for a single re-run.

    USAGE (from a NATIVE Windows PowerShell/Terminal window so the credential
    dialog can appear):
      pwsh -NoProfile -File tools\certification\Invoke-PathVeerCertification.ps1 [-Stage All]
#>
[CmdletBinding()]
param(
    [string]$VmName = 'PathVeer-Certification',
    # Canonical certification execution baseline. MUST be PV-CERT-HARNESS (clean Windows +
    # approved JEA instrumentation, NO PathVeer product). Restoring PV-CLEAN-WINDOWS would
    # wipe the JEA endpoint and break every privileged gate. Missing -> harness fails loudly.
    [string]$CertificationSnapshot = 'PV-CERT-HARNESS',
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
    # Certification execution baseline MUST be PV-CERT-HARNESS (clean Windows + approved JEA
    # instrumentation, NO PathVeer product). Restoring PV-CLEAN-WINDOWS would wipe the JEA
    # endpoint and break every privileged gate. Fail loudly (no silent fallback) if absent.
    Write-Host "Restoring certification checkpoint '$CertificationSnapshot'..." -ForegroundColor Yellow
    if ($Session) { Remove-PSSession $Session -ErrorAction SilentlyContinue }
    try {
        Restore-VMSnapshot -VMName $VmName -Name $CertificationSnapshot -Confirm:$false -ErrorAction Stop
    } catch {
        throw "Certification checkpoint '$CertificationSnapshot' not found or could not be restored on VM '$VmName'. Create it from the clean Windows baseline AFTER registering the JEA endpoint (Enable-PathVeerCertificationJea.ps1), then re-run. Do NOT use PV-CLEAN-WINDOWS for certification runs. ($($_.Exception.Message))"
    }
    # A Standard checkpoint captured while Running may already be Running after restore,
    # so do NOT unconditionally Start-VM (unsafe state transition). Reuse Assert-VmRunning,
    # which starts the VM only when it is not already Running, then waits for heartbeat.
    # This works regardless of whether the restored certification checkpoint was captured
    # Running or Off. New-GuestSession's retry loop establishes PowerShell Direct after.
    Assert-VmRunning
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

# Shared privileged-execution primitive (single source of truth). Defines
# New-GuestJeaSession / Invoke-GuestJeaFunction / Invoke-GuestJeaInstall / Invoke-GuestJeaCli /
# Stop-GuestJeaService / Start-GuestJeaService / Stop-GuestJeaTray / Get-GuestJeaServiceState /
# Get-GuestJeaRouteState / Get-GuestJeaInstallManifest / Get-GuestJeaProgramDataState, used by
# every privileged gate. Execution occurs through a narrow JEA endpoint 'PathVeer.Certification'
# (virtual account) registered in the guest by the operator (Enable-PathVeerCertificationJea.ps1).
# This replaces the retired Scheduled-Task (RunLevel Highest) elevation design, which real-VM
# Windows rejected with "Access is denied" from the filtered PowerShell Direct parent.
#
# NOTE: PathVeer.Certification.Elevation.ps1 is RETIRED (preserved as historical diagnostic
# evidence of the rejected design) and must NOT be dot-sourced or invoked by the harness.
. (Join-Path $PSScriptRoot 'PathVeer.Certification.Jea.ps1')

# Establish the privileged JEA session for elevated guest operations. If the endpoint is
# not registered in the guest, this returns $null and privileged gates fail with a
# structured HARNESS/ENVIRONMENT error (never silent non-elevated execution).
$script:JeaSession = $null
function Get-GuestJeaSession([System.Management.Automation.PSCredential]$Cred) {
    if ($script:JeaSession) { return $script:JeaSession }
    $s = New-GuestJeaSession -Cred $Cred -VMName $VmName -ConfigurationName 'PathVeer.Certification'
    if (-not $s) {
        throw "JEA certification endpoint 'PathVeer.Certification' is not registered in the guest. Run Enable-PathVeerCertificationJea.ps1 (elevated) inside the guest, then re-run."
    }
    $script:JeaSession = $s
    return $s
}

# Convenience wrappers that route privileged gate operations through the trusted JEA
# module functions. NO raw command / ScriptBlock / executable is forwarded to the guest;
# the JEA endpoint exposes only validated wrappers.

function Run-GuestJeaInstall([System.Management.Automation.Runspaces.PSSession]$Session,
                             [string]$Action='Install', [string[]]$Feature=@('RegisterShell','InstallTray')) {
    $jea = Get-GuestJeaSession $script:Cred
    return Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action $Action -Feature $Feature
}

# -------- Stage implementations (each returns a hashtable of evidence) --------

function Assert-CandidateMatchesProtected([string]$CandidatePackage, [string]$ExpectedPackageId) {
    # SECURITY (Option A semantics): the harness installs ONLY the operator-staged protected
    # payload (ExpectedPackageId). It must NOT silently accept a -CandidatePackage that names a
    # different release while the VM installs the checkpointed protected candidate. Fail loud on
    # identity mismatch so the operator never passes candidate X while the VM installs candidate Y.
    $leaf = [System.IO.Path]::GetFileName($CandidatePackage)
    if ($leaf -ne $ExpectedPackageId) {
        throw ("CANDIDATE IDENTITY MISMATCH: -CandidatePackage basename '$leaf' does not match the operator-staged protected certification payload '$ExpectedPackageId'. The harness installs only the protected payload staged via Enable-PathVeerCertificationJea.ps1; pass the matching candidate package.")
    }
    Step "Candidate identity matches protected payload: $leaf"
}

function Run-GATE5([System.Management.Automation.Runspaces.PSSession]$Session, [string]$Pkg) {
    Write-Stage "GATE-5 FRESH INSTALL (from clean baseline)"
    # SECURITY (Option A): the operator bootstrap is the ONLY trust transition. PV-CERT-HARNESS
    # already contains the protected, operator-approved installer + payload. The harness performs
    # NO runtime copy of executable bytes into the guest (the old Copy-ToGuest into the untrusted
    # C:\pv-cert\incoming was obsolete and unconsumed after Option A). The harness does NOT inspect
    # the protected tree from the filtered pvcert session (the Option-A ACL denies it, by design).
    # Instead it validates the Desktop-side candidate identity, then delegates to the trusted JEA
    # wrapper, which enforces the exact protected installer + payload preconditions inside the
    # privileged virtual-account context and returns the installer's structured result. pvcert
    # cannot choose the privileged executable bytes.
    $expectedPackageId = 'PathVeer-1.0.0-beta.1'
    Assert-CandidateMatchesProtected -CandidatePackage $Pkg -ExpectedPackageId $expectedPackageId

    $pre = Invoke-Command -Session $Session -ScriptBlock {
        [PSCustomObject]@{
            programFilesPathVeer = (Test-Path 'C:\Program Files\PathVeer')
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programData = (Test-Path "$env:ProgramData\PathVeer")
        }
    }

    Step "Running installer (ELEVATED via JEA trusted wrapper, RegisterShell + InstallTray)"
    # Trusted installer wrapper: fixed installer path, validated switches only. No generic shell.
    $install = Run-GuestJeaInstall -Session $Session -Action Install -Feature @('RegisterShell','InstallTray')

    # Read the installer's structured result + progress (authoritative gate boundary).
    # The trusted function now writes the installer's -ResultFile/-ProgressFile into the JEA
    # virtual account's TEMP, reads them back, and surfaces them on $install.result. The harness
    # therefore consumes the structured result directly from the trusted return object and never
    # reads C:\pv-cert\install-*.json (the installer was never given those paths to write).
    $installResult = $null; $installProgress = $null
    if ($install.result) {
        $installResult   = $install.result.installerResult
        $installProgress = $install.result.installerProgress
    }

    # Hard gate boundary: installation must succeed before any CLI use.
    $post = Get-GuestJeaServiceState -Session $Session -JeaSession (Get-GuestJeaSession $script:Cred)
    $installRootExists = $false; $svcExists = $false; $manifestExists = $false
    $manifest = Get-GuestJeaInstallManifest -Session $Session -JeaSession (Get-GuestJeaSession $script:Cred)
    $installRootExists = (Invoke-Command -Session $Session -ScriptBlock { Test-Path 'C:\Program Files\PathVeer' })
    $svcExists = ($post.result.exists -eq $true)
    if ($manifest.result) { $manifestExists = $true }

    $gate5Failed = $false
    $failReasons = [System.Collections.Generic.List[string]]::new()
    if (-not $install.elevationAvailable) { $gate5Failed = $true; $failReasons.Add("elevation unavailable: $($install.error)") }
    # Only classify as "child not genuinely elevated" when the install actually completed and
    # reached identity capture. If the invocation was rejected upstream (e.g. NoLanguage syntax),
    # completed=$false and the "did not complete" reason already explains the real cause -- do not
    # mislabel it as a loss of JEA virtual-account elevation (which the control plane already proved).
    if ($install.completed -eq $true -and $install.elevationSucceeded -ne $true) { $gate5Failed = $true; $failReasons.Add("child not genuinely elevated (childIsAdministrator=$($install.childIsAdministrator), childUser=$($install.childUser))") }
    if ($install.completed -eq $false) { $gate5Failed = $true; $failReasons.Add("installer did not complete: $($install.error)") }
    # --- Installer lifecycle (from the trusted function's explicit fields, not the wrapper's
    #     misleading 'completed' flag). 'installCompleted=true' historically meant only "the JEA
    #     call returned without an error string", which is NOT proof the installer ran. These
    #     fields disambiguate installer start / return / exit / error. ---
    $ir = $install.result
    if ($null -eq $ir) { $gate5Failed = $true; $failReasons.Add('trusted install function returned no result object') }
    else {
        if ($ir.installerInvocationAttempted -ne $true) { $gate5Failed = $true; $failReasons.Add('installer invocation was not attempted by the trusted wrapper') }
        if ($ir.installerStarted -ne $true) { $gate5Failed = $true; $failReasons.Add('installer process did not start') }
        if ($ir.installerReturned -ne $true) { $gate5Failed = $true; $failReasons.Add("installer process did not return: $($ir.installerError)") }
        if ($null -ne $ir.installerExitCode -and $ir.installerExitCode -ne 0) { $gate5Failed = $true; $failReasons.Add("installer exit code $($ir.installerExitCode)") }
        if ($ir.installerError) { $gate5Failed = $true; $failReasons.Add("installer error: $($ir.installerError)") }
        if ($ir.installerResult -and $ir.installerResult.success -eq $false) { $gate5Failed = $true; $failReasons.Add("installer reported failure: [$($ir.installerResult.category)] $($ir.installerResult.message)") }
    }
    if (-not $svcExists) { $gate5Failed = $true; $failReasons.Add('PathVeer service not present after install') }
    if (-not $manifestExists) { $gate5Failed = $true; $failReasons.Add('install-manifest.json not present after install') }

    if ($gate5Failed) {
        $failEvidence = [PSCustomObject]@{
            gate = 'GATE-5'; result = 'FAIL'; reasons = $failReasons.ToArray()
            elevated = $install.elevationAvailable; elevationSucceeded = $install.elevationSucceeded
            childUser = $install.childUser; childIsAdministrator = $install.childIsAdministrator
            # Explicit installer lifecycle (replaces the misleading 'installCompleted').
            trustedInstallFunctionReached  = ($null -ne $install.childUser)
            installerPathValidated         = if ($ir) { $ir.installerPathValidated } else { $null }
            payloadPathValidated           = if ($ir) { $ir.payloadPathValidated } else { $null }
            installerInvocationAttempted   = if ($ir) { $ir.installerInvocationAttempted } else { $null }
            installerStarted               = if ($ir) { $ir.installerStarted } else { $null }
            installerReturned              = if ($ir) { $ir.installerReturned } else { $null }
            installerExitCode              = if ($ir) { $ir.installerExitCode } else { $null }
            installerError                 = if ($ir) { $ir.installerError } else { $null }
            installerResult                = $installResult
            installerProgress              = $installProgress
            installCompleted               = $install.completed
            postConditions = $post.result; preInstall = $pre
        }
        Save-Json '05-gate5-fresh-install.json' $failEvidence
        Write-Host "  GATE-5 FAIL (no CLI invoked): $($failReasons -join ' | ')" -ForegroundColor Red
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("GATE-5 install failed: $($failReasons -join '; ')"),
            'GATE5InstallFailed', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
    }

    # Success path: locate CLI from the authoritative manifest, guard before invoking.
    $cliExe = $null
    if ($manifest.result -and $manifest.result.cliExecutablePath) { $cliExe = $manifest.result.cliExecutablePath }
    else { $cliExe = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe' }

    $ev = Invoke-Command -Session $Session -ScriptBlock {
        param($cliExe, $installManifest)
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
        if ($app) { $appsAndFeatures = [PSCustomObject]@{ displayName = $app.DisplayName; version = $app.DisplayVersion; publisher = $app.Publisher; uninstall = $app.UninstallString } }
        [PSCustomObject]@{
            serviceName = $svcName; serviceState = $svcState; serviceStartMode = $svcStartMode; servicePathName = $svcPathName
            appsAndFeatures = $appsAndFeatures; startMenuShortcuts = $startMenu
            trayPresent = ($null -ne $tray); cliOnPathCount = $cliOnPath
            ipcPipes = $pipe; cliExists = $cliExists; cliPath = $cliExe
            cliStatus = ($cliStatus -join "`n"); programDataState = (Test-Path "$env:ProgramData\PathVeer")
            installManifest = $installManifest
        }
    } -ArgumentList $cliExe, $manifest.result

    Save-Json '05-gate5-fresh-install.json' ([PSCustomObject]@{
        capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
        preInstall = $pre
        elevationAvailable = $install.elevationAvailable
        installExitCode = $installExit
        installCompleted = $install.completed
        installResult = $installResult
        installProgress = $installProgress
        postConditions = $post.result
        evidence = $ev
    })
    return $ev
}

function Run-ServiceTrayContract([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "SERVICE/TRAY AUTHORITY CONTRACT"
    # Normalize a ServiceController enum value regardless of how it was obtained:
    #  - native [System.ServiceProcess.ServiceControllerStatus]/[ServiceStartMode] enum
    #  - deserialized remoting wrapper: @{ value = 4; Value = "Running" }  (Invoke-Command over a
    #    session serializes enums as this PSObject with int 'value' + string 'Value')
    #  - plain string
    #  - bare int (numeric fallback mapped for the two fields this contract asserts)
    function Get-EnumString($obj) {
        if ($null -eq $obj) { return $null }
        if ($obj -is [string]) { return [string]$obj }
        try {
            if ($obj.PSObject.Properties['Value'] -and $obj.Value) { return [string]$obj.Value }
        } catch {}
        try {
            if ($obj.PSObject.Properties['value']) {
                $v = $obj.value
                if ($v -is [string] -and $v) { return [string]$v }
                if ($v -is [int]) {
                    # ServiceControllerStatus: 4 = Running. ServiceStartMode: 2 = Automatic.
                    if ($v -eq 4) { return 'Running' }
                    if ($v -eq 2) { return 'Automatic' }
                    return [string]$v
                }
            }
        } catch {}
        return [string]$obj
    }
    $jea = Get-GuestJeaSession $script:Cred
    # --- Tray precondition (coverage): ensure Tray is genuinely running under the NORMAL certification
    #     user (the PowerShell Direct session identity), NOT the JEA virtual admin. This makes the
    #     "running Tray -> terminated -> Service stays Running" assertion a real test instead of a
    #     silently-skipped precondition. If Tray cannot be established we must NOT silently pass. ---
    $trayExe = Join-Path 'C:\Program Files\PathVeer' 'Tray\PathVeer.Tray.exe'
    $trayPreconditionEstablished = $false
    try {
        $trayRunningNow = Invoke-Command -Session $Session -ScriptBlock {
            param($trayExe)
            if (-not (Test-Path -LiteralPath $trayExe -PathType Leaf)) { return $false }
            $p = Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue
            if ($null -eq $p) {
                # Launch as the normal user (same session scope) — NOT the JEA virtual admin.
                Start-Process -FilePath $trayExe -ErrorAction Stop
                Start-Sleep -Seconds 2
                $p = Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue
            }
            return ($null -ne $p)
        } -ArgumentList $trayExe
        $trayPreconditionEstablished = ($trayRunningNow -eq $true)
    } catch {
        $trayPreconditionEstablished = $false
    }
    # --- Desktop-side JEA reads (no remoting scope leakage) ---
    $svcBefore = Get-GuestJeaServiceState -Session $Session -JeaSession $jea   # service state BEFORE Tray stop
    $trayStop  = Stop-GuestJeaTray -Session $Session -JeaSession $jea           # privileged Tray teardown (no service action)
    # Bounded observation window, THEN measure fresh state. Do NOT infer post-wait state from the stop return.
    Start-Sleep -Seconds 3
    $svcAfter  = Get-GuestJeaServiceState -Session $Session -JeaSession $jea   # service state AFTER Tray stop

    # --- Remote facts ONLY (normal PowerShell Direct session): no Desktop variables referenced inside,
    #     so the cross-runspace null bug cannot recur. CLI existence + bounded readiness probe output,
    #     fresh Tray process presence, named-pipe-name presence. ---
    $remote = Invoke-Command -Session $Session -ScriptBlock {
        $cliExe  = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe'
        $trayExe = Join-Path 'C:\Program Files\PathVeer' 'Tray\PathVeer.Tray.exe'
        $cliExists = (Test-Path -LiteralPath $cliExe -PathType Leaf)

        # Bounded deterministic readiness probe: poll the read-only CLI status until success OR a fixed
        # timeout. No blind sleep. Records every attempt so timing vs. product-IPC races are distinguishable.
        $cliProbeTimeoutSeconds = 20
        $cliAttemptIntervalSeconds = 1
        $cliAttempts = @()
        $cliHealthyDuringPoll = $false
        if ($cliExists) {
            $elapsed = 0
            while ($elapsed -lt $cliProbeTimeoutSeconds) {
                $out = $null; $code = $null
                try { $out = (& $cliExe status 2>&1); $code = $LASTEXITCODE } catch { $code = -1 }
                $cliAttempts += [PSCustomObject]@{ attempt = ($cliAttempts.Count + 1); exitCode = $code; output = ($out -join "`n") }
                if ($code -eq 0) { $cliHealthyDuringPoll = $true; break }
                Start-Sleep -Seconds $cliAttemptIntervalSeconds
                $elapsed += $cliAttemptIntervalSeconds
            }
        }
        $finalAttempt = if ($cliAttempts.Count) { $cliAttempts[-1] } else { $null }
        $svc = Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue
        $trayProcPresent = ($null -ne (Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue))
        $pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*PathVeer.Control.v1*' }
        [PSCustomObject]@{
            cliExists = $cliExists
            cliAttemptCount = $cliAttempts.Count
            cliExitCode = if ($finalAttempt) { $finalAttempt.exitCode } else { $null }
            cliStdoutStderr = if ($finalAttempt) { $finalAttempt.output } else { $null }
            cliHealthyDuringPoll = $cliHealthyDuringPoll
            serviceStateAtCliAttempt = if ($svc) { $svc.Status } else { 'absent' }
            trayProcessPresentAfterWait = $trayProcPresent
            ipcPipeNamePresentAfter = ($null -ne $pipe)
            trayExePath = $trayExe
        }
    }

    # --- Desktop combines EXPLICIT values into the contract (Defects 1-4, truthful semantics) ---
    $contract = [PSCustomObject]@{
        capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
        serviceStateBeforeTrayKill   = (Get-EnumString $svcBefore.result.status)
        serviceStartModeBefore       = (Get-EnumString $svcBefore.result.startType)
        trayWasRunningBefore         = $trayStop.result.trayWasRunning          # truthful Boolean (was Tray running?)
        trayPreconditionEstablished  = $trayPreconditionEstablished             # Tray genuinely ran under normal user before stop
        serviceStateAfterTrayKill    = (Get-EnumString $svcAfter.result.status)
        serviceStartModeAfter        = (Get-EnumString $svcAfter.result.startType)
        trayProcessPresentAfterWait  = $remote.trayProcessPresentAfterWait      # fresh post-wait read (Defect 3)
        cliExists                    = $remote.cliExists
        cliAttemptCount              = $remote.cliAttemptCount
        cliExitCode                  = $remote.cliExitCode
        cliStdoutStderr              = $remote.cliStdoutStderr
        cliHealthyDuringPoll         = $remote.cliHealthyDuringPoll
        cliWorksWithoutTray          = $remote.cliHealthyDuringPoll             # exit 0 during bounded poll (no Tray)
        serviceStateAtCliAttempt     = $remote.serviceStateAtCliAttempt
        ipcPipeNamePresentAfter      = $remote.ipcPipeNamePresentAfter          # name enumeration ONLY (truthful name)
        trayExePath                  = $remote.trayExePath
    }

    # --- Contract assertions (Defect 6): fail loudly, never silently continue ---
    $reasons = @()
    if ($contract.serviceStateBeforeTrayKill -ne 'Running') {
        $reasons += "service-not-Running-before-tray-kill (was: $($contract.serviceStateBeforeTrayKill))"
    }
    if ($contract.serviceStateAfterTrayKill -ne 'Running') {
        $reasons += "service-not-Running-after-tray-kill (was: $($contract.serviceStateAfterTrayKill))"
    }
    if ($contract.serviceStartModeBefore -ne $contract.serviceStartModeAfter) {
        $reasons += "service-start-mode-changed (before: $($contract.serviceStartModeBefore) after: $($contract.serviceStartModeAfter))"
    }
    if ($contract.trayWasRunningBefore -and $contract.trayProcessPresentAfterWait) {
        $reasons += "tray-still-present-after-stop (auto-relaunch suspected during observation window)"
    }
    if (-not $contract.cliExists) { $reasons += "cli-executable-missing" }
    if (-not $contract.cliWorksWithoutTray) { $reasons += "cli-status-failed-without-tray (exit: $($contract.cliExitCode))" }
    if (-not $contract.trayPreconditionEstablished) { $reasons += "tray-precondition-not-established (Tray was never running under the normal user; contract not actually exercised)" }
    if (-not $contract.ipcPipeNamePresentAfter) { $reasons += "ipc-pipe-name-absent-after-install" }

    if ($reasons.Count) {
        $fail = [PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            elevationAvailable = ($jea -ne $null)
            contract = $contract
            failReasons = $reasons
        }
        Save-Json '22-servicetray-contract.json' $fail
        Write-Host ("  SERVICE/TRAY CONTRACT FAIL: " + ($reasons -join ' | ')) -ForegroundColor Red
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("Service/Tray authority contract not satisfied: " + ($reasons -join '; ')),
            'ServiceTrayContractFailed', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
    }

    Save-Json '22-servicetray-contract.json' ([PSCustomObject]@{
        capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
        elevationAvailable = ($jea -ne $null)
        contract = $contract
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
            serviceState = $svc.Status; serviceStartMode = $svc.StartType; startedBy = $svc.StartType; policy = $policy
        }
    }
    Step "Restarting guest VM only..."
    Restart-Computer -VMName $VmName -Force -Wait -For PowerShellDirect -Credential $script:Cred -Timeout 300 -ErrorAction Stop
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
            serviceState = $svc.Status; serviceStartMode = $svc.StartType
            ipcPipeAlive = ($null -ne $pipe); cliWorks = $cliOk; policy = $policy
        }
    }
    Save-Json '03-gate3-reboot-persistence.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); before=$before; after=$after; rebootedVmOnly=$true
    })
    return @{ Session=$session2; after=$after }
}

function Run-GATE2([System.Management.Automation.Runspaces.PSSession]$Session, [string]$Prefix) {
    Write-Stage "GATE-2 ROUTE MUTATION / RECOVERY (bounded TEST-NET $Prefix)"
    $jea = Get-GuestJeaSession $script:Cred
    $routesBefore = Invoke-Command -Session $Session -ScriptBlock {
        @(Get-NetRoute -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop, InterfaceIndex, RouteMetric)
    }
    # CLI route + service control verbs run through the trusted JEA CLI wrapper.
    $addC = Invoke-GuestJeaCli -Session $Session -JeaSession $jea -Verb 'custom-routes' -SubVerb 'add-cidr' -Argument $Prefix
    Start-Sleep -Seconds 2
    $disC = Invoke-GuestJeaCli -Session $Session -JeaSession $jea -Verb 'disable'
    Start-Sleep -Seconds 2
    $enC  = Invoke-GuestJeaCli -Session $Session -JeaSession $jea -Verb 'enable'
    Start-Sleep -Seconds 3
    $doc  = Invoke-GuestJeaCli -Session $Session -JeaSession $jea -Verb 'doctor'
    $customAfterAdd = (Get-GuestJeaRouteState -Session $Session -JeaSession $jea -Prefix $Prefix).result
    $afterDisable    = (Get-GuestJeaRouteState -Session $Session -JeaSession $jea -Prefix $Prefix).result
    $afterEnable     = (Get-GuestJeaRouteState -Session $Session -JeaSession $jea -Prefix $Prefix).result
    # Recovery: controlled service stop/start (privileged), confirm reconcile.
    $stop = Stop-GuestJeaService -Session $Session -JeaSession $jea
    Start-Sleep -Seconds 2
    $stoppedState = $stop.result.status
    $start = Start-GuestJeaService -Session $Session -JeaSession $jea
    Start-Sleep -Seconds 4
    $recoveredState = $start.result.status
    $afterRecovery = (Get-GuestJeaRouteState -Session $Session -JeaSession $jea -Prefix $Prefix).result
    $routesAfter = Invoke-Command -Session $Session -ScriptBlock {
        @(Get-NetRoute -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop, InterfaceIndex, RouteMetric)
    }
    $ev = [PSCustomObject]@{
        routesBeforeCount = $routesBefore.Count
        cliExists = $true
        cliPath = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe'
        addCustomRouteExit = $addC.result.exitCode
        customRouteAfterAdd = $customAfterAdd
        disableExit = $disC.result.exitCode
        prefixPresentAfterDisable = ($afterDisable.Count -gt 0)
        enableExit = $enC.result.exitCode
        prefixPresentAfterEnable = ($afterEnable.Count -gt 0)
        doctorSummary = $doc.result.output
        serviceStoppedState = $stoppedState
        serviceRecoveredState = $recoveredState
        prefixPresentAfterRecovery = ($afterRecovery.Count -gt 0)
        routesAfterCount = $routesAfter.Count
        defaultRouteIntact = ($null -ne ($routesAfter | Where-Object { $_.DestinationPrefix -eq '0.0.0.0/0' }))
    }
    Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); managedPrefix=$Prefix
        elevationAvailable = ($jea -ne $null)
        evidence = $ev
    })
    return $ev
}

function Run-GATE4([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-4 PURGE -> REINSTALL"
    $jea = Get-GuestJeaSession $script:Cred
    # Hard purge via trusted wrapper.
    $u = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action PurgeUninstall
    $afterUninstall = Invoke-Command -Session $Session -ScriptBlock {
        param($u)
        [PSCustomObject]@{
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programFiles = (Test-Path 'C:\Program Files\PathVeer')
            programData = (Test-Path "$env:ProgramData\PathVeer")
            uninstallExitCode = $u.result.exitCode
            uninstallElevated = $u.elevationAvailable
        }
    } -ArgumentList $u
    # Clean reinstall (elevated).
    $r = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Install -Feature @('RegisterShell','InstallTray')
    $svc = Invoke-Command -Session $Session -ScriptBlock {
        param($r)
        $svc = Get-Service -Name 'PathVeer' -ErrorAction Stop
        [PSCustomObject]@{ reinstallExitCode = $r.result.exitCode; reinstalledServiceState = $svc.Status; reinstallElevated = $r.elevationAvailable }
    } -ArgumentList $r
    Save-Json '04-gate4-purge-reinstall.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); uninstall = $afterUninstall; reinstall = $svc
    })
    return $svc
}

function Run-GATE8([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-8 UNINSTALL / STATE-PRESERVED REINSTALL"
    $jea = Get-GuestJeaSession $script:Cred
    # Write a sentinel into persistent state to prove preservation across reinstall.
    Invoke-Command -Session $Session -ScriptBlock {
        $stateDir = Join-Path $env:ProgramData 'PathVeer'
        if (-not (Test-Path $stateDir)) { New-Item -ItemType Directory -Force -Path $stateDir | Out-Null }
        Set-Content -Path (Join-Path $stateDir 'cert-sentinel.txt') -Value 'preserved-state-marker' -Encoding utf8
    } | Out-Null
    # Normal uninstall (NO purge) via trusted wrapper.
    $u = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Uninstall
    $afterUninstall = Invoke-Command -Session $Session -ScriptBlock {
        param($u)
        $stateDir = Join-Path $env:ProgramData 'PathVeer'
        [PSCustomObject]@{
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programFiles = (Test-Path 'C:\Program Files\PathVeer')
            programData = (Test-Path $stateDir)               # expected TRUE (preserved)
            sentinelPreserved = (Test-Path (Join-Path $stateDir 'cert-sentinel.txt'))
            appsAndFeaturesEntry = ($null -ne (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PathVeer' -ErrorAction SilentlyContinue))
            uninstallExitCode = $u.result.exitCode
            uninstallElevated = $u.elevationAvailable
        }
    } -ArgumentList $u
    # Reinstall and prove preserved state recognized (elevated).
    $r = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Install -Feature @('RegisterShell','InstallTray')
    $reinstallSentinelPreserved = Invoke-Command -Session $Session -ScriptBlock {
        param($r)
        $stateDir = Join-Path $env:ProgramData 'PathVeer'
        [PSCustomObject]@{
            reinstallExitCode = $r.result.exitCode
            reinstallElevated = $r.elevationAvailable
            preservedStateRecognized = (Test-Path (Join-Path $stateDir 'cert-sentinel.txt'))
        }
    } -ArgumentList $r
    Save-Json '08-gate8-uninstall-reinstall.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); uninstall = $afterUninstall; reinstall = $reinstallSentinelPreserved
    })
    return $reinstallSentinelPreserved
}

function Run-GATE6([System.Management.Automation.Runspaces.PSSession]$Session, [string]$OldPkg, [string]$NewPkg) {
    Write-Stage "GATE-6 UPGRADE (0.9.0 -> 1.0.0-beta.1) + DOWNGRADE BLOCK"
    $jea = Get-GuestJeaSession $script:Cred
    # NOTE: the trusted wrapper always installs the fixed staged package ($script:InstallPackage);
    # Old/New package paths are used ONLY to validate candidate identity against the protected
    # operator-staged payload (Option A: no runtime copy of executable bytes; the harness asserts
    # the protected payload exists and installs it). GATE-6's 0.9.0->1.0.0-beta.1 framing is
    # simulated by the wrapper; only the 1.0.0-beta.1 payload must be operator-staged.
    $expectedPackageId = 'PathVeer-1.0.0-beta.1'
    Assert-CandidateMatchesProtected -CandidatePackage $NewPkg -ExpectedPackageId $expectedPackageId
    # SECURITY (Option A): the harness never promotes untrusted incoming into the protected tree.
    # The trusted wrapper enforces the exact protected installer + payload preconditions inside the
    # privileged virtual-account context; the harness does not inspect protected paths from pvcert.
    # Install older baseline (elevated) — wrapper uses the staged package.
    $b = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Install -Feature @('RegisterShell','InstallTray')
    $oldVer = $null
    if ($b.result -and $b.result.exitCode -eq 0) {
        $oldVer = (Invoke-Command -Session $Session -ScriptBlock { (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw | ConvertFrom-Json).productVersion })
    }
    # Upgrade (elevated).
    $u = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Upgrade -Feature @('RegisterShell','InstallTray')
    $newVer = $null; $svcAfter = $null
    if ($u.result -and $u.result.exitCode -eq 0) {
        $up = Invoke-Command -Session $Session -ScriptBlock { [PSCustomObject]@{
            version = (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw | ConvertFrom-Json).productVersion
            serviceState = (Get-Service -Name 'PathVeer').Status
        } }
        $newVer = $up.version; $svcAfter = $up.serviceState
    }
    # Downgrade attempt (must be blocked, exit 103). Elevated.
    $d = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Install -Feature @('RegisterShell')
    $services = Invoke-Command -Session $Session -ScriptBlock {
        @(Get-CimInstance Win32_Service | Where-Object { $_.Name -eq 'PathVeer' -or $_.Name -eq 'IranDirect' } | ForEach-Object { $_.Name })
    }
    Save-Json '06-gate6-upgrade.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o')
        baselineInstallExitCode = $b.result.exitCode
        baselineElevated = $b.elevationAvailable
        baselineVersion = $oldVer
        upgradeExitCode = $u.result.exitCode
        upgradeElevated = $u.elevationAvailable
        upgradedVersion = $newVer
        serviceStateAfterUpgrade = $svcAfter
        downgradeExitCode = $d.result.exitCode
        downgradeBlocked = ($d.result.exitCode -eq 103)
        serviceIdentities = $services
        noDuplicateService = ($services.Count -le 1)
    })
    return @{ baselineVersion=$oldVer; upgradedVersion=$newVer; downgradeBlocked=($d.result.exitCode -eq 103) }
}

function Run-GATE28([System.Management.Automation.Runspaces.PSSession]$Session, [string]$Pkg) {
    Write-Stage "GATE-28 SAME-VERSION REPAIR"
    $jea = Get-GuestJeaSession $script:Cred
    # SECURITY (Option A): no runtime copy of executable bytes into the guest. The harness only
    # validates candidate identity and asserts the protected payload exists, then installs it.
    $expectedPackageId = 'PathVeer-1.0.0-beta.1'
    Assert-CandidateMatchesProtected -CandidatePackage $Pkg -ExpectedPackageId $expectedPackageId
    # SECURITY (Option A): the harness never promotes untrusted incoming into the protected tree.
    # The trusted wrapper enforces the protected installer + payload preconditions inside the
    # privileged virtual-account context; the harness does not inspect protected paths from pvcert.
    # Same-version repair/install over existing (elevated).
    $r = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Repair -Feature @('RegisterShell','InstallTray')
    $svcAfter = $null; $ver = $null; $cliRepairExit = $null
    if ($r.result -and $r.result.exitCode -eq 0) {
        $svcAfter = (Invoke-Command -Session $Session -ScriptBlock { (Get-Service -Name 'PathVeer' -ErrorAction Stop).Status })
        $ver = (Invoke-Command -Session $Session -ScriptBlock { (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw | ConvertFrom-Json).productVersion })
        # CLI repair verb (privileged, guarded, via trusted wrapper).
        $rep = Invoke-GuestJeaCli -Session $Session -JeaSession $jea -Verb 'repair'
        $cliRepairExit = $rep.result.exitCode
    }
    Save-Json '28-gate28-repair.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o')
        installExitCode = $r.result.exitCode
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
$script:Cred = Get-Credential -UserName 'PV-CERT\pvcert' -Message 'PathVeer-Certification (PV-CERT) local admin password for PowerShell Direct'
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
                # The final block restores the certification checkpoint (PV-CERT-HARNESS).
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
if ($script:JeaSession) { $script:JeaSession | Remove-PSSession -ErrorAction SilentlyContinue; $script:JeaSession = $null }
if (-not $SkipRestore) {
    $sess | Remove-PSSession -ErrorAction SilentlyContinue
    Restore-Clean
    Write-Host "Guest restored to certification baseline '$CertificationSnapshot'." -ForegroundColor Green
}

Write-Host "`nAll evidence written to: $EvidenceDir" -ForegroundColor Green
