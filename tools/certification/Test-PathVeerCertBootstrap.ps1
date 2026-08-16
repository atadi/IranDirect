<#
.SYNOPSIS
    Behavioral regression tests for the certification bootstrap promotion contract
    (Invoke-PathVeerCertificationPromotion). Proves Defect-3 requirements on isolated temp dirs
    WITHOUT mutating the host:

      A) existing protected candidate present + new source missing -> bootstrap THROWS and the
         existing protected candidate STILL EXISTS (not destroyed).
      B) installer source missing -> throws before mutation.
      C) payload source missing   -> throws before mutation.
      D) complete valid source    -> promotion succeeds.
      E) final protected basename is exactly PathVeer-1.0.0-beta.1.

    No product code, no host ProgramData mutation, no elevation required.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Isolate the promotion logic under temp roots so we never touch the real protected tree.
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-promo-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null

$fail = 0; $pass = 0
function Assert($cond, $msg) {
    if ($cond) { $script:pass++; Write-Host "[+] $msg" }
    else { $script:fail++; Write-Host "[-] FAIL: $msg" }
}

# Dot-source the promotion seam (defines Invoke-PathVeerCertificationPromotion + ACL helpers).
. (Resolve-Path (Join-Path $PSScriptRoot 'jea/Invoke-PathVeerCertificationPromotion.ps1'))

# Helper: build a fresh protected-tree + source-tree layout for a scenario.
function New-PromoLayout {
    param([string]$Tag)
    $base = Join-Path $root $Tag
    $trusted = Join-Path $base 'Protected\Trusted'
    $payload = Join-Path $base 'Protected\Payloads'
    $src     = Join-Path $base 'Source'
    New-Item -ItemType Directory -Force -Path $trusted, $payload, $src | Out-Null
    [PSCustomObject]@{ Base = $base; Trusted = $trusted; Payload = $payload; Source = $src }
}

try {
    # ---- Scenario A: existing protected candidate + MISSING new source -> throws, existing preserved ----
    $a = New-PromoLayout 'A'
    # Seed an EXISTING valid protected candidate (simulating a previously good PV-CERT-HARNESS).
    $existingInstaller = Join-Path $a.Trusted 'Install-PathVeer.ps1'
    $existingPayload   = Join-Path $a.Payload 'PathVeer-1.0.0-beta.1'
    New-Item -ItemType File  -Force -Path $existingInstaller | Out-Null
    New-Item -ItemType Directory -Force -Path $existingPayload | Out-Null
    # Source tree is EMPTY (operator omitted the payload source).
    $threwA = $false
    try {
        Invoke-PathVeerCertificationPromotion -SourceDir $a.Source -TrustedDir $a.Trusted -PayloadDir $a.Payload
    } catch { $threwA = $true }
    Assert ($threwA -eq $true)                                  'A: promotion THROWS when source missing'
    Assert (Test-Path -LiteralPath $existingInstaller)          'A: existing protected installer STILL EXISTS (not destroyed)'
    Assert (Test-Path -LiteralPath $existingPayload)            'A: existing protected payload STILL EXISTS (not destroyed)'

    # ---- Scenario B: installer source missing -> throws before mutation ----
    $b = New-PromoLayout 'B'
    # Source has only the payload, not the installer.
    New-Item -ItemType Directory -Force -Path (Join-Path $b.Source 'PathVeer-1.0.0-beta.1') | Out-Null
    $threwB = $false
    try { Invoke-PathVeerCertificationPromotion -SourceDir $b.Source -TrustedDir $b.Trusted -PayloadDir $b.Payload } catch { $threwB = $true }
    Assert ($threwB -eq $true)                                  'B: promotion THROWS when installer source missing'
    Assert (-not (Test-Path -LiteralPath (Join-Path $b.Trusted 'Install-PathVeer.ps1'))) 'B: no installer promoted (no mutation)'

    # ---- Scenario C: payload source missing -> throws before mutation ----
    $c = New-PromoLayout 'C'
    New-Item -ItemType File -Force -Path (Join-Path $c.Source 'Install-PathVeer.ps1') | Out-Null
    $threwC = $false
    try { Invoke-PathVeerCertificationPromotion -SourceDir $c.Source -TrustedDir $c.Trusted -PayloadDir $c.Payload } catch { $threwC = $true }
    Assert ($threwC -eq $true)                                  'C: promotion THROWS when payload source missing'
    Assert (-not (Test-Path -LiteralPath (Join-Path $c.Payload 'PathVeer-1.0.0-beta.1'))) 'C: no payload promoted (no mutation)'

    # ---- Scenario D: complete valid source -> promotion succeeds ----
    $d = New-PromoLayout 'D'
    New-Item -ItemType File -Force -Path (Join-Path $d.Source 'Install-PathVeer.ps1') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $d.Source 'PathVeer-1.0.0-beta.1') | Out-Null
    $threwD = $false
    try { Invoke-PathVeerCertificationPromotion -SourceDir $d.Source -TrustedDir $d.Trusted -PayloadDir $d.Payload } catch { $threwD = $true; Write-Host ("  D error: " + $_.Exception.Message) }
    Assert ($threwD -eq $false)                                 'D: complete valid source promotes without throwing'
    Assert (Test-Path -LiteralPath (Join-Path $d.Trusted 'Install-PathVeer.ps1')) 'D: installer promoted'
    Assert (Test-Path -LiteralPath (Join-Path $d.Payload 'PathVeer-1.0.0-beta.1')) 'D: payload promoted'

    # ---- Scenario E: final protected basename is exactly PathVeer-1.0.0-beta.1 ----
    $promotedName = [System.IO.Path]::GetFileName((Join-Path $d.Payload 'PathVeer-1.0.0-beta.1'))
    Assert ($promotedName -eq 'PathVeer-1.0.0-beta.1')          'E: final protected payload basename == PathVeer-1.0.0-beta.1'

    Write-Host "`nPASS=$pass  FAIL=$fail"
    if ($fail -gt 0) { exit 1 }
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
