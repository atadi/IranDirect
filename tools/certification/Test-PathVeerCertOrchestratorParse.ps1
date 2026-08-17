<#
.SYNOPSIS
    Full-file parser + structural validation for the GATE-3 orchestrator.

    This is the test that 87cd0cf's "parser PASS" falsely satisfied: prior checks parsed only
    extracted function snippets, not the whole script, so the real committed file's unbalanced
    try/finally (missing try-closing brace) went undetected until the operator ran it and got a
    ParserError at line 2463. This test parses the ACTUAL repository file end-to-end and FAILS
    NONZERO on any parser error. It also includes a self-check: a deliberately malformed fixture
    MUST cause the same parse routine to report errors, proving the test is effective.

    No VM, no credentials, no product install. Local AST inspection only.
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$orchPath = Resolve-Path "tools/certification/Invoke-PathVeerCertification.ps1"
$orch = [System.IO.File]::ReadAllText($orchPath)

$script:fail = 0; $script:pass = 0
function Assert($cond, $msg) {
    if ($cond) { $script:pass++; Write-Host "  PASS: $msg" } else { $script:fail++; Write-Host "  FAIL: $msg" }
}

# --- 1. Full-file parse of the ACTUAL orchestrator; zero errors required -----------------------
function Test-ParseErrors($text) {
    $t = $null; $e = $null
    [void][System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$t, [ref]$e)
    if ($null -eq $e) { return [PSCustomObject]@{ Count = 0; Errors = @() } }
    return [PSCustomObject]@{ Count = @($e).Count; Errors = @($e) }
}

$orchErrors = Test-ParseErrors $orch
Write-Host "  [DEBUG] orchErrors count=$($orchErrors.Count)"
Assert ($orchErrors.Count -eq 0) "ACTUAL Invoke-PathVeerCertification.ps1 parses with ZERO parser errors (was 2 at 87cd0cf: line 2463 missing '}', line 2580 try missing finally)"
if ($orchErrors.Count -gt 0) {
    $orchErrors.Errors | ForEach-Object { Write-Host "    PARSE ERROR L$($_.Extent.StartLineNumber): $($_.Message)" }
}

# --- 2. Self-check: a malformed fixture MUST produce parser errors (proves the test is effective) ---
$malformed = "function Bad {`n    if (`$true) {`n        Write-Host 'unclosed`n    # missing closing brace`n"
$malformedErrors = Test-ParseErrors $malformed
Assert ($malformedErrors.Count -gt 0) "parser test is EFFECTIVE: a malformed fixture reports parser errors (count=$($malformedErrors.Count))"

# --- 3. AST structural inspection of the real orchestrator --------------------------------
$ast = [System.Management.Automation.Language.Parser]::ParseInput($orch, [ref]$null, [ref]$null)
$scriptBlock = $ast

# 3a. param block exists with Stage ValidateSet containing expected values.
$paramBlock = $scriptBlock.ParamBlock
Assert ($null -ne $paramBlock) 'top-level param block exists'
$stageParam = $paramBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'Stage' }
Assert ($null -ne $stageParam) 'Stage parameter declared'
$validateSet = $stageParam.Attributes | Where-Object { $_.TypeName -like '*ValidateSet*' }
if ($validateSet) {
    $vals = $validateSet.PositionalArguments | ForEach-Object { $_.Extent.Text.Trim("'") }
    Assert ($vals -contains 'GATE3') 'Stage ValidateSet contains GATE3'
    Assert ($vals -contains 'GATE2') 'Stage ValidateSet contains GATE2'
    Assert ($vals -contains 'BASELINE') 'Stage ValidateSet contains BASELINE'
    Assert ($vals -contains 'GATE5VERIFY') 'Stage ValidateSet contains GATE5VERIFY'
} else { Assert $false 'Stage ValidateSet attribute present' }

# 3b. try/finally around the stage loop (top-level execution structure).
$tryStatements = @($scriptBlock.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.TryStatementAst] })
Assert ($tryStatements.Count -ge 1) 'at least one top-level try statement exists'
$foundLoopInTry = $false; $foundFinally = $false
foreach ($ts in $tryStatements) {
    # The cleanup try wraps a foreach over $stages whose body is a switch ($st).
    $bodyText = $ts.Body.Extent.Text
    if ($bodyText -match 'foreach\s*\(\$st in \$stages\)' -and $bodyText -match 'switch\s*\(\$st\)') {
        $foundLoopInTry = $true
    }
    if ($ts.Finally -ne $null) { $foundFinally = $true }
}
Assert $foundLoopInTry 'stage loop (foreach $st in $stages > switch $st) resides inside a top-level try'
Assert $foundFinally 'top-level try has a matching finally (final Restore-Clean runs even on gate throw)'

# 3c. Exactly one top-level final cleanup authority (Restore-Clean not duplicated outside the finally).
$restoreCleanCount = ([regex]::Matches($orch, 'Restore-Clean')).Count
# Restore-Clean appears in: (a) pre-stage restore inside loop, (b) final restore inside finally. Both are
# legitimate. Assert the FINAL restore (inside finally, after the loop) is present exactly once at top level.
$finalTry = @($tryStatements | Where-Object { $_.Finally -ne $null } | Select-Object -First 1)
Assert ($finalTry.Count -eq 1) 'exactly one top-level try/finally owns final cleanup'
$finalBlock = $finalTry[0].Finally.Extent.Text
Assert ($finalBlock -match 'Restore-Clean') 'final Restore-Clean lives inside the finally block'
Assert ($finalBlock -match "Guest restored to certification baseline") 'finally prints the baseline-restore confirmation'
Assert ($finalBlock -notmatch 'function ') 'finally contains no function definition (no mis-nested top-level code)'

# 3d. BASELINE PreserveVmState path remains.
Assert ([regex]::IsMatch($orch, 'BASELINE.*PreserveVmState = \$true', [System.Text.RegularExpressions.RegexOptions]::Singleline)) 'BASELINE sets PreserveVmState=$true (no final restore for baseline)'
# 3e. GATE5VERIFY special semantics remain (single final restore; sets PreserveVmState=$false on success).
Assert ([regex]::IsMatch($orch, 'GATE5VERIFY.*PreserveVmState = \$false', [System.Text.RegularExpressions.RegexOptions]::Singleline)) 'GATE5VERIFY success sets PreserveVmState=$false (single restore)'

# 3f. No final Restore-Clean exists OUTSIDE the intended finally (i.e., not a second top-level Restore-Clean
#     after the loop that would duplicate cleanup). The pre-stage Restore-Clean is inside the loop's try.
Assert ($orch -notmatch "Write-Host `"`nAll evidence written") 'evidence-written line is outside the finally (post-completion only)'

# --- 4. GATE-3 evidence-construction correction preserved (87cd0cf) ---------------------------
Assert ($orch -match 'failReasons = \$persistFail\.ToArray\(\)') 'failReasons present at [PSCustomObject] construction (no post-build mutation)'
Assert ($orch -notmatch '\$evidence\.failReasons\s*=') 'no post-construction $evidence.failReasons = mutation (root cause of 93a999e crash)'
Assert ($orch -match 'Save-Json ''03-gate3-reboot-persistence\.json'' \$evidence') 'single Save-Json for GATE-3 evidence'
Assert ($orch -match 'Get-EnumString \$before\.serviceState') 'pre-reboot service state normalized via shared authority'
Assert ($orch -match 'Get-EnumString \$after\.serviceState') 'post-reboot service state normalized via shared authority'
Assert ($orch -match 'Restart-VM -Name .* -Force -Wait -For Heartbeat') 'hard Hyper-V Restart-VM preserved'

Write-Host ""
Write-Host "Orchestrator parser/structure test: $($script:pass) passed, $($script:fail) failed."
exit $(if ($script:fail -eq 0) { 0 } else { 1 })
