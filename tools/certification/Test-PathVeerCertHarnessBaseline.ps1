<#
.SYNOPSIS
    Unit tests for the certification harness baseline preflight (Test-PathVeerCertificationHarnessBaseline).
    Runs LOCALLY (no VM, no product). Drives the module's real protected-path derivation by pointing
    $env:ProgramData at a temp root and laying down the Trusted/Payloads tree exactly as the bootstrap
    would, then asserts the read-only baseline logic reports present/absent/misnamed correctly.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Redirect the module's $env:ProgramData-derived protected paths into an isolated temp root so we can
# stage real installer/payload fixtures without touching the host. The module computes:
#   $script:InstallScript = <ProgramData>\PathVeerCertificationJea\Trusted\Install-PathVeer.ps1
#   $script:InstallPackage= <ProgramData>\PathVeerCertificationJea\Payloads\PathVeer-1.0.0-beta.1
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-baseline-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null
$env:ProgramData = $root   # module reads [System.Environment]::GetFolderPath only for system dir; ProgramData uses $env:ProgramData
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
    $rA = Test-PathVeerCertificationHarnessBaseline
    Assert ($rA.baselineReady -eq $true)        'A: baselineReady=true when installer+payload present'
    Assert ($rA.installerPresent -eq $true)     'A: installerPresent=true'
    Assert ($rA.payloadPresent   -eq $true)     'A: payloadPresent=true'
    Assert ($rA.installerBasenameOk -eq $true)  'A: installerBasenameOk=true'
    Assert ($rA.payloadBasenameOk   -eq $true)  'A: payloadBasenameOk=true'

    # --- Case B: payload ABSENT (the real defect class) ---
    Remove-Item -LiteralPath $payload -Recurse -Force
    $rB = Test-PathVeerCertificationHarnessBaseline
    Assert ($rB.baselineReady -eq $false)       'B: baselineReady=false when payload absent'
    Assert ($rB.payloadPresent -eq $false)      'B: payloadPresent=false'
    Assert ($rB.installerPresent -eq $true)     'B: installer still present (only payload missing)'
    Assert ($rB.note -match 'MISSING')          'B: note flags missing candidate'

    # --- Case C: installer ABSENT (the other missing-candidate class) ---
    New-Item -ItemType Directory -Force -Path $payload | Out-Null
    Remove-Item -LiteralPath $installer -Force
    $rC = Test-PathVeerCertificationHarnessBaseline
    Assert ($rC.baselineReady -eq $false)       'C: baselineReady=false when installer absent'
    Assert ($rC.installerBasenameOk -eq $false) 'C: installerBasenameOk=false'

    # --- Case D: payload present but MISNAMED (identity drift on the expected path) ---
    # The module hard-codes the expected payload basename 'PathVeer-1.0.0-beta.1'. If the operator
    # staged a differently-named directory in Payloads, the expected path is absent -> payloadPresent=false
    # and baselineReady=false. We verify the basename comparison branch directly by staging the wrong
    # name and asserting the protected (expected) path is reported absent.
    New-Item -ItemType File -Force -Path $installer | Out-Null
    Remove-Item -LiteralPath $payload -Recurse -Force -ErrorAction SilentlyContinue
    $wrong = Join-Path $pay 'PathVeer-9.9.9'
    New-Item -ItemType Directory -Force -Path $wrong | Out-Null
    $rD = Test-PathVeerCertificationHarnessBaseline
    Assert ($rD.payloadPresent -eq $false)       'D: expected payload path absent when misnamed dir present'
    Assert ($rD.baselineReady -eq $false)        'D: baselineReady=false on identity drift'

    Write-Host "`nPASS=$pass  FAIL=$fail"
    if ($fail -gt 0) { exit 1 }
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    $env:ProgramData = $null
}
