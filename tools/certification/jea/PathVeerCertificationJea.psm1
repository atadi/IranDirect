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
# DETERMINISTIC protected result directory for privileged installer result/progress files.
# Deliberately NOT $env:TEMP / $env:TMP / [IO.Path]::GetTempPath(): in a JEA WinRM virtual-account
# session those environment variables are not guaranteed to be populated, which would make
# Join-Path $env:TEMP (...) produce a null path and the following Remove-Item -LiteralPath $null
# throw (masking the real error and preventing a lifecycle object from being returned). We use a
# static path under the protected certification root instead, created/ACL'd by the bootstrap.
$script:ResultsDir = Join-Path $script:BaseDir 'Results'

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
    # NOTE: do NOT use Get-CimInstance Win32_Service for StartType. The restricted JEA session
    # cannot resolve Get-CimInstance (CimCmdlets is intentionally not exposed; it remains in the
    # forbidden command list), so the CIM call throws and the trusted wrapper crashes during
    # post-install result construction. The ServiceController returned by Get-Service already
    # exposes StartType, which is sufficient and JEA-safe.
    [PSCustomObject]@{
        exists = $true
        status = $svc.Status
        startType = $svc.StartType
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
    # Lifecycle / result capture. These DISAMBIGUATE the installer's actual execution from the
    # wrapper's own return. They replace the misleading single 'completed' flag the harness used to
    # read, because 'wrapper returned without an error string' is NOT 'installer completed'.
    $installerPathValidated        = $false
    $payloadPathValidated          = $false
    $installerInvocationAttempted  = $false
    $installerStarted              = $false
    $installerReturned             = $false
    $installerExitCode             = $null
    $installerError                = $null
    $installerResult               = $null
    $installerProgress             = $null
    $childError                    = $null
    $resultFile   = $null
    $progressFile = $null
    $errFile      = $null

    $exitCode = $null; $errorMsg = $null; $featList = @()
    try {
        if (-not (Test-Path -LiteralPath $script:InstallScript)) {
            throw "Certification installer not staged at $($script:InstallScript). Stage the package first."
        }
        $installerPathValidated = $true
        if (-not (Test-Path -LiteralPath $script:InstallPackage)) {
            throw "Certification payload not staged at $($script:InstallPackage). Stage the package first."
        }
        $payloadPathValidated = $true

        # The installer writes STRUCTURED result/progress to files when given -ResultFile/-ProgressFile
        # (see Install-PathVeer.ps1 Phase 37.2 contract). We place them in a DETERMINISTIC protected
        # certification directory (NOT $env:TEMP, which is not guaranteed populated in a JEA WinRM
        # virtual-account session). The harness never reads these paths directly; the structured
        # contents are surfaced in the return object. No caller-supplied path.
        if (-not (Test-Path -LiteralPath $script:ResultsDir -PathType Container)) {
            New-Item -ItemType Directory -Force -Path $script:ResultsDir | Out-Null
        }
        $resultFile   = Join-Path $script:ResultsDir ('pathveer-cert-install-' + [guid]::NewGuid().ToString('N') + '.json')
        $progressFile = Join-Path $script:ResultsDir ('pathveer-cert-progress-' + [guid]::NewGuid().ToString('N') + '.json')
        $errFile      = Join-Path $script:ResultsDir ('pathveer-cert-err-' + [guid]::NewGuid().ToString('N') + '.txt')

        # Build a fixed, validated argument list. No caller-supplied paths/strings reach the process.
        # Translate the CERTIFICATION harness action vocabulary to the PRODUCT installer contract.
        # The product installer (tools/Install-PathVeer.ps1) supports install / uninstall (-PurgeState) /
        # status / statejson. It has NO 'PurgeUninstall', 'Repair', or 'Upgrade' action and rejects any of
        # those at parameter binding before the body runs, so the product body never executes. The harness
        # may keep using 'PurgeUninstall' / 'Repair' / 'Upgrade' as convenient semantics; we translate them
        # to the product 'install' contract so the product repair/upgrade body actually runs.
        #
        # AUTHORITATIVE SAME-VERSION REPAIR / UPGRADE PRIMITIVE: Invoke-Install (product) branches on
        # $previousVersion. It only blocks when installed is NEWER (downgrade, Phase 37.2). A SAME version
        # is NOT blocked, and runs the full repair sequence: stop service -> replace binaries -> re-point
        # SCM -> re-add CLI/tray -> restart -> wait Running -> readiness check -> re-register Apps&Features
        # (if -RegisterShell) -> rewrite manifest -> emit success. Persistent state ($env:ProgramData\PathVeer)
        # is untouched. So both GATE-28 Repair (same version) and GATE-6 Upgrade (newer version) map to
        # product 'install' with the same protected package + features. This is the SAME vocabulary-mismatch
        # class previously fixed for PurgeUninstall (GATE-4): the harness exposed a higher-level semantic
        # that the product contract does not name directly; the adapter translates intent to the product
        # primitive rather than inventing an unsupported -Action. (GATE-28 root cause: 'Repair' reached the
        # product as -Action Repair and was rejected at binding -> exit 1, no result record.)
        #
        # The install path registered Apps&Features + Start Menu entries via -RegisterShell, so the symmetric
        # uninstall must request the same cleanup (-RegisterShell) or the Apps&Features registration survives
        # — breaking the GATE-4/GATE-8 postconditions. Pass -RegisterShell for both Uninstall and
        # PurgeUninstall; the harness 'Uninstall' maps to product 'uninstall' WITHOUT -PurgeState (GATE-8
        # state-preservation contract). Install/Upgrade/Repair map to product 'install' (+ their features).
        $productAction = $Action
        $addPurgeState = $false
        $addRegisterShell = $false
        switch ($Action) {
            'PurgeUninstall' { $productAction = 'uninstall'; $addPurgeState = $true;  $addRegisterShell = $true }
            'Uninstall'      { $productAction = 'uninstall';                         $addRegisterShell = $true }
            'Upgrade'        { $productAction = 'install' }
            'Repair'         { $productAction = 'install' }
        }
        $psiArgs = @('-NoProfile', '-File', $script:InstallScript)
        $psiArgs += '-Action';  $psiArgs += $productAction
        $psiArgs += '-PackageDirectory'; $psiArgs += $script:InstallPackage
        # Split + validate the comma-joined feature string against the fixed set (trusted code).
        foreach ($tok in ($Feature -split ',')) {
            $t = $tok.Trim()
            if ($t -eq 'RegisterShell' -or $t -eq 'InstallTray') { $featList += $t }
        }
        if ($featList.Count -eq 0) { throw 'No valid feature specified.' }
        if ($Action -in @('Install','Upgrade','Repair')) {
            foreach ($f in $featList) { $psiArgs += "-$f" }
        }
        if ($Action -in @('Uninstall','PurgeUninstall')) {
            $psiArgs += '-RegisterShell'
        }
        if ($addPurgeState) { $psiArgs += '-PurgeState' }
        $psiArgs += '-ResultFile';   $psiArgs += $resultFile
        $psiArgs += '-ProgressFile'; $psiArgs += $progressFile

        # Launch an EXPLICIT powershell host to run the installer.
        # DO NOT use (Get-Process -Id $pid).Path here: inside a JEA/WinRM session $pid is
        # wsmprovhost.exe, which does not accept -File/-NoProfile and would silently fail to
        # execute the installer (script returns without entering the install body, no result file).
        # An explicit powershell.exe is always available to the virtual account and runs the
        # installer as the same privileged identity. The path and arguments are fixed/validated here.
        # Synchronous ('&' waits for the child to exit).
        $installerInvocationAttempted = $true
        # Resolve the Windows PowerShell host WITHOUT relying on nullable JEA environment variables.
        # $env:SystemRoot / $env:WINDIR / $env:TEMP are frequently ABSENT inside a
        # RunAsVirtualAccount WinRM session (this exact null-bind broke the prior attempt), so we
        # resolve the system directory via the OS itself, not an env var. GetFolderPath(System)
        # returns e.g. 'C:\Windows\system32' through kernel folder resolution and is always
        # available to the virtual account. (We intentionally launch Windows PowerShell, never
        # wsmprovhost.exe.)
        $systemDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::System)
        if (-not $systemDir) { $systemDir = 'C:\Windows\system32' }
        $hostExe = Join-Path $systemDir 'WindowsPowerShell\v1.0\powershell.exe'
        if (-not (Test-Path -LiteralPath $hostExe)) {
            throw ("Windows PowerShell executable not found at expected certification host path: " + $hostExe)
        }
        $installerStarted = $true   # the verified host process is now launched (synchronous '&' below)
        # Suppress the child installer's OWN console (stdout) output. The trusted function's
        # structured contract is the SINGLE PSCustomObject returned below (L241); the child's
        # stdout must NOT enter the JEA success stream, or Invoke-Command returns
        # [child stdout...] + [final object] as an Object[] and the Desktop bridge's
        # $res.PSObject.Properties.Match('childUser') sees 0 matches -> null/false telemetry
        # (exactly the real-VM GATE-5 childUser=null / childIsAdministrator=false symptom).
        # Diagnostic evidence is preserved: stderr is captured to $errFile, and the installer writes its
        # real result/progress to $resultFile/$progressFile (parsed below), not to stdout. Capturing stderr
        # to a file (NOT merged into the success stream) keeps the single-object contract intact while making
        # child parameter-binding failures (e.g. an invalid -Action) visible instead of collapsing into a
        # null exit code. $LASTEXITCODE is set by the external process and is unaffected by stdout suppression.
        $null = & $hostExe @psiArgs 2> $errFile
        $installerReturned  = $true
        $installerExitCode  = $LASTEXITCODE
        if (Test-Path -LiteralPath $resultFile) {
            try { $installerResult = (Get-Content -LiteralPath $resultFile -Raw -ErrorAction Stop | ConvertFrom-Json) } catch {}
        }
        if (Test-Path -LiteralPath $progressFile) {
            try { $installerProgress = (Get-Content -LiteralPath $progressFile -Raw -ErrorAction Stop) } catch {}
        }
        if (Test-Path -LiteralPath $errFile) {
            try { $childError = (Get-Content -LiteralPath $errFile -Raw -ErrorAction Stop).Trim() } catch { $childError = $null }
        }
    } catch {
        $errorMsg = $_.Exception.Message
        if (-not $installerError) { $installerError = $errorMsg }
    } finally {
        # Null-safe cleanup: only remove if a path was actually assigned. A null path must never cause
        # Remove-Item -LiteralPath to throw, because that would mask the real error and prevent the
        # lifecycle object from being returned to the caller.
        if ($resultFile)   { Remove-Item -LiteralPath $resultFile   -ErrorAction SilentlyContinue }
        if ($progressFile) { Remove-Item -LiteralPath $progressFile -ErrorAction SilentlyContinue }
        if ($errFile)      { Remove-Item -LiteralPath $errFile      -ErrorAction SilentlyContinue }
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
        # Backward-compatible aliases: GATE-6/28 and the bridge read .exitCode / .error. These map
        # to the explicit installer lifecycle fields below so the richer schema is additive, not a break.
        exitCode = $installerExitCode
        error = $installerError
        installerPathValidated = $installerPathValidated
        payloadPathValidated = $payloadPathValidated
        installerInvocationAttempted = $installerInvocationAttempted
        installerStarted = $installerStarted
        installerReturned = $installerReturned
        installerExitCode = $installerExitCode
        installerError = $installerError
        installerResult = $installerResult
        installerProgress = $installerProgress
        # Normalized product-operation diagnostics (visible to the orchestrator independent of the
        # bridge wrapper). productResultMissing = $true means the product installer wrote NO result
        # record — its body either never ran (e.g. parameter-binding rejection of an invalid -Action)
        # or crashed before Write-ResultRecord. That is the exact signature of the GATE-4 harness
        # invocation defect and must FAIL-CLOSED at the gate, never be read as success.
        wrapperError = $installerError
        childExitCode = $installerExitCode
        childError = $childError
        productResultMissing = ($null -eq $installerResult)
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
        # AUTHORITATIVE SubVerb grammar for the certification JEA CLI bridge.
        # SINGLE SOURCE OF TRUTH for the PowerShell parameter binder: every subverb the
        # PathVeer custom-routes CLI actually supports MUST be listed here. The Desktop-side
        # bridge (PathVeer.Certification.Jea.ps1 $validSub) is a defense-in-depth mirror of this
        # exact set; Test-PathVeerCertGuestJeaBinder.ps1 enforces that the two NEVER drift.
        # Do NOT shrink this set without also updating the bridge + re-running the binder test,
        # or the next real VM run will fail at parameter binding (cf. 2949ed9: 'resolve' rejected
        # by a stale ValidateSet('add-cidr','list') before reaching product).
        [ValidateSet('list','add-domain','add-ip','add-cidr','enable','disable','remove','resolve','status','invalidate','invalidate-all')]
        [string]$SubVerb = 'list',

        [ValidateSet('status','repair','doctor','enable','disable','custom-routes')]
        [string]$Verb = 'status',

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
        boundary, PLUS a read-only view of the current protected certification baseline readiness.

        The forbidden-command definition (fact A) is a trusted fact. The caller-visible measurement
        (fact B) is taken by the DESKTOP probe in the restricted JEA session, never from here.

        The baseline-readiness fields (expectedPackageId / protected path presence + basename
        checks) are ALSO trusted facts computed here, because they read the module's own resolved
        protected paths (C:\ProgramData\PathVeerCertificationJea\{Trusted,Payloads}). This is the
        read-only harness-baseline precondition: if the protected installer/payload are absent or
        misnamed, no install-dependent GATE (2/3/4/8/28) may run. Reusing this existing privileged
        function keeps VisibleFunctions at exactly 12 (no new privileged surface).
    #>
    [CmdletBinding()]
    param(
        [string]$ExpectedInstallerBasename = 'Install-PathVeer.ps1',
        [string]$ExpectedPayloadBasename   = 'PathVeer-1.0.0-beta.1'
    )
    # Identity is the basename only (the protected tree holds one promoted candidate). We do NOT
    # read package internals; presence at the fixed protected path IS the trusted identity.
    $installerPresent = Test-Path -LiteralPath $script:InstallScript
    $payloadPresent   = Test-Path -LiteralPath $script:InstallPackage
    $installerBasenameOk = ($installerPresent -and ([System.IO.Path]::GetFileName($script:InstallScript) -eq $ExpectedInstallerBasename))
    $payloadBasenameOk   = ($payloadPresent   -and ([System.IO.Path]::GetFileName($script:InstallPackage)  -eq $ExpectedPayloadBasename))
    [PSCustomObject]@{
        # Canonical dangerous-command definition (trusted fact). The probe compares the restricted
        # session's OWN Get-Command output against this list Desktop-side.
        forbiddenDefined = @(
            'powershell.exe', 'cmd.exe', 'pwsh.exe', 'wscript.exe', 'cscript.exe', 'mshta.exe',
            'Start-Process', 'Invoke-Expression', 'Invoke-Command', 'Invoke-WebRequest',
            'New-ScheduledTask', 'Register-ScheduledTask', 'Set-Content', 'Set-Item',
            'New-Item', 'Invoke-Item', 'Get-CimInstance'
        )
        # Read-only certification-baseline readiness (trusted facts, computed from protected paths).
        expectedPackageId       = $ExpectedPayloadBasename
        trustedInstallerPath    = $script:InstallScript
        protectedPayloadPath    = $script:InstallPackage
        installerPresent        = $installerPresent
        payloadPresent          = $payloadPresent
        installerBasenameOk     = $installerBasenameOk
        payloadBasenameOk       = $payloadBasenameOk
        baselineReady           = ($installerBasenameOk -and $payloadBasenameOk)
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
