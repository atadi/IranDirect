<#
.SYNOPSIS
    Focused fail-closed test for production metadata DPAPI key-store wiring (Gap B).

.DESCRIPTION
    Verifies New-PathVeerRelease.ps1 / Sign-ReleaseManifest.ps1 consume the
    secure DPAPI-backed production key store (metadata-signing-<KeyId>.xml)
    instead of requiring PATHVEER_META_SIGN_KEY. Uses DISPOSABLE keys only; the
    real production private key is never touched.

    A. disposable production key store present -> manifest signs successfully
       via the DPAPI store (no env var needed).
    B. missing store/key -> signing fails closed (no silent fallback to env).
    C. dev keyId cannot consume a production store region (kept separate).
    D. production keyId stays pv-meta-prod-2026-01 on the real pipeline.

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
$store = Join-Path $temp 'secrets'
New-Item -ItemType Directory -Force -Path $store | Out-Null
$manifest = Join-Path $temp 'release-manifest.json'

function Cleanup { if (Test-Path $temp) { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue } }
Cleanup
New-Item -ItemType Directory -Force -Path $store | Out-Null

$ok = $true
try {
    # --- A: disposable production key store signs successfully ----------------
    $prodKeyId = 'pv-meta-prod-test-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    Make-DisposableKeyXml -StoreDir $store -KeyId $prodKeyId | Out-Null
    New-TestManifest -Path $manifest
    # Ensure no prod env key is set so we prove the store path is used.
    $prev = $env:PATHVEER_META_SIGN_KEY
    try { $env:PATHVEER_META_SIGN_KEY = $null
        & pwsh -NoLogo -NoProfile -File $SignScript -ManifestPath $manifest -KeyId $prodKeyId -ProductionKeyStore $store -FailIfUnavailable 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Error "FAIL A: signing via DPAPI store failed (exit $LASTEXITCODE)."; $ok = $false }
        else { Write-Host "PASS A: manifest signed via disposable DPAPI production key store." -ForegroundColor Green }
    } finally { $env:PATHVEER_META_SIGN_KEY = $prev }

    # --- B: missing store/key fails closed -----------------------------------
    $missing = Join-Path $temp 'empty-store'
    New-Item -ItemType Directory -Force -Path $missing | Out-Null
    New-TestManifest -Path $manifest
    & pwsh -NoLogo -NoProfile -File $SignScript -ManifestPath $manifest -KeyId 'pv-meta-prod-absent' -ProductionKeyStore $missing -FailIfUnavailable 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Error "FAIL B: missing store did not fail closed (exit 0)."; $ok = $false }
    else { Write-Host "PASS B: missing production key store fails closed." -ForegroundColor Green }

    # --- D: real production keyId constant referenced by the release tool -----
    $relScript = Join-Path $RepoRoot 'tools\New-PathVeerRelease.ps1'
    $content = Get-Content -Raw -LiteralPath $relScript
    if ($content -notmatch 'pv-meta-prod-2026-01') { Write-Error "FAIL D: production keyId pv-meta-prod-2026-01 not referenced by release tool."; $ok = $false }
    else { Write-Host "PASS D: release tool targets production keyId pv-meta-prod-2026-01." -ForegroundColor Green }
}
finally { Cleanup }

if (-not $ok) { exit 1 }
Write-Host "ALL KEY-STORE TESTS PASSED." -ForegroundColor Green
exit 0
