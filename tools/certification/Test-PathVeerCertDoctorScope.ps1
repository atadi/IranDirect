<#
.SYNOPSIS
    Control-flow regression test for the GATE-2 doctor SCOPE decision (HARNESS TEST-VALIDITY DEFECT).

    GATE-2 is a CUSTOM-ROUTE DNS-RESOLUTION lifecycle. The `doctor` command runs a BROAD health sweep
    that also covers country/prefix/reconciliation state a fresh custom-route-only install does NOT
    initialize (desired configuration, prefix configuration, prefix metadata, runtime snapshot,
    prefix update history). Those failures are EXPECTED and OUT OF SCOPE.

    GATE-2 must NOT require global doctor exit 0. It scopes the doctor result: only an IN-SCOPE failure
    (custom-routes / runtime / route-integrity) may fail the gate. This test exercises the REAL scope
    function extracted from Invoke-PathVeerCertification.ps1 (the exact code the orchestrator calls),
    using rendered doctor summaries that match the Detailed renderer produced by PathVeer.Core.Cli
    (DiagnosticReportCliRenderer: failed => "  X <Title>", passed => "  + <Title>").

    No VM, no product install.
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$orch = Resolve-Path "tools/certification/Invoke-PathVeerCertification.ps1"
$orchText = [System.IO.File]::ReadAllText($orch)
if (-not ($orchText -match 'function Test-PathVeerCertificationDoctorScope\b')) {
    Write-Error "Test-PathVeerCertificationDoctorScope not found in orchestrator."
}

# Extract-and-invoke the REAL function source (does not run the dispatcher).
$src = [System.IO.File]::ReadAllText($orch)
$m = [regex]::Match($src, '(?s)function Test-PathVeerCertificationDoctorScope\s*\{.*?\n\}')
if (-not $m.Success) { Write-Error "Could not extract Test-PathVeerCertificationDoctorScope source." }
Invoke-Expression $m.Value

$fail = 0; $pass = 0
function Assert($cond, $msg) {
    if ($cond) { $script:pass++; Write-Host "  PASS: $msg" }
    else { $script:fail++; Write-Host "  FAIL: $msg" }
}

# Real operator doctor summary (53ac31d): four OUT-OF-SCOPE failures, in-scope all pass.
$realSummary = @"
PathVeer Diagnostics

Configuration
-------------
  X Desired configuration
  X Prefix configuration
  X Prefix metadata
  + Prefix update history
  + Custom routes

Runtime
-------
  + Runtime state
  + Runtime operation
  + Route inventory
  X Runtime snapshot

Routing
-------
  + Windows route table
  + Route ownership
  + Managed route consistency

Summary

Warnings: 0
Failed: 4
"@

$r = Test-PathVeerCertificationDoctorScope -DoctorSummary $realSummary
Write-Host "Scenario A: real operator evidence (53ac31d) — doctor exit 2, four out-of-scope failures"
Assert ($r.Verdict -eq 'OUT_OF_SCOPE_ONLY') "verdict is OUT_OF_SCOPE_ONLY (real evidence does NOT fail GATE-2)"
Assert (-not $r.HasInScopeFailure) "no in-scope failure detected (custom-route/runtime/route-integrity all healthy)"
Assert ($r.OutOfScopeFailed.Count -eq 4) "four out-of-scope failures captured: $($r.OutOfScopeFailed -join ', ')"
Assert ($r.InScopeFailed.Count -eq 0) "zero in-scope failures"
Assert ($r.AmbiguousFailed.Count -eq 0) "zero ambiguous (unknown) failures"

Write-Host "Scenario B: healthy full pass (doctor exit 0)"
$healthy = @"
PathVeer Diagnostics

Configuration
-------------
  + Desired configuration
  + Prefix configuration
  + Prefix metadata
  + Prefix update history
  + Custom routes

Runtime
-------
  + Runtime state
  + Runtime operation
  + Route inventory
  + Runtime snapshot

Routing
-------
  + Windows route table
  + Route ownership
  + Managed route consistency

Summary

Warnings: 0
Failed: 0
"@
$r = Test-PathVeerCertificationDoctorScope -DoctorSummary $healthy
Assert ($r.Verdict -eq 'OUT_OF_SCOPE_ONLY') "healthy summary does not fail GATE-2"
Assert (-not $r.HasInScopeFailure) "healthy summary has no in-scope failure"

Write-Host "Scenario C: IN-SCOPE failure — custom-route check itself fails (real contract breach)"
$badCustom = @"
Configuration
-------------
  + Desired configuration
  + Prefix configuration
  + Prefix metadata
  + Prefix update history
  + Custom routes

Runtime
-------
  + Runtime state
  + Runtime operation
  + Route inventory
  + Runtime snapshot

Routing
-------
  X Windows route table
  + Route ownership
  + Managed route consistency

Summary

Warnings: 0
Failed: 1
"@
$r = Test-PathVeerCertificationDoctorScope -DoctorSummary $badCustom
Assert ($r.Verdict -eq 'IN_SCOPE_FAILURE') "a Windows route table failure is IN_SCOPE_FAILURE"
Assert ($r.HasInScopeFailure) "HasInScopeFailure true when an in-scope check fails"
Assert (($r.InScopeFailed -join ',') -eq 'Windows route table') "in-scope failure identified correctly"

Write-Host "Scenario D: IN-SCOPE failure — Runtime state fails"
$badRuntime = @"
Runtime
-------
  X Runtime state
  + Runtime operation

Routing
-------
  + Windows route table

Summary

Failed: 1
"@
$r = Test-PathVeerCertificationDoctorScope -DoctorSummary $badRuntime
Assert ($r.HasInScopeFailure) "Runtime state failure is in-scope and fails GATE-2"

Write-Host "Scenario E: unknown/ambiguous failed check fails closed (never silently passes)"
$unknown = @"
Configuration
-------------
  X Some Future Check

Summary

Failed: 1
"@
$r = Test-PathVeerCertificationDoctorScope -DoctorSummary $unknown
Assert ($r.HasInScopeFailure) "an unrecognized failed check is treated as in-scope (fail closed)"
Assert ($r.AmbiguousFailed.Count -eq 1) "unrecognized failure captured as ambiguous"

Write-Host "Scenario F: custom-route resolution failure alone is in-scope"
$badResolve = @"
Configuration
-------------
  + Custom routes

Routing
-------
  X Route ownership

Summary

Failed: 1
"@
$r = Test-PathVeerCertificationDoctorScope -DoctorSummary $badResolve
Assert ($r.HasInScopeFailure) "Route ownership failure is in-scope"

Write-Host ""
Write-Host "Doctor-scope test: $pass passed, $fail failed."
exit $(if ($fail -eq 0) { 0 } else { 1 })
