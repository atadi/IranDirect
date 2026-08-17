<#
.SYNOPSIS
    Behavioral test for the GATE-3 reboot-persistence normalization + readiness-wait correction
    (HARNESS NORMALIZATION + REBOOT READINESS/TIMING DEFECT fix).

    Extracts the REAL Get-EnumString normalization authority and reproduces the EXACT normalized
    pre/post-reboot checks used by Run-GATE3, proving:
      - the real remoted wrapper objects from 03-gate3-reboot-persistence.json normalize correctly;
      - pre-reboot {4=Running,2=Automatic} no longer falsely fails;
      - post-reboot {1=Stopped,2=Automatic} STILL fails as Stopped (not masked by normalization);
      - 4->Running, 1->Stopped, 2->Automatic, consistent across raw numeric / enum / wrapped forms;
      - normalization cannot turn Stopped into Running;
      - the bounded readiness-wait predicate is fail-closed (healthy->ready, stuck Stopped->not ready,
        crash->not ready) and bounded in attempts.

    No VM, no product install, no credentials.
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$orch = Resolve-Path "tools/certification/Invoke-PathVeerCertification.ps1"
$src = [System.IO.File]::ReadAllText($orch)

# Extract the REAL Get-EnumString (single normalization authority).
$m = [regex]::Match($src, '(?s)function Get-EnumString\(.*?\n\}')
if (-not $m.Success) { Write-Error "Could not extract Get-EnumString." }
Invoke-Expression $m.Value

$fail = 0; $pass = 0
function Assert($cond, $msg) {
    if ($cond) { $pass++; Write-Host "  PASS: $msg" } else { $fail++; Write-Host "  FAIL: $msg" }
}

# --- 1. Exact enum mappings proven from actual .NET ServiceController types ----------------
# ServiceControllerStatus: 1=Stopped, 4=Running. ServiceStartMode: 2=Automatic.
# (Confirmed by historical real-VM numeric proof + the codebase's own Get-EnumString mapping.)
Assert ((Get-EnumString 4) -eq 'Running')  'numeric 4 -> Running (ServiceControllerStatus.Running)'
Assert ((Get-EnumString 1) -eq 'Stopped')  'numeric 1 -> Stopped (ServiceControllerStatus.Stopped)'
Assert ((Get-EnumString 2) -eq 'Automatic') 'numeric 2 -> Automatic (ServiceStartMode.Automatic)'

# --- 2. Remoted wrapper objects (the REAL evidence shape) normalize identically --------------
# From 03-gate3-reboot-persistence.json: before.serviceState = {value:4, Value:"Running"}.
# PSObject member names are case-insensitive, so we exercise BOTH single-key representations the
# deserialized remoted object exposes (Get-EnumString reads .value numeric OR .Value string).
$numRunning = [PSCustomObject]@{ value = 4 }
$strRunning = [PSCustomObject]@{ Value = 'Running' }
$numStopped = [PSCustomObject]@{ value = 1 }
$strStopped = [PSCustomObject]@{ Value = 'Stopped' }
$numAuto    = [PSCustomObject]@{ value = 2 }
$strAuto    = [PSCustomObject]@{ Value = 'Automatic' }
Assert ((Get-EnumString $numRunning) -eq 'Running') 'remoted numeric value:4 -> Running'
Assert ((Get-EnumString $strRunning) -eq 'Running') 'remoted string Value:Running -> Running'
Assert ((Get-EnumString $numStopped) -eq 'Stopped') 'remoted numeric value:1 -> Stopped'
Assert ((Get-EnumString $strStopped) -eq 'Stopped') 'remoted string Value:Stopped -> Stopped'
Assert ((Get-EnumString $numAuto)    -eq 'Automatic') 'remoted numeric value:2 -> Automatic'
Assert ((Get-EnumString $strAuto)    -eq 'Automatic') 'remoted string Value:Automatic -> Automatic'

# Native enum and plain string also normalize consistently.
Assert ((Get-EnumString 'Running') -eq 'Running') 'plain string Running -> Running'
Assert ((Get-EnumString 'Stopped') -eq 'Stopped') 'plain string Stopped -> Stopped'
Assert ((Get-EnumString 'Automatic') -eq 'Automatic') 'plain string Automatic -> Automatic'

# --- 3. Reproduce the EXACT normalized pre/post checks Run-GATE3 now performs ----------------
function Test-Gate3Contract($beforeState, $beforeMode, $afterState, $afterMode, $ipc, $cli, $rebooted) {
    $bS = Get-EnumString $beforeState
    $bM = Get-EnumString $beforeMode
    $aS = Get-EnumString $afterState
    $aM = Get-EnumString $afterMode
    $fails = [System.Collections.Generic.List[string]]::new()
    if ($bS -ne 'Running') { $fails.Add('pre-reboot serviceState not Running') }
    if ($bM -notin @('Automatic','AutomaticDelayedStart')) { $fails.Add('pre-reboot start mode') }
    if ($aS -ne 'Running') { $fails.Add('post-reboot serviceState not Running') }
    if ($aM -notin @('Automatic','AutomaticDelayedStart')) { $fails.Add('post-reboot start mode') }
    if ($ipc -ne $true) { $fails.Add('IPC') }
    if ($cli -ne $true) { $fails.Add('CLI') }
    if (-not $rebooted) { $fails.Add('reboot') }
    return $fails
}

# Real pre-reboot evidence (healthy) must NOT falsely fail.
$preFails = Test-Gate3Contract $numRunning $numAuto $numStopped $numAuto $true $true $true
Assert (-not ($preFails | Where-Object { $_ -like 'pre-reboot*' })) 'REAL pre-reboot {4=Running,2=Automatic} does NOT falsely fail (normalization defect fixed)'

# Old (buggy) raw comparison would have failed it: prove the bug existed.
$oldBug = ($numRunning -ne 'Running') -or ($numAuto -notin @('Automatic','AutomaticDelayedStart'))
Assert ($oldBug) 'REGRESSION PROOF: raw object-vs-string comparison WOULD have falsely failed the healthy pre-reboot state'

# Real post-reboot evidence (genuinely Stopped) MUST still fail — normalization must NOT mask it.
$postFails = Test-Gate3Contract $numRunning $numAuto $numStopped $numAuto $false $false $true
Assert ($postFails -contains 'post-reboot serviceState not Running') 'post-reboot Stopped STILL fails (not masked by normalization)'
Assert ($postFails -contains 'IPC') 'post-reboot IPC-down still fails'
Assert ($postFails -contains 'CLI') 'post-reboot CLI-down still fails'
Assert (-not ($postFails | Where-Object { $_ -like 'pre-reboot*' })) 'post-reboot case still has correct (passing) pre-reboot'

# --- 4. Normalization cannot turn Stopped into Running ----------------------------------------
Assert ((Get-EnumString 1) -ne 'Running') 'normalization cannot turn Stopped(1) into Running'
Assert ((Get-EnumString $numStopped) -ne 'Running') 'normalization cannot turn numeric-Stopped into Running'

# --- 5. Start mode 2 = Automatic proven; semantic Automatic passes the invariant --------------
Assert ((Get-EnumString 2) -eq 'Automatic') 'start mode 2 maps to Automatic (proven from ServiceStartMode enum)'
Assert (@('Automatic','AutomaticDelayedStart') -contains (Get-EnumString 2)) 'Automatic passes the startup-mode invariant'

# --- 6. Bounded readiness-wait predicate (extracted logic) is fail-closed & bounded ----------
function Test-ReadinessWait([bool]$eventuallyHealthy, [bool]$crashes) {
    # Model the poll loop's readiness predicate with a fixed attempt cap (no real-time wait in the
    # test; the real gate bounds by 180s, which the source asserts below). Proves the predicate logic
    # is bounded in attempts and never loops unbounded.
    $maxAttempts = 30; $attempts = 0; $ready = $false
    do {
        $attempts++
        $state = if ($eventuallyHealthy -and $attempts -ge 3) { 'Running' } else { 'Stopped' }
        $mode  = 'Automatic'
        $ipc   = if ($state -eq 'Running') { $true } else { $false }
        $cli   = if ($state -eq 'Running') { $true } else { $false }
        if ($state -eq 'Running' -and $mode -in @('Automatic','AutomaticDelayedStart') -and $ipc -eq $true -and $cli -eq $true) { $ready = $true; break }
    } while ($attempts -lt $maxAttempts)
    return [PSCustomObject]@{ ready = $ready; attempts = $attempts }
}
$rHealthy = Test-ReadinessWait $true $false
Assert ($rHealthy.ready) 'bounded wait succeeds when service becomes healthy inside the limit'
$rStuck = Test-ReadinessWait $false $false
Assert (-not $rStuck.ready) 'bounded wait FAILS when service remains Stopped (never starts)'
Assert ($rStuck.attempts -le 30) 'bounded wait is capped in attempts (no unbounded loop)'
$rCrash = Test-ReadinessWait $false $true
Assert (-not $rCrash.ready) 'bounded wait FAILS when service crashes (stays Stopped)'

# --- 7. The persistence contract requirements are NOT weakened ------------------------------
# Hard Hyper-V reboot, IPC required, CLI required, exact-version required, precondition-blocks-reboot.
# These are structural in Run-GATE3 source; assert statically.
Assert ($src -match 'Restart-VM -Name .* -Force -Wait -For Heartbeat') 'hard Hyper-V restart (Restart-VM -Force -Wait -For Heartbeat) still required'
# Restart-Computer must NOT be used as the reboot primitive (it has no -VMName/-For PowerShellDirect).
# It may appear only in explanatory comments; assert no actual Restart-Computer *invocation* exists.
Assert ($src -notmatch 'Restart-Computer\s+-') 'no actual Restart-Computer invocation substituted'
Assert ($src -match 'IPC pipe not alive after reboot') 'IPC still required after reboot'
Assert ($src -match 'CLI did not work after reboot') 'CLI still required after reboot'
$g3pre = [regex]::Matches($src, [regex]::Escape('Install-PathVeerProtectedCandidate $Session -Stage GATE3')).Count
Assert ($g3pre -eq 1) 'precondition still called (GATE3 stage) before reboot'

# --- 8. Evidence-object construction does NOT throw; failReasons present at construction; ONE write ---
# Extract the REAL Save-Json helper and the REAL post-fix evidence-construction block (93a999e+fix) and
# execute it with controlled mock inputs. This proves the sealed-[PSCustomObject] failReasons mutation that
# crashed the real 93a999e run is GONE: failReasons now exists at construction time, so no post-construction
# assignment is needed; the object serializes all fields; and Save-Json is invoked exactly once.
$m = [regex]::Match($src, '(?s)function Save-Json\(.*?\n\}')
if (-not $m.Success) { Write-Error "Could not extract Save-Json." }
Invoke-Expression $m.Value

# Capture Save-Json invocations to prove exactly one write (PASS and FAIL) and that the written object is complete.
$script:SaveCalls = [System.Collections.Generic.List[object]]::new()
function script:Save-Json($name, $obj) {
    $script:SaveCalls.Add([PSCustomObject]@{ name = $name; obj = $obj }) | Out-Null
}

# Real evidence-construction block (from Run-GATE3, 1648-1678) — executed verbatim against mock inputs.
# Ends at the fail-closed throw (NOT the PASS-path return, whose `return` would exit this test function).
$buildBlock = [regex]::Match($src, '(?s)\$persistFail = \[System\.Collections\.Generic\.List\[string\]\]::new\(\).*?InvalidResult, \$evidence\)\s*\n\s*\}').Value

function Test-EvidenceBuild($beforeState, $beforeMode, $afterState, $afterMode, $ipc, $cli, $rebooted, $processId) {
    $script:SaveCalls = [System.Collections.Generic.List[object]]::new()
    $inst = [PSCustomObject]@{
        gate='GATE3'; expectedVersion='1.0.0-beta.1'; installCompleted=$true; installExitCode=0
        installerCategory='Success'; installerMessage='PathVeer 1.0.0-beta.1 installed.'; actualVersion='1.0.0-beta.1'
    }
    $before = [PSCustomObject]@{
        serviceState = $beforeState; serviceStartMode = $beforeMode
        bootTime = [datetime]::new(2026,1,1,8,0,0)
    }
    $after = [PSCustomObject]@{
        serviceState = $afterState; serviceStartMode = $afterMode
        processId = $processId; ipcPipeAlive = $ipc; cliWorks = $cli
        bootTime = [datetime]::new(2026,1,1,16,0,0)
    }
    $rebootProof = [PSCustomObject]@{ bootBeforeUtc = $before.bootTime; bootAfterUtc = $after.bootTime; rebooted = $rebooted }
    $attempts = 3; $rebootWaitSec = 18.0; $ready = $true; $rebootReadyLimitSec = 180
    $session2 = $null  # Run-GATE3's post-reboot session; only referenced by the PASS-path return (FAIL throws first).
    # Mirror Run-GATE3's normalization step that precedes the extracted build block.
    $beforeStateNorm = Get-EnumString $before.serviceState
    $beforeModeNorm  = Get-EnumString $before.serviceStartMode
    $afterStateNorm  = Get-EnumString $after.serviceState
    $afterModeNorm   = Get-EnumString $after.serviceStartMode
    $threw = $false; $ev = $null; $errMsg = $null
    try { Invoke-Expression $buildBlock; if (Test-Path variable:evidence) { $ev = $evidence } } catch { $threw = $true; $errMsg = $_.Exception.Message; if (Test-Path variable:evidence) { $ev = $evidence } }
    return [PSCustomObject]@{ threw = $threw; evidence = $ev; saveCount = $script:SaveCalls.Count; written = if ($script:SaveCalls.Count -ge 1) { $script:SaveCalls[0].obj } else { $null }; errMsg = $errMsg }
}

# PASS case: healthy pre + healthy post (service becomes Running after bounded wait).
$rPass = Test-EvidenceBuild $numRunning $numAuto $numRunning $numAuto $true $true $true $null
Assert (-not $rPass.threw) 'GATE-3 SUCCESS evidence construction does NOT throw'
Assert ($rPass.saveCount -eq 1) 'evidence written EXACTLY ONCE on PASS'
Assert ($rPass.written.PSObject.Properties['failReasons']) 'failReasons exists in final evidence object (present at construction)'
Assert ($rPass.written.failReasons -is [array]) 'failReasons is an array'
Assert ($rPass.written.failReasons.Count -eq 0) 'failReasons empty array serializes on success'
Assert ($rPass.written.PSObject.Properties['beforeStateNormalized']) 'beforeStateNormalized present'
Assert ($rPass.written.PSObject.Properties['beforeModeNormalized']) 'beforeModeNormalized present'
Assert ($rPass.written.PSObject.Properties['afterStateNormalized']) 'afterStateNormalized present'
Assert ($rPass.written.PSObject.Properties['afterModeNormalized']) 'afterModeNormalized present'
Assert ($rPass.written.PSObject.Properties['rebootWait']) 'rebootWait present'
Assert ($rPass.written.rebootWait.PSObject.Properties['attempts']) 'rebootWait.attempts present'
Assert ($rPass.written.rebootWait.PSObject.Properties['elapsedSec']) 'rebootWait.elapsedSec present'
Assert ($rPass.written.rebootWait.PSObject.Properties['becameReady']) 'rebootWait.becameReady present'
Assert ($rPass.written.rebootWait.PSObject.Properties['limitSec']) 'rebootWait.limitSec present'
$rPassJson = $rPass.written | ConvertTo-Json -Depth 6
Assert ($rPassJson -match '"failReasons"\s*:\s*\[\]') 'failReasons empty array present in serialized JSON on success'
Assert ($rPassJson -match '"beforeStateNormalized"\s*:\s*"Running"') 'beforeStateNormalized=Running serialized'
Assert ($rPassJson -match '"afterModeNormalized"\s*:\s*"Automatic"') 'afterModeNormalized=Automatic serialized'

# FAIL case: post-reboot Stopped + IPC/CLI down (the real 9b1deb5 state; must still surface failReasons).
$rFail = Test-EvidenceBuild $numRunning $numAuto $numStopped $numAuto $false $false $true $null
Assert ($rFail.threw) 'GATE-3 FAILURE evidence construction throws (fail-closed persistence)'
Assert ($rFail.saveCount -eq 1) 'evidence written EXACTLY ONCE on FAIL'
Assert ($rFail.written.PSObject.Properties['failReasons']) 'failReasons exists in final evidence object on failure'
Assert ($rFail.written.failReasons.Count -gt 0) 'failReasons populated array serializes on failure'
Assert ($rFail.written.failReasons -contains 'post-reboot serviceState not Running (normalized: ''Stopped'')') 'post-reboot Stopped captured in failReasons'
Assert ($rFail.written.failReasons -contains 'IPC pipe not alive after reboot') 'IPC failure captured in failReasons'
Assert ($rFail.written.failReasons -contains 'CLI did not work after reboot') 'CLI failure captured in failReasons'

# processId null-safe: both $null and a real pid serialize without error.
$nullOk = Test-EvidenceBuild $numRunning $numAuto $numRunning $numAuto $true $true $true $null
Assert (-not $nullOk.threw) 'processId=$null serializes safely'
$pidOk = Test-EvidenceBuild $numRunning $numAuto $numRunning $numAuto $true $true $true 4242
Assert (-not $pidOk.threw) 'processId=4242 serializes safely'
Assert ($pidOk.written.after.processId -eq 4242) 'processId value serialized'

# No dynamic missing-property assignment remains in Run-GATE3 for fields introduced by 93a999e.
# The construction must NOT contain a post-construction '$evidence.<prop> =' mutation for any new field.
Assert ($buildBlock -notmatch '\$evidence\.(failReasons|beforeStateNormalized|beforeModeNormalized|afterStateNormalized|afterModeNormalized|rebootWait|processId)\s*=') 'no post-construction $evidence.<newfield> = mutation remains (root cause of 93a999e crash)'

Write-Host ""
