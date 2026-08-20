<#
.SYNOPSIS
    Focused fail-closed test for production metadata DPAPI key-store wiring (Gap B)
    AND metadata signing key-domain isolation.

.DESCRIPTION
    Verifies New-PathVeerRelease.ps1 / Sign-ReleaseManifest.ps1 consume the
    secure DPAPI-backed production key store (metadata-signing-<KeyId>.xml)
    instead of requiring PATHVEER_META_SIGN_KEY, and that development and
    production metadata keys are strictly isolated (no cross-trust-domain
    fallback). Uses DISPOSABLE keys only; the real production/private keys are
    never touched, printed, or referenced.

    A. disposable production key store present -> manifest signs successfully
       via the DPAPI store (no env var needed).
    B. missing store/key -> signing fails closed (no silent fallback to env).
    C. DEVELOPMENT signing (KeyId pv-meta-dev-2026-01) CANNOT consume a
       PRODUCTION key: a store containing ONLY a production-key-named file must
       fail closed (never relabel the prod key as dev, never sign with prod
       material).
    D. PRODUCTION signing (KeyId pv-meta-prod-2026-01) CANNOT consume a
       DEVELOPMENT key: a store containing ONLY a dev-key-named file must fail
       closed (never fall back across trust domains).
    E. the release orchestrator's production default remains pv-meta-prod-2026-01.

    NOTE: the CRYPTO-layer isolation (a production verifier rejecting a
    dev-signed manifest and vice versa) is covered by the C# tests
    DevelopmentMetadataTrustTests / ProductionTrustTests; this script proves the
    SIGNING-KEY-STORE selection layer.

.EXAMPLE
    pwsh -NoProfile -File tools/Test-PathVeerReleaseKeyStore.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$SignScript = Join-Path $RepoRoot 'tools\Sign-ReleaseManifest.ps1'
if (-not (Test-Path -LiteralPath $SignScript)) { Write-Error "missing $SignScript"; exit 1 }

function Make-DisposableKeyXml {
    param([string]$StoreDir, [string]$KeyId)
    $bytes = New-Object byte[] 96
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $b64 = [System.Convert]::ToBase64String($bytes)
    $ss = ConvertTo-SecureString -String $b64 -AsPlainText -Force
    $path = Join-Path $StoreDir ("metadata-signing-{0}.xml" -f $KeyId)
    $ss | Export-Clixml -LiteralPath $path
    return $path
}

function New-TestManifest {
    param([string]$Path)
    $doc = [ordered]@{
        version  = '1.0.0-test'
        channel  = 'beta'
        product  = 'PathVeer'
        installer = [ordered]@{ fileName = 'x.exe'; url = 'https://releases.pathveer.com/x.exe'; sha256 = 'abc'; size = 1 }
    }
    $doc | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Path -Encoding UTF8
}

$temp = Join-Path $env:TEMP ('PathVeer.KeyStoreTest.' + [guid]::NewGuid().ToString('N'))
function Cleanup { if (Test-Path $temp) { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue } }
Cleanup
New-Item -ItemType Directory -Force -Path $temp | Out-Null

# Ensure NO real or stray signing env keys leak into the test (fail-closed proof).
$prevProd = $env:PATHVEER_META_SIGN_KEY
$prevDev  = $env:PATHVEER_DEV_META_SIGN_KEY
$env:PATHVEER_META_SIGN_KEY = $null
$env:PATHVEER_DEV_META_SIGN_KEY = $null

$ok = $true
try {
    # --- A: disposable production key store signs successfully ----------------
    $storeA = Join-Path $temp 'storeA'
    New-Item -ItemType Directory -Force -Path $storeA | Out-Null
    $prodKeyId = 'pv-meta-prod-test-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    Make-DisposableKeyXml -StoreDir $storeA -KeyId $prodKeyId | Out-Null
    $manifest = Join-Path $temp 'm-A.json'
    New-TestManifest -Path $manifest
    & pwsh -NoLogo -NoProfile -File $SignScript -ManifestPath $manifest -KeyId $prodKeyId -ProductionKeyStore $storeA -FailIfUnavailable 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "FAIL A: signing via DPAPI store failed (exit $LASTEXITCODE)."; $ok = $false }
    else { Write-Host "PASS A: manifest signed via disposable DPAPI production key store." -ForegroundColor Green }

    # --- B: missing store/key fails closed -----------------------------------
    $missing = Join-Path $temp 'empty-store'
    New-Item -ItemType Directory -Force -Path $missing | Out-Null
    $manifest = Join-Path $temp 'm-B.json'
    New-TestManifest -Path $manifest
    & pwsh -NoLogo -NoProfile -File $SignScript -ManifestPath $manifest -KeyId 'pv-meta-prod-absent' -ProductionKeyStore $missing -FailIfUnavailable 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Error "FAIL B: missing store did not fail closed (exit 0)."; $ok = $false }
    else { Write-Host "PASS B: missing production key store fails closed." -ForegroundColor Green }

    # --- C: dev KeyId CANNOT consume a PRODUCTION-only store ------------------
    $storeC = Join-Path $temp 'storeC-prodOnly'
    New-Item -ItemType Directory -Force -Path $storeC | Out-Null
    # Store contains ONLY a production-key-named file.
    Make-DisposableKeyXml -StoreDir $storeC -KeyId 'pv-meta-prod-2026-01' | Out-Null
    $manifest = Join-Path $temp 'm-C.json'
    New-TestManifest -Path $manifest
    & pwsh -NoLogo -NoProfile -File $SignScript -ManifestPath $manifest -KeyId 'pv-meta-dev-2026-01' -ProductionKeyStore $storeC -FailIfUnavailable 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Error "FAIL C: development signing consumed a production key (exit 0)."
        $ok = $false
    } else { Write-Host "PASS C: development signing cannot consume production key material." -ForegroundColor Green }

    # --- D: prod KeyId CANNOT consume a DEVELOPMENT-only store ----------------
    $storeD = Join-Path $temp 'storeD-devOnly'
    New-Item -ItemType Directory -Force -Path $storeD | Out-Null
    # Store contains ONLY a dev-key-named file.
    Make-DisposableKeyXml -StoreDir $storeD -KeyId 'pv-meta-dev-2026-01' | Out-Null
    $manifest = Join-Path $temp 'm-D.json'
    New-TestManifest -Path $manifest
    & pwsh -NoLogo -NoProfile -File $SignScript -ManifestPath $manifest -KeyId 'pv-meta-prod-2026-01' -ProductionKeyStore $storeD -FailIfUnavailable 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Error "FAIL D: production signing consumed a development key (exit 0)."
        $ok = $false
    } else { Write-Host "PASS D: production signing cannot consume development key material." -ForegroundColor Green }

    # --- E: release orchestrator production default unchanged -----------------
    $relScript = Join-Path $RepoRoot 'tools\New-PathVeerRelease.ps1'
    $content = Get-Content -Raw -LiteralPath $relScript
    if ($content -notmatch 'pv-meta-prod-2026-01') {
        Write-Error "FAIL E: production keyId pv-meta-prod-2026-01 not referenced by release tool."
        $ok = $false
    } else { Write-Host "PASS E: release production keyId remains pv-meta-prod-2026-01." -ForegroundColor Green }
}
finally {
    $env:PATHVEER_META_SIGN_KEY = $prevProd
    $env:PATHVEER_DEV_META_SIGN_KEY = $prevDev
    Cleanup
}

if (-not $ok) { exit 1 }
Write-Host "ALL KEY-STORE TESTS PASSED." -ForegroundColor Green
exit 0
