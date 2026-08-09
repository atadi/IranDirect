<#
.SYNOPSIS
    Phase 37.3 — release-manifest signing (separate ECDsa P-256 key from Authenticode).

.DESCRIPTION
    Signs a release-manifest JSON file with ECDsa P-256 (ES256) over its
    canonical payload and appends a signature envelope:

      {
        ...manifest fields...,
        "signature": { "algorithm": "ES256", "keyId": "<id>", "value": "<base64>" }
      }

    Canonicalization matches PathVeer.Core.Update.JsonCanonicalizer exactly:
    the "signature" and "signed" nodes are removed, then object property names
    are recursively sorted lexicographically and the JSON is emitted compact
    (no insignificant whitespace), UTF-8. This is what the C# client verifies.

    Key handling (secret-safe):
      * Production private key is read from $env:PATHVEER_META_SIGN_KEY
        (base64 of a 96-byte blob: Q.X[32] | Q.Y[32] | D[32]). Never committed.
      * If unavailable and -FailIfUnavailable is set, this is a HARD FAIL so an
        unsigned manifest can never masquerade as production-ready.
      * If unavailable and -FailIfUnavailable is NOT set (Development/Unsigned),
        the manifest is left unsigned and a warning is emitted.

.PARAMETER ManifestPath
    Path to the release-manifest.json to sign in place.

.PARAMETER KeyId
    Identifier for the trusted key (must match the client's trusted key set).

.PARAMETER FailIfUnavailable
    Fail if no signing key is available (Release/Signed mode).

.EXAMPLE
    .\tools\Sign-ReleaseManifest.ps1 -ManifestPath artifacts/releases/1.0.0/win-x64/release-manifest.json -KeyId pv-meta-2026 -FailIfUnavailable
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $false)]
    [string]$KeyId = 'pv-meta-2026',

    [Parameter(Mandatory = $false)]
    [switch]$FailIfUnavailable
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ManifestPath)) { throw "Manifest not found: $ManifestPath" }

# --- Resolve signing key ----------------------------------------------------
$rawKeyB64 = $env:PATHVEER_META_SIGN_KEY
if ([string]::IsNullOrWhiteSpace($rawKeyB64)) {
    if ($FailIfUnavailable) {
        throw "Release/Signed was requested but PATHVEER_META_SIGN_KEY is not set (no release-metadata signing key available)."
    }
    Write-Host "  No release-metadata signing key (PATHVEER_META_SIGN_KEY). Manifest left UNSIGNED (developer mode)." -ForegroundColor DarkGray
    exit 0
}

# 96-byte blob: Q.X[32] | Q.Y[32] | D[32]
$keyBytes = [System.Convert]::FromBase64String($rawKeyB64)
if ($keyBytes.Length -ne 96) { throw "PATHVEER_META_SIGN_KEY must be 96 bytes (base64 of X|Y|D)." }
$x = $keyBytes[0..31]; $y = $keyBytes[32..63]; $d = $keyBytes[64..95]

$ecp = [System.Security.Cryptography.ECParameters]::new()
$ecp.Curve = [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256
$ecp.Q = [System.Security.Cryptography.ECPoint]::new()
$ecp.Q.X = $x; $ecp.Q.Y = $y; $ecp.D = $d

$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
$ecdsa.ImportParameters($ecp)

# --- Canonicalize (mirrors JsonCanonicalizer) -------------------------------
function ConvertTo-CanonicalJson {
    param($Node)
    # ConvertFrom-Json yields PSCustomObject, not an IDictionary; handle both.
    if ($Node -is [System.Management.Automation.PSCustomObject] -or
        $Node -is [System.Management.Automation.PSObject] -or
        $Node -is [System.Collections.IDictionary]) {
        $dict = if ($Node -is [System.Collections.IDictionary]) { $Node }
                else { $Node.PSObject.Properties }
        $s = [ordered]@{}
        if ($Node -is [System.Collections.IDictionary]) {
            foreach ($k in ($Node.Keys | Sort-Object)) { $s[$k] = ConvertTo-CanonicalJson $Node[$k] }
        } else {
            foreach ($p in ($dict | Sort-Object Name)) { $s[$p.Name] = ConvertTo-CanonicalJson $p.Value }
        }
        return $s
    }
    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        $a = @()
        foreach ($item in $Node) { $a += ConvertTo-CanonicalJson $item }
        return $a
    }
    return $Node
}

$manifest = Get-Content -Raw $ManifestPath | ConvertFrom-Json
# Remove envelope/status fields that are not part of the signed content.
if ($manifest.PSObject.Properties['signature']) { $manifest.PSObject.Properties.Remove('signature') }
if ($manifest.PSObject.Properties['signed']) { $manifest.PSObject.Properties.Remove('signed') }

$canonicalObj = ConvertTo-CanonicalJson $manifest
$canonicalJson = $canonicalObj | ConvertTo-Json -Compress -Depth 20
$payload = [System.Text.Encoding]::UTF8.GetBytes($canonicalJson)

$signature = $ecdsa.SignData($payload, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
$sigB64 = [System.Convert]::ToBase64String($signature)

# Self-verify before writing (sign -> verify, fail-closed).
$verified = $ecdsa.VerifyData($payload, [System.Convert]::FromBase64String($sigB64), [System.Security.Cryptography.HashAlgorithmName]::SHA256)
if (-not $verified) { throw "Produced manifest signature failed self-verification." }

# --- Re-emit manifest WITH signature envelope ------------------------------
$original = Get-Content -Raw $ManifestPath | ConvertFrom-Json
$original | Add-Member -NotePropertyName 'signature' -NotePropertyValue ([ordered]@{
        algorithm = 'ES256'
        keyId     = $KeyId
        value     = $sigB64
    }) -Force
$original.signed = $true
$original | ConvertTo-Json -Depth 20 | Set-Content -Path $ManifestPath -Encoding UTF8

Write-Host "  Manifest signed (keyId=$KeyId, alg=ES256)." -ForegroundColor Green
