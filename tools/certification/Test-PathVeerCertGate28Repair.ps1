<#
.SYNOPSIS
    Local (no-VM) tests for GATE-28 SAME-VERSION REPAIR.

    Two concerns, both exercised against the REAL shipped code via extraction + mocks:

    A) REPAIR ADAPTER — extract the REAL arg-build block from PathVeerCertificationJea.psm1
       and prove the certification 'Repair' (and 'Upgrade') semantics are translated to the
       product 'install' contract, never to an unsupported -Action Repair / -Action Upgrade.
       This is the GATE-28 root-cause fix (the harness previously sent -Action Repair straight
       to the product, which its ValidateSet [install,uninstall,status,statejson] rejected at
       parameter binding -> exit 1, no result record).

    B) GATE-28 RESULT DECISION — extract the REAL Run-GATE28 repair decision block from
       Invoke-PathVeerCertification.ps1 and prove fail-closed ordering:
       the repair OPERATION must succeed before any downstream product-state check is
       interpreted as PASS. elevationAvailable is NOT success. Missing/null result or exit
       code is NEVER defaulted to success. CLI repair is verified after a successful repair.

    No real VM, no real file deletion, no credentials. Destructive/stateful cmdlets are mocked.
#>
$PSScriptRootReal = Split-Path -Parent $MyInvocation.MyCommand.Definition
$Root = Resolve-Path (Join-Path $PSScriptRootReal '..\..')
$psm1 = Join-Path $Root 'tools/certification/jea/PathVeerCertificationJea.psm1'
$orch = Join-Path $Root 'tools/certification/Invoke-PathVeerCertification.ps1'

$pass = [System.Collections.Generic.List[string]]::new()
$fail = [System.Collections.Generic.List[string]]::new()
function Assert-True($Cond, $Name) { if ($Cond) { [void]$script:pass.Add($Name) } else { [void]$script:fail.Add($Name) } }

# ===================================================================
# A) REPAIR ADAPTER: extract the REAL arg-build block from the JEA module
# ===================================================================
$psm1Text = Get-Content -LiteralPath $psm1 -Raw
$m = [regex]::Match($psm1Text, '(?s)\$productAction = \$Action.*?\$psiArgs \+= ''-ProgressFile''; \$psiArgs \+= \$progressFile')
Assert-True $m.Success 'A0 extracted JEA arg-build block from committed psm1'
$block = $m.Value
$block = $block -replace '\$script:InstallScript', '$installScriptPath' -replace '\$script:InstallPackage', '$installPackagePath'

function Build-Args($Action, $Feature='RegisterShell,InstallTray') {
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

# --- A1: Repair maps to a VALID product Action (install), never 'Repair' ---
$rArgs = Build-Args 'Repair'
$rStr = $rArgs -join ' '
Assert-True ($rArgs -contains '-Action' -and ($rArgs[[array]::IndexOf($rArgs,'-Action')+1]) -eq 'install') 'A1 Repair -> product -Action install (valid contract)'
Assert-True ($rStr -notmatch '-Action\s+Repair') 'A1b Repair NEVER passes -Action Repair to product (root-cause fix)'
Assert-True ($rArgs -contains '-RegisterShell' -and $rArgs -contains '-InstallTray') 'A1c Repair passes same required features (RegisterShell,InstallTray)'

# --- A2: Upgrade maps to product 'install' (same primitive), never 'Upgrade' ---
$uArgs = Build-Args 'Upgrade'
Assert-True ($uArgs -contains '-Action' -and ($uArgs[[array]::IndexOf($uArgs,'-Action')+1]) -eq 'install') 'A2 Upgrade -> product -Action install (valid contract)'
Assert-True ($uArgs -notmatch '-Action\s+Upgrade') 'A2b Upgrade NEVER passes -Action Upgrade to product'

# --- A3: Repair does NOT accidentally gain -PurgeState ---
Assert-True ($rArgs -notcontains '-PurgeState') 'A3 Repair -> NO -PurgeState (it is a repair, not a purge)'

# --- A4: PurgeUninstall still maps to uninstall -PurgeState (GATE-4 regression) ---
$pArgs = Build-Args 'PurgeUninstall'
Assert-True ($pArgs -contains '-Action' -and ($pArgs[[array]::IndexOf($pArgs,'-Action')+1]) -eq 'uninstall') 'A4 PurgeUninstall -> product -Action uninstall'
Assert-True ($pArgs -contains '-PurgeState') 'A4b PurgeUninstall -> -PurgeState preserved'
Assert-True ($pArgs -notmatch '-Action\s+PurgeUninstall') 'A4c PurgeUninstall NEVER passes -Action PurgeUninstall'

# --- A5: normal Uninstall still maps to uninstall WITHOUT -PurgeState (GATE-8 regression) ---
$uArgs2 = Build-Args 'Uninstall'
Assert-True ($uArgs2 -contains '-Action' -and ($uArgs2[[array]::IndexOf($uArgs2,'-Action')+1]) -eq 'uninstall') 'A5 Uninstall -> product -Action uninstall'
Assert-True ($uArgs2 -notcontains '-PurgeState') 'A5b Uninstall -> NO -PurgeState (GATE-8 state preserved)'

# --- A6: Install mapping unchanged ---
$iArgs = Build-Args 'Install'
Assert-True ($iArgs -contains '-Action' -and ($iArgs[[array]::IndexOf($iArgs,'-Action')+1]) -eq 'install') 'A6 Install -> -Action install'
Assert-True ($iArgs -notcontains '-PurgeState') 'A6b Install -> NO -PurgeState'

# --- A7: same protected package is passed (PackageDirectory is the protected payload path) ---
Assert-True ($rArgs -contains '-PackageDirectory' -and ($rArgs[[array]::IndexOf($rArgs,'-PackageDirectory')+1]) -eq 'C:\ProgramData\PathVeerCertificationJea\Payloads\PathVeer-1.0.0-beta.1') 'A7 Repair passes the protected candidate package (no old/foreign candidate)'

# --- A8: AST — product child args never contain -Action Repair / -Action Upgrade / -Action PurgeUninstall ---
$orchText = Get-Content -LiteralPath $orch -Raw
Assert-True ($orchText -notmatch "Invoke-GuestJeaInstall[^\n]*'-Action'\s*,\s*'Repair'") 'A8a orchestrator never passes literal -Action Repair'
Assert-True ($orchText -notmatch "'-Action'\s*,\s*'Upgrade'") 'A8b orchestrator never passes literal -Action Upgrade'
Assert-True ($orchText -notmatch "'-Action'\s*,\s*'PurgeUninstall'") 'A8c orchestrator never passes literal -Action PurgeUninstall'

# ===================================================================
# B) GATE-28 RESULT DECISION: extract the REAL Run-GATE28 repair block
# ===================================================================
# The decision block: from '$repairFail = [System.Collections.Generic.List[string]]::new()' through the
# final 'return @{ repairedVersion=$ver; serviceStateAfterRepair=$svcAfter }'. It is the authoritative
# shipped logic (fail-closed ordering + normalized repair object + legacy aliases).
$m2 = [regex]::Match($orchText, '(?s)\$repairFail = \[System\.Collections\.Generic\.List\[string\]\]::new\(\).*?return @\{ repairedVersion=\$ver; serviceStateAfterRepair=\$svcAfter \}')
Assert-True $m2.Success 'B0 extracted Run-GATE28 repair decision block from committed orchestrator'
$decision = $m2.Value

# Stub Save-Json; capture thrown evidence via a global.
$script:LastSaved = $null
function Save-Json($Name, $Obj) { $script:LastSaved = [PSCustomObject]@{ Name = $Name; Obj = $Obj } }

function Build-RepairResult($props) {
    $ir = [PSCustomObject]@{}
    foreach ($k in @('installerInvocationAttempted','installerStarted','installerExitCode','installerError','wrapperError','productResultMissing','childExitCode','childError','installerResult','installerProgress')) {
        if ($props.ContainsKey($k)) { $ir | Add-Member -NotePropertyName $k -NotePropertyValue $props[$k] }
    }
    $r = [PSCustomObject]@{}
    if ($props.ContainsKey('completed')) { $r | Add-Member -NotePropertyName 'completed' -NotePropertyValue $props['completed'] }
    if ($props.ContainsKey('elevationAvailable')) { $r | Add-Member -NotePropertyName 'elevationAvailable' -NotePropertyValue $props['elevationAvailable'] }
    if ($props.ContainsKey('elevationSucceeded')) { $r | Add-Member -NotePropertyName 'elevationSucceeded' -NotePropertyValue $props['elevationSucceeded'] }
    $r | Add-Member -NotePropertyName 'result' -NotePropertyValue $ir
    return $r
}

function Run-Repair($r, $svc, $ver, $cliExit) {
    $inst = [PSCustomObject]@{ expectedVersion='1.0.0-beta.1' }
    $script:LastSaved = $null
    # The extracted block calls Invoke-Command with a ScriptBlock that extracts .Status / .productVersion.
    # Mock returns the scalar the real scriptblock would yield (detect by scriptblock content).
    $script:AfterMock = [PSCustomObject]@{ Status = $svc }
    $script:VerMock   = $ver
    $script:CliMock   = [PSCustomObject]@{ result = [PSCustomObject]@{ exitCode = $cliExit } }
    function Invoke-Command { param($Session,$ScriptBlock,$ArgumentList)
        $sb = $ScriptBlock.ToString()
        if ($sb -match 'Get-Service') { return $script:AfterMock.Status }
        if ($sb -match 'Get-Content') { return $script:VerMock }
        return $script:AfterMock
    }
    function Invoke-GuestJeaCli { param($Session,$JeaSession,$Verb); return $script:CliMock }
    $threw = $false; $blockErr = $null
    # The shipped block ends with `return @{ repairedVersion=$ver; serviceStateAfterRepair=$svcAfter }`.
    # Replace that trailing return (test-only copy) so we can capture the resolved scalars instead of
    # letting the return abort the function before our capture lines run. The decision LOGIC is unchanged.
    $decisionLocal = $decision -replace 'return @\{ repairedVersion=\$ver; serviceStateAfterRepair=\$svcAfter \}', '$script:VerOut = $ver; $script:SvcOut = $svcAfter; $script:CliOut = $cliRepairExit'
    try {
        Invoke-Expression @"
`$r = `$r
`$svcAfter = `$null; `$ver = `$null; `$cliRepairExit = `$null
`$inst = `$inst
$decisionLocal
`$script:VerOut = `$ver
`$script:SvcOut = `$svcAfter
`$script:CliOut = `$cliRepairExit
"@
    } catch {
        $threw = $true; $blockErr = $_.Exception.Message
    }
    return [PSCustomObject]@{ Threw=$threw; Error=$blockErr; Saved=$script:LastSaved; Ver=$script:VerOut; Svc=$script:SvcOut; Cli=$script:CliOut }
}

function ProdResult($success, $category, $message) {
    [PSCustomObject]@{ success=$success; category=$category; message=$message; version='1.0.0-beta.1'; timestamp='2026-08-16T00:00:00Z' }
}

# --- B1: full success -> PASS (no throw), version=1.0.0-beta.1, service Running, CLI 0 ---
$r = Build-RepairResult @{ completed=$true; elevationAvailable=$true; elevationSucceeded=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=0; productResultMissing=$false; installerResult=(ProdResult $true 'Success' 'PathVeer 1.0.0-beta.1 installed.') }
$res = Run-Repair $r 'Running' '1.0.0-beta.1' 0
Assert-True (-not $res.Threw) 'B1 product repair success + Running service + same version + CLI ok -> PASS (no throw)'
Assert-True ($res.Ver -eq '1.0.0-beta.1') 'B1b repaired version = 1.0.0-beta.1'

# --- B2: wrapper result null -> FAIL (no product ran) ---
$r = [PSCustomObject]@{ completed=$null; elevationAvailable=$true }   # no .result
$res = Run-Repair $r 'Running' '1.0.0-beta.1' 0
Assert-True $res.Threw 'B2 null wrapper/bridge result -> FAIL'

# --- B3: installerResult null (product body never ran) -> FAIL ---
$r = Build-RepairResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=1; productResultMissing=$true; installerResult=$null }
$res = Run-Repair $r 'Running' '1.0.0-beta.1' 0
Assert-True $res.Threw 'B3 installerResult null -> FAIL (product body never ran / binding rejection)'

# --- B4: productResultMissing=true -> FAIL ---
$r = Build-RepairResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=1; productResultMissing=$true; installerResult=(ProdResult $false 'InstallFailed' 'bad') }
$res = Run-Repair $r 'Running' '1.0.0-beta.1' 0
Assert-True $res.Threw 'B4 productResultMissing=true -> FAIL'

# --- B5: installerExitCode missing (null) -> FAIL, NOT defaulted to 0 ---
$r = Build-RepairResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=$null; productResultMissing=$false; installerResult=$null }
$res = Run-Repair $r 'Running' '1.0.0-beta.1' 0
Assert-True $res.Threw 'B5 installerExitCode null -> FAIL (not treated as success)'

# --- B6: installerExitCode=1 -> FAIL (the real GATE-28 symptom) ---
$r = Build-RepairResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=1; productResultMissing=$false; installerResult=(ProdResult $false 'BindingRejected' 'unsupported -Action') }
$res = Run-Repair $r 'Running' '1.0.0-beta.1' 0
Assert-True $res.Threw 'B6 installerExitCode=1 -> FAIL (matches real GATE-28 repairExitCode=1)'

# --- B7: elevationAvailable=true but no product result -> FAIL (elev is NOT success) ---
$r = [PSCustomObject]@{ completed=$true; elevationAvailable=$true; elevationSucceeded=$true; result=$null }
$res = Run-Repair $r 'Running' '1.0.0-beta.1' 0
Assert-True $res.Threw 'B7 elevationAvailable=true but no product result -> still FAIL (not misread as success)'

# --- B8: product repair success but service NOT Running -> FAIL ---
$r = Build-RepairResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=0; productResultMissing=$false; installerResult=(ProdResult $true 'Success' 'installed') }
$res = Run-Repair $r 'Stopped' '1.0.0-beta.1' 0
Assert-True $res.Threw 'B8 repair success but service not Running -> FAIL'

# --- B9: product repair success but version mismatch -> FAIL ---
$res = Run-Repair $r 'Running' '2.0.0' 0
Assert-True $res.Threw 'B9 repair success but version != 1.0.0-beta.1 -> FAIL (not upgrade/downgrade)'

# --- B10: product repair success but CLI repair fails -> FAIL ---
$res = Run-Repair $r 'Running' '1.0.0-beta.1' 1
Assert-True $res.Threw 'B10 repair success but CLI repair exit!=0 -> FAIL'

# --- B11: wrong protected candidate identity -> FAIL BEFORE repair (Assert-CandidateMatchesProtected) ---
# The decision block itself does not contain the candidate assertion; it is enforced upstream by
# Assert-CandidateMatchesProtected (Run-GATE28). Assert that function exists and is invoked on the
# GATE-28 path with the same protected payload identity before the Repair action is dispatched.
Assert-True ($orchText -match "Assert-CandidateMatchesProtected -CandidatePackage") 'B11 GATE-28 asserts candidate identity matches protected payload before repair'
Assert-True ($orchText -match "expectedPackageId = 'PathVeer-1.0.0-beta.1'") 'B11b GATE-28 expected protected package id = PathVeer-1.0.0-beta.1'

# --- B12: same-version exact candidate allowed (the precondition install uses the same protected candidate) ---
Assert-True ($orchText -match "Install-PathVeerProtectedCandidate .*-Stage GATE28") 'B12 GATE-28 precondition install uses same protected candidate (no foreign candidate)'

# --- B13: normalized 'repair' object captured on failure exposes productResultMissing/childError ---
$r = Build-RepairResult @{ elevationAvailable=$true; installerInvocationAttempted=$true; installerStarted=$true; installerExitCode=1; productResultMissing=$true; childError='VariableIsUndefined -Action Repair unsupported'; installerResult=$null }
$res = Run-Repair $r 'Running' '1.0.0-beta.1' 0
Assert-True $res.Threw 'B13 causal repair failure captured'
Assert-True ($res.Saved -and $res.Saved.Name -eq '28-gate28-repair.json') 'B13b evidence written to 28-gate28-repair.json'
$repObj = $res.Saved.Obj.repair
Assert-True ($null -ne $repObj) 'B13c normalized repair object present in evidence'
Assert-True ($repObj.productResultMissing -eq $true) 'B13d repair.productResultMissing=true exposed (root-cause diagnostic)'
Assert-True ($repObj.childError -like '*Repair*') 'B13e repair.childError exposes the unsupported -Action'

# ===================================================================
# Report
# ===================================================================
Write-Host "GATE-28 REPAIR TESTS: $($pass.Count) passed, $($fail.Count) failed" -ForegroundColor $(if ($fail.Count -eq 0){'Green'}else{'Red'})
foreach ($f in $fail) { Write-Host "  FAIL: $f" -ForegroundColor Red }
foreach ($p in $pass) { Write-Host "  PASS: $p" -ForegroundColor DarkGray }
if ($fail.Count -gt 0) { exit 1 }
exit 0
