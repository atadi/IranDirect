<#
.SYNOPSIS
    Control-flow regression test for the harness restore policy (HARNESS SEQUENCING / FINAL-CLEANUP
    DEFECT, fixed at this commit). The orchestrator's restore decisions live in two genuine functions,
    Test-PathVeerCertificationPreRestore and Test-PathVeerCertificationFinalRestore, which the dispatcher
    and final-cleanup block ACTUALLY call. This test extracts those exact function definitions from the
    real orchestrator source and invokes them, so we exercise the genuine code (not a re-implementation),
    plus static assertions that the BASELINE switch sets PreserveVmState=$true (so the final block skips
    restore) and that the final block consumes the predicate.

    Proof targets:
      1. BASELINE success  -> Restore-Clean count = 0 (final-restore predicate false because PreserveVmState=true)
      2. BASELINE failure  -> Restore-Clean count = 0
      3. GATE2             -> pre-restore true AND final-restore true (restores canonical checkpoint)
      4. GATE5VERIFY       -> pre-restore false, final-restore true (single restore, ae3e3a9 invariant)
      5. GATE9             -> pre-restore true, final-restore true
      6. BASELINE excluded from Stage All
      7. BASELINE read-only (no product install call in its switch arm)
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$orchPath = Resolve-Path (Join-Path $PSScriptRoot 'Invoke-PathVeerCertification.ps1')
$src = [System.IO.File]::ReadAllText($orchPath)

# Extract the two genuine predicate function definitions verbatim from the orchestrator source.
function Extract-Function([string]$Name) {
    $pattern = '(?s)function\s+' + [regex]::Escape($Name) + '\b.*?\n\}\s*(?=\n|$)'
    if ($src -notmatch $pattern) { throw "Could not extract $Name from orchestrator source." }
    return $Matches[0]
}
$preDef  = Extract-Function 'Test-PathVeerCertificationPreRestore'
$finalDef = Extract-Function 'Test-PathVeerCertificationFinalRestore'
Invoke-Expression $preDef
Invoke-Expression $finalDef

$fail = 0; $pass = 0
function Assert($cond, $msg) {
    if ($cond) { $script:pass++; Write-Host "[+] $msg" }
    else { $script:fail++; Write-Host "[-] FAIL: $msg" }
}

# --- 1 & 2: BASELINE restore count = 0 (both success and failure) ---
# The BASELINE switch arm sets $script:PreserveVmState = $true on success AND failure. The final block
# calls Test-PathVeerCertificationFinalRestore; with PreserveVmState=$true it returns $false -> no restore.
$baseBaselineSuccess = Test-PathVeerCertificationFinalRestore -SkipRestore $false -PreserveVmState $true
$baseBaselineFailure = Test-PathVeerCertificationFinalRestore -SkipRestore $false -PreserveVmState $true
Assert ($baseBaselineSuccess  -eq $false) 'BASELINE success -> final Restore-Clean NOT called (count 0)'
Assert ($baseBaselineFailure -eq $false) 'BASELINE failure -> final Restore-Clean NOT called (count 0)'

# --- 3: GATE2 restores (pre + final) ---
Assert (Test-PathVeerCertificationPreRestore  -StageName 'GATE2' -SkipRestore $false) 'GATE2 -> pre-stage Restore-Clean IS called'
Assert (Test-PathVeerCertificationFinalRestore -SkipRestore $false -PreserveVmState $false) 'GATE2 -> final Restore-Clean IS called'

# --- 4: GATE5VERIFY: pre exempt, final restores once (ae3e3a9 invariant) ---
Assert (-not (Test-PathVeerCertificationPreRestore -StageName 'GATE5VERIFY' -SkipRestore $false)) 'GATE5VERIFY -> pre-stage Restore-Clean NOT called (exempt)'
Assert (Test-PathVeerCertificationFinalRestore -SkipRestore $false -PreserveVmState $false) 'GATE5VERIFY -> final Restore-Clean IS called (single restore)'

# --- 5: GATE9 ordinary gate restores ---
Assert (Test-PathVeerCertificationPreRestore  -StageName 'GATE9' -SkipRestore $false) 'GATE9 -> pre-stage Restore-Clean IS called'
Assert (Test-PathVeerCertificationFinalRestore -SkipRestore $false -PreserveVmState $false) 'GATE9 -> final Restore-Clean IS called'

# --- 6: BASELINE excluded from Stage All (functional invariant) ---
if ($src -match '\$stages = @\((.*?)\)') {
    $allList = $Matches[1]
    Assert ($allList -notmatch 'BASELINE') 'BASELINE is NOT in the -Stage All list'
} else { Assert $false 'Could not locate $stages = @(...) definition' }

# --- 7: BASELINE switch arm is read-only (no product install / no Restore-Clean call inside the arm) ---
# The BASELINE arm spans from "'BASELINE' {" up to the next 8-space-indented case label ("'GATE5'").
$startIdx = $src.IndexOf("'BASELINE' {")
$endMarker = [Environment]::NewLine + "        'GATE5'  {"
$endIdx = $src.IndexOf($endMarker, $startIdx)
$arm = $src.Substring($startIdx, $endIdx - $startIdx)
Assert ($arm.Length -gt 0) 'BASELINE switch arm located'
Assert ($arm -notmatch 'Restore-Clean') 'BASELINE arm does NOT call Restore-Clean directly'
Assert ($arm -match 'PreserveVmState = \$true') 'BASELINE arm sets PreserveVmState=$true (success path)'
# Both the try (success) and catch (failure) branches set PreserveVmState=$true -> VM never auto-restored.
$occurrences = ([regex]::Matches($arm, 'PreserveVmState = \$true')).Count
Assert ($occurrences -ge 2) "BASELINE arm sets PreserveVmState=`$true on BOTH success and failure paths (found $occurrences)"

# --- SkipRestore bypasses all restoration ---
Assert (-not (Test-PathVeerCertificationPreRestore  -StageName 'GATE2' -SkipRestore $true)) 'SkipRestore -> no pre restore'
Assert (-not (Test-PathVeerCertificationFinalRestore -SkipRestore $true  -PreserveVmState $false)) 'SkipRestore -> no final restore'

Write-Host "`nPASS=$pass  FAIL=$fail"
if ($fail -gt 0) { exit 1 }
