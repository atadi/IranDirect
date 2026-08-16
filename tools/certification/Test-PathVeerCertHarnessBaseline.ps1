<#
.SYNOPSIS
    Unit tests for the certification harness-baseline readiness fields, now exposed by the EXISTING
    privileged function Get-PathVeerCertificationBoundary (no new JEA function; VisibleFunctions
    stays at exactly 12). Runs LOCALLY (no VM, no product). Drives the module's real protected-path
    derivation by pointing $env:ProgramData at a temp root and laying down the Trusted/Payloads tree
    exactly as the bootstrap would, then asserts the read-only baseline logic reports
    present/absent/misnamed correctly.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-baseline-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null
$env:ProgramData = $root
try {
    $module = Resolve-Path (Join-Path $PSScriptRoot 'jea/PathVeerCertificationJea.psm1')
    Import-Module $module -Force -ErrorAction Stop

    $trust = Join-Path $root 'PathVeerCertificationJea\Trusted'
    $pay   = Join-Path $root 'PathVeerCertificationJea\Payloads'
    New-Item -ItemType Directory -Force -Path $trust, $pay | Out-Null

    $fail = 0; $pass = 0
    function Assert($cond, $msg) {
        if ($cond) { $script:pass++; Write-Host "[+] $msg" }
        else { $script:fail++; Write-Host "[-] FAIL: $msg" }
    }

    # --- Case A: protected candidate PRESENT (simulated) ---
    $installer = Join-Path $trust 'Install-PathVeer.ps1'
    $payload   = Join-Path $pay 'PathVeer-1.0.0-beta.1'
    New-Item -ItemType File -Force -Path $installer | Out-Null
    New-Item -ItemType Directory -Force -Path $payload | Out-Null
    $rA = Get-PathVeerCertificationBoundary
    Assert ($rA.baselineReady -eq $true)        'A: baselineReady=true when installer+payload present'
    Assert ($rA.installerPresent -eq $true)     'A: installerPresent=true'
    Assert ($rA.payloadPresent   -eq $true)     'A: payloadPresent=true'
    Assert ($rA.installerBasenameOk -eq $true)  'A: installerBasenameOk=true'
    Assert ($rA.payloadBasenameOk   -eq $true)  'A: payloadBasenameOk=true'
    Assert ($rA.expectedPackageId -eq 'PathVeer-1.0.0-beta.1') 'A: expectedPackageId carried'

    # --- Case B: payload ABSENT (the real defect class) ---
    Remove-Item -LiteralPath $payload -Recurse -Force
    $rB = Get-PathVeerCertificationBoundary
    Assert ($rB.baselineReady -eq $false)       'B: baselineReady=false when payload absent'
    Assert ($rB.payloadPresent -eq $false)      'B: payloadPresent=false'
    Assert ($rB.installerPresent -eq $true)     'B: installer still present (only payload missing)'

    # --- Case C: installer ABSENT (the other missing-candidate class) ---
    New-Item -ItemType Directory -Force -Path $payload | Out-Null
    Remove-Item -LiteralPath $installer -Force
    $rC = Get-PathVeerCertificationBoundary
    Assert ($rC.baselineReady -eq $false)       'C: baselineReady=false when installer absent'
    Assert ($rC.installerBasenameOk -eq $false) 'C: installerBasenameOk=false'

    # --- Case D: payload present but MISNAMED (identity drift on the expected path) ---
    New-Item -ItemType File -Force -Path $installer | Out-Null
    Remove-Item -LiteralPath $payload -Recurse -Force -ErrorAction SilentlyContinue
    $wrong = Join-Path $pay 'PathVeer-9.9.9'
    New-Item -ItemType Directory -Force -Path $wrong | Out-Null
    $rD = Get-PathVeerCertificationBoundary
    Assert ($rD.payloadPresent -eq $false)       'D: expected payload path absent when misnamed dir present'
    Assert ($rD.baselineReady -eq $false)        'D: baselineReady=false on identity drift'

    Write-Host "`nPASS=$pass  FAIL=$fail"
    if ($fail -gt 0) { exit 1 }
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    $env:ProgramData = $null
}
