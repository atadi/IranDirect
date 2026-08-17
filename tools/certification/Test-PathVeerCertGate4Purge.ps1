<#
.SYNOPSIS
    GATE-4 PURGE contract tests (certification-harness fix, NOT a product change).

    Proves:
      1. The JEA module translates the harness 'PurgeUninstall' semantic to the PRODUCT contract
         'uninstall -PurgeState' (the product installer has NO 'PurgeUninstall' action and rejects it
         at parameter binding — that was the GATE-4 harness invocation defect).
      2. The product args for PurgeUninstall contain -Action uninstall AND -PurgeState, and NEVER
         contain -Action PurgeUninstall.
      3. The harness 'Uninstall' semantic maps to product 'uninstall' WITHOUT -PurgeState (GATE-8
         normal-uninstall state-preservation contract).
      4. Normal Uninstall never accidentally receives -PurgeState.
      5. Run-GATE4 reads the NORMALIZED product result (installerInvocationAttempted / installerStarted /
         installerExitCode / installerResult / productResultMissing) and does NOT treat elevationAvailable
         as product-success.
      6. Null / malformed / nonzero / failed product result FAILS CLOSED and SKIPS reinstall.
      7. Only a concrete successful product result + all postconditions may justify reinstall.

    The adapter block is EXTRACTED VERBATIM from the committed JEA module so the test exercises the real
    translation code, not a copy. The Run-GATE4 decision block is likewise extracted from the committed
    orchestrator so the fail-closed sequencing is tested against the actual shipped logic.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSScriptRootReal = Split-Path -Parent $MyInvocation.MyCommand.Definition
$Root = Resolve-Path (Join-Path $PSScriptRootReal '..\..')
$psm1 = Join-Path $Root 'tools/certification/jea/PathVeerCertificationJea.psm1'
$orch = Join-Path $Root 'tools/certification/Invoke-PathVeerCertification.ps1'

# --- tiny assertion harness (no external deps) ---
$pass = [System.Collections.Generic.List[string]]::new()
$fail = [System.Collections.Generic.List[string]]::new()
function Assert-True($Cond, $Name) { if ($Cond) { [void]$script:pass.Add($Name) } else { [void]$script:fail.Add($Name) } }

# ===================================================================
# A) ADAPTER: extract the REAL arg-build block from the JEA module
# ===================================================================
$psm1Text = Get-Content -LiteralPath $psm1 -Raw
# The translation + arg-build block begins at '$productAction = $Action' and ends at the
# '-ProgressFile' append. It is the authoritative shipped code.
$m = [regex]::Match($psm1Text, '(?s)\$productAction = \$Action.*?\$psiArgs \+= ''-ProgressFile''; \$psiArgs \+= \$progressFile')
Assert-True $m.Success 'extracted JEA arg-build block from committed psm1'
$block = $m.Value
# Adapt the extracted block for standalone execution: replace $script:-scoped paths with locals.
$block = $block -replace '\$script:InstallScript', '$installScriptPath' -replace '\$script:InstallPackage', '$installPackagePath'

function Build-PurgeArgs($Action, $Feature='RegisterShell,InstallTray') {
    $installScriptPath = 'C:\ProgramData\PathVeerCertificationJea\Trusted\Install-PathVeer.ps1'
    $installPackagePath = 'C:\ProgramData\PathVeerCertificationJea\Payloads\PathVeer-1.0.0-beta.1'
    $featList = @()
    $psiArgs = $null
    $src = @"
`$Action = '$Action'
`$Feature = '$Feature'
$block
"@
    Invoke-Expression $src
    return $psiArgs
}

# --- A1: PurgeUninstall -> product 'uninstall -PurgeState', never 'PurgeUninstall' ---
$pArgs = Build-PurgeArgs 'PurgeUninstall'
$argStr = $pArgs -join ' '
Assert-True ($pArgs -contains '-Action' -and ($pArgs[[array]::IndexOf($pArgs,'-Action')+1]) -eq 'uninstall') 'PurgeUninstall -> product -Action uninstall'
Assert-True ($pArgs -contains '-PurgeState') 'PurgeUninstall -> product -PurgeState present'
Assert-True ($argStr -notmatch '-Action\s+PurgeUninstall') 'PurgeUninstall NEVER passes -Action PurgeUninstall'
Assert-True ($pArgs -contains '-RegisterShell') 'PurgeUninstall -> -RegisterShell (Apps&Features cleanup)'

# --- A2: Uninstall -> product 'uninstall' WITHOUT -PurgeState (GATE-8 contract) ---
$uArgs = Build-PurgeArgs 'Uninstall'
$uStr = $uArgs -join ' '
Assert-True ($uArgs -contains '-Action' -and ($uArgs[[array]::IndexOf($uArgs,'-Action')+1]) -eq 'uninstall') 'Uninstall -> product -Action uninstall'
Assert-True ($uArgs -notcontains '-PurgeState') 'Uninstall -> NO -PurgeState (GATE-8 state preserved)'
Assert-True ($uArgs -contains '-RegisterShell') 'Uninstall -> -RegisterShell (Apps&Features cleanup symmetric with install)'

# --- A3: Install still forwards feature flags (regression guard) ---
$iArgs = Build-PurgeArgs 'Install'
Assert-True ($iArgs -contains '-RegisterShell' -and $iArgs -contains '-InstallTray') 'Install -> -RegisterShell -InstallTray preserved'
Assert-True ($iArgs -notcontains '-PurgeState') 'Install -> NO -PurgeState'

# ===================================================================
# B) RESULT-SHAPE: extract the REAL Run-GATE4 purge decision block
# ===================================================================
$orchText = Get-Content -LiteralPath $orch -Raw
# From '$ir = if ($u.result)' through '$reinstallSkipped = $false' (the normalized result + fail-closed
# sequencing + throw + reinstallSkipped flag). This is the authoritative shipped decision logic.
$m2 = [regex]::Match($orchText, '(?s)\$ir = if \(\$u\.result\).*?\$reinstallSkipped = \$false')
Assert-True $m2.Success 'extracted Run-GATE4 purge decision block from committed orchestrator'
$decision = $m2.Value

# Stub Save-Json so the FAIL path can record evidence without touching disk; capture the thrown evidence.
$script:LastSaved = $null
function Save-Json($Name, $Obj) { $script:LastSaved = [PSCustomObject]@{ Name = $Name; Obj = $Obj } }

function Build-PurgeResult($props) {
    # Build a bridge-shaped $u object (what Run-GATE4 receives from Invoke-GuestJeaInstall).
    $ir = [PSCustomObject]@{}
    foreach ($k in @('installerInvocationAttempted','installerStarted','installerExitCode','installerError','wrapperError','productResultMissing','childExitCode','childError','installerResult','installerProgress')) {
        if ($props.ContainsKey($k)) { $ir | Add-Member -NotePropertyName $k -NotePropertyValue $props[$k] }
    }
    $u = [PSCustomObject]@{}
    if ($props.ContainsKey('completed')) { $u | Add-Member -NotePropertyName 'completed' -NotePropertyValue $props['completed'] }
    if ($props.ContainsKey('elevationAvailable')) { $u | Add-Member -NotePropertyName 'elevationAvailable' -NotePropertyValue $props['elevationAvailable'] }
    if ($props.ContainsKey('elevationSucceeded')) { $u | Add-Member -NotePropertyName 'elevationSucceeded' -NotePropertyValue $props['elevationSucceeded'] }
    $u | Add-Member -NotePropertyName 'result' -NotePropertyValue $ir
    return $u
}

function Run-Decision($u, $afterUninstall) {
    $inst = [PSCustomObject]@{ expectedVersion='1.0.0-beta.1' }
    $afterInstall = [PSCustomObject]@{ serviceExists=$true; programFiles=$true; programData=$true }
    $script:LastSaved = $null
    # The extracted decision block contains the live-state probe '$afterUninstall = Invoke-Command ...'.
    # Mock Invoke-Command to return the controlled post-state so the real sequencing logic is exercised.
    $script:AfterMock = $afterUninstall
    function Invoke-Command { param($Session,$ScriptBlock,$ArgumentList); return $script:AfterMock }
    $threw = $false; $skip = $null; $blockErr = $null
    try {
        Invoke-Expression @"
`$u = `$u
`$afterUninstall = `$afterUninstall
`$inst = `$inst
`$afterInstall = `$afterInstall
$decision
`$script:ReinstallSkippedOut = `$reinstallSkipped
"@
    } catch {
        $threw = $true; $skip = $true; $blockErr = $_.Exception.Message
    }
    if (-not $threw) { $skip = $script:ReinstallSkippedOut }
    return [PSCustomObject]@{ Threw=$threw; ReinstallSkipped=$skip; Error=$blockErr; Saved=$script:LastSaved }
}

function AfterState($svc, $pf, $pd, $aaf) {
    [PSCustomObject]@{ serviceExists=$svc; programFiles=$pf; programData=$pd; appsAndFeaturesEntry=$aaf; uninstallExitCode=$null; uninstallElevated=$true }
}
function ProductResult($success, $category, $message) {
    [PSCustomObject]@{ success=$success; category=$category; message=$message; version='1.0.0-beta.1'; timestamp='2026-08-16T00:00:00Z' }
}

# --- B1: success path (exitCode 0 + success result + all postconditions clean) -> PASS, reinstall invoked ---
$u = Build-PurgeResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=0; productResultMissing=$false; installerResult=(ProductResult $true 'Success' 'PathVeer uninstalled.') }
$r = Run-Decision $u (AfterState $false $false $false $false)
Assert-True (-not $r.Threw) 'B1 product success + clean postconditions -> purge decision does NOT throw'
Assert-True ($r.ReinstallSkipped -eq $false) 'B1 reinstallSkipped=false (reinstall proceeds)'

# --- B2: wrapper result null -> FAIL (case A) ---
$u = [PSCustomObject]@{ completed=$null; elevationAvailable=$true }   # no .result
$r = Run-Decision $u (AfterState $true $true $true $true)
Assert-True $r.Threw 'B2 null result object -> FAIL (reinstall skipped)'
Assert-True $r.ReinstallSkipped 'B2 null result -> reinstallSkipped=true'

# --- B3: installerResult null (product body never ran) -> FAIL ---
$u = Build-PurgeResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=1; productResultMissing=$true; installerResult=$null }
$r = Run-Decision $u (AfterState $true $true $true $true)
Assert-True $r.Threw 'B3 installerResult null -> FAIL (reinstall skipped)'
Assert-True $r.ReinstallSkipped 'B3 installerResult null -> reinstallSkipped=true'

# --- B4: installerExitCode missing -> FAIL ---
$u = Build-PurgeResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=$null; productResultMissing=$false; installerResult=$null }
$r = Run-Decision $u (AfterState $true $true $true $true)
Assert-True $r.Threw 'B4 installerExitCode null -> FAIL (reinstall skipped)'

# --- B5: installerExitCode nonzero -> FAIL ---
$u = Build-PurgeResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=109; productResultMissing=$false; installerResult=(ProductResult $false 'PurgeFailed' 'state delete failed') }
$r = Run-Decision $u (AfterState $true $true $true $true)
Assert-True $r.Threw 'B5 nonzero installerExitCode -> FAIL (reinstall skipped)'

# --- B6: product category failure -> FAIL ---
$u = Build-PurgeResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=0; productResultMissing=$false; installerResult=(ProductResult $false 'UninstallFailed' 'boom') }
$r = Run-Decision $u (AfterState $true $true $true $true)
Assert-True $r.Threw 'B6 product reports failure (success=false) -> FAIL (reinstall skipped)'

# --- B7: success but service remains -> FAIL (case D, real purge defect) ---
$u = Build-PurgeResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=0; productResultMissing=$false; installerResult=(ProductResult $true 'Success' 'uninstalled') }
$r = Run-Decision $u (AfterState $true $false $false $false)
Assert-True $r.Threw 'B7 product success but service remains -> FAIL (case D)'
Assert-True $r.ReinstallSkipped 'B7 service remains -> reinstallSkipped=true'

# --- B8: success but Program Files remains -> FAIL ---
$r = Run-Decision $u (AfterState $false $true $false $false)
Assert-True $r.Threw 'B8 product success but Program Files remains -> FAIL'
Assert-True $r.ReinstallSkipped 'B8 Program Files remains -> reinstallSkipped=true'

# --- B9: success but ProgramData\PathVeer remains -> FAIL ---
$r = Run-Decision $u (AfterState $false $false $true $false)
Assert-True $r.Threw 'B9 product success but ProgramData\PathVeer remains -> FAIL'
Assert-True $r.ReinstallSkipped 'B9 ProgramData\PathVeer remains -> reinstallSkipped=true'

# --- B10: success but Apps&Features remains -> FAIL ---
$r = Run-Decision $u (AfterState $false $false $false $true)
Assert-True $r.Threw 'B10 product success but Apps&Features remains -> FAIL'
Assert-True $r.ReinstallSkipped 'B10 Apps&Features remains -> reinstallSkipped=true'

# --- B11: purge failure -> reinstall NOT invoked (covered by B2..B10 reinstallSkipped checks); explicit ---
$u = Build-PurgeResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=0; productResultMissing=$false; installerResult=$null }
$r = Run-Decision $u (AfterState $true $true $true $true)
Assert-True $r.ReinstallSkipped 'B11 purge failure -> reinstall NOT invoked'

# --- B12: purge success + all postconditions -> reinstall invoked (rebuild success $u) ---
$uSucc = Build-PurgeResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=0; productResultMissing=$false; installerResult=(ProductResult $true 'Success' 'PathVeer uninstalled.') }
$rSucc = Run-Decision $uSucc (AfterState $false $false $false $false)
Assert-True ($rSucc.ReinstallSkipped -eq $false) 'B12 purge success + clean postconditions -> reinstall invoked'

# --- B13: reinstall exact version required: decision does not reinstall, but the shared precondition
#          (Install-PathVeerProtectedCandidate) enforces ExpectedVersion='1.0.0-beta.1'. Verified by contract:
#          the reinstall path calls Install-PathVeerProtectedCandidate -Stage GATE4, which throws unless
#          actualVersion -eq '1.0.0-beta.1'. Assert the decision reaches reinstall only on clean success. ---
Assert-True (-not $rSucc.Threw) 'B13 reinstall path reachable only on concrete clean success'

# --- Adversarial: elevationAvailable alone must NOT be treated as product success (case A with elev=true) ---
$u = [PSCustomObject]@{ completed=$true; elevationAvailable=$true; elevationSucceeded=$true; result=$null }
$r = Run-Decision $u (AfterState $true $true $true $true)
Assert-True $r.Threw 'ADV elevationAvailable=true but no product result -> still FAIL (not misread as success)'

# ===================================================================
# C) GATE-4 vs GATE-8 contract separation (adapter-level)
# ===================================================================
$pA = Build-PurgeArgs 'PurgeUninstall'
$uA = Build-PurgeArgs 'Uninstall'
Assert-True ($pA -contains '-PurgeState' -and $uA -notcontains '-PurgeState') 'SEP PurgeUninstall has -PurgeState; Uninstall does NOT'
Assert-True (($pA[[array]::IndexOf($pA,'-Action')+1]) -eq 'uninstall' -and ($uA[[array]::IndexOf($uA,'-Action')+1]) -eq 'uninstall') 'SEP both map to product uninstall'

# ===================================================================
# Report
# ===================================================================
Write-Host "GATE-4 PURGE TESTS: $($pass.Count) passed, $($fail.Count) failed" -ForegroundColor $(if ($fail.Count -eq 0){'Green'}else{'Red'})
foreach ($f in $fail) { Write-Host "  FAIL: $f" -ForegroundColor Red }
foreach ($p in $pass) { Write-Host "  PASS: $p" -ForegroundColor DarkGray }
if ($fail.Count -gt 0) { exit 1 }
exit 0
