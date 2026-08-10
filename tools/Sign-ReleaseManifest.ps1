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

    The canonicalizer parses with System.Text.Json.JsonNode (NOT ConvertFrom-Json)
    so date-like and numeric strings keep their original JSON form — this is the
    critical detail that keeps PS-canonical byte-identical to C#-canonical.

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

.PARAMETER VerifyOnly
    Verify an existing signature (does NOT modify the file). Requires -TrustedKeyBase64.

.PARAMETER TrustedKeyBase64
    "<keyId>:<base64>" — 64-byte public (X|Y) OR 96-byte private (X|Y|D) blob
    used to verify in -VerifyOnly mode.

.PARAMETER SignerCommand
    Optional managed-signer command (KMS/HSM/Key Vault). When set, the canonical
    payload bytes are piped to this command's STDIN and its STDOUT (raw base64
    ES256 signature) is used as the manifest signature. This signs WITHOUT
    exporting the private key into PATHVEER_META_SIGN_KEY. The command must emit
    ONLY the base64 signature (no trailing newline/logging). When empty (default)
    the local raw-key path (PATHVEER_META_SIGN_KEY, 96-byte X|Y|D) is used.

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
    [switch]$FailIfUnavailable,

    [Parameter(Mandatory = $false)]
    [switch]$VerifyOnly,

    [Parameter(Mandatory = $false)]
    [string]$TrustedKeyBase64 = '',

    [Parameter(Mandatory = $false)]
    [string]$SignerCommand = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Canonicalize (byte-identical to PathVeer.Core.Update.JsonCanonicalizer) ---
# Walks System.Text.Json.JsonElement (NOT JsonNode, which auto-converts ISO date
# strings to DateTime) so date-like and numeric strings keep their original JSON
# text — exactly as the C# JsonCanonicalizer does. Recursive property-name sort
# (Ordinal), compact, UTF-8.
function ConvertTo-CanonicalJson {
    param($Element)
    switch ($Element.ValueKind) {
        ([System.Text.Json.JsonValueKind]::Object) {
            $pairs = @()
            $names = @()
            foreach ($p in $Element.EnumerateObject()) { $names += $p.Name }
            foreach ($k in ($names | Sort-Object)) {
                $pairs += ('"{0}":{1}' -f (Escape-JsonString $k), (ConvertTo-CanonicalJson $Element.GetProperty($k)))
            }
            return '{' + ($pairs -join ',') + '}'
        }
        ([System.Text.Json.JsonValueKind]::Array) {
            $items = @()
            foreach ($item in $Element.EnumerateArray()) { $items += ConvertTo-CanonicalJson $item }
            return '[' + ($items -join ',') + ']'
        }
        ([System.Text.Json.JsonValueKind]::String) { return '"' + (Escape-JsonString $Element.GetString()) + '"' }
        ([System.Text.Json.JsonValueKind]::Number) {
            $l = 0; $d = 0.0
            if ($Element.TryGetInt64([ref]$l) -and $Element.TryGetDouble([ref]$d) -and $l -eq $d) { return [string]$l }
            return [string]$d
        }
        ([System.Text.Json.JsonValueKind]::True) { return 'true' }
        ([System.Text.Json.JsonValueKind]::False) { return 'false' }
        ([System.Text.Json.JsonValueKind]::Null) { return 'null' }
        default { return 'null' }
    }
}

function Escape-JsonString {
    param([string]$s)
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) {
        if ($ch -eq [char]'"') { $sb.Append('\"') | Out-Null }
        elseif ($ch -eq [char]'\') { $sb.Append('\\') | Out-Null }
        elseif ([int]$ch -lt 0x20) { $sb.Append(('\u{0:x4}' -f [int]$ch)) | Out-Null }
        else { $sb.Append($ch) | Out-Null }
    }
    return $sb.ToString()
}

if (-not (Test-Path $ManifestPath)) { throw "Manifest not found: $ManifestPath" }

# --- Verify-only mode (publisher signature gate; does NOT modify the file) ----
if ($VerifyOnly) {
    if ([string]::IsNullOrWhiteSpace($TrustedKeyBase64)) {
        throw "VerifyOnly requires -TrustedKeyBase64 '<keyId>:<base64 64-byte public key>'."
    }
    if (-not ($TrustedKeyBase64 -match '^([^:]+):(.+)$')) {
        throw "TrustedKeyBase64 must be '<keyId>:<base64 public key>'."
    }
    $trustKeyId = $Matches[1]
    $trustBytes = [System.Convert]::FromBase64String($Matches[2])
    if ($trustBytes.Length -ne 64 -and $trustBytes.Length -ne 96) {
        throw "TrustedKey must be 64 bytes (public X|Y) or 96 bytes (private X|Y|D)."
    }

    $raw = Get-Content -Raw $ManifestPath
    $jm = [System.Text.Json.Nodes.JsonNode]::Parse($raw)
    if ($null -eq $jm['signature']) { throw "Manifest is not signed (no signature envelope)." }
    $sigKeyId = [string]$jm['signature']['keyId']
    if ($sigKeyId -ne $trustKeyId) { throw "Manifest signed by keyId '$sigKeyId'; trusted key is '$trustKeyId'." }
    $sigValue = [System.Convert]::FromBase64String([string]$jm['signature']['value'])

    # Canonicalize the signed-payload (signature + signed removed) exactly as the signer does.
    # Use JsonElement (not JsonNode) so ISO date strings stay strings — matching C#.
    $jm.AsObject().Remove('signature') | Out-Null
    $jm.AsObject().Remove('signed') | Out-Null
    $cleanDoc = [System.Text.Json.JsonDocument]::Parse($jm.ToString())
    $vPayload = ConvertTo-CanonicalJson $cleanDoc.RootElement
    $vBytes = [System.Text.Encoding]::UTF8.GetBytes($vPayload)

    # Public-only EC key import is unreliable on this SDK/PowerShell surface, so the
    # publisher verify gate accepts the 96-byte private blob (X|Y|D) — available in any
    # signing-capable environment — and imports it as full EC parameters (works, as the
    # signer proves). The C# client verifies the same manifest with the 64-byte public
    # key (see ReleaseSignatureVerifier). Both verify the identical canonical payload.
    if ($trustBytes.Length -eq 96) {
        $tx = New-Object byte[] 32; [Array]::Copy($trustBytes, 0, $tx, 0, 32)
        $ty = New-Object byte[] 32; [Array]::Copy($trustBytes, 32, $ty, 0, 32)
        $td = New-Object byte[] 32; [Array]::Copy($trustBytes, 64, $td, 0, 32)
        $kp = [System.Security.Cryptography.ECParameters]::new()
        $kp.Curve = [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256
        $kp.Q = [System.Security.Cryptography.ECPoint]::new()
        $kp.Q.X = $tx; $kp.Q.Y = $ty; $kp.D = $td
    } else {
        throw "On this platform the PowerShell verify gate requires the 96-byte private blob (X|Y|D). The production client verifies with the 64-byte public key in C# (ReleaseSignatureVerifier)."
    }
    $vEcdsa = [System.Security.Cryptography.ECDsa]::Create()
    $vEcdsa.ImportParameters($kp)

    $ok = $vEcdsa.VerifyData($vBytes, $sigValue, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    if (-not $ok) { throw "Manifest signature is INVALID for trusted key '$trustKeyId'." }
    Write-Host "  Manifest signature VERIFIED (keyId=$trustKeyId, alg=ES256)." -ForegroundColor Green
    exit 0
}

# --- Canonicalize the signed payload (shared by both signer paths) ----------
$raw = Get-Content -Raw $ManifestPath
$jm = [System.Text.Json.Nodes.JsonNode]::Parse($raw)
# Remove envelope/status fields that are not part of the signed content.
$jm.AsObject().Remove('signature') | Out-Null
$jm.AsObject().Remove('signed') | Out-Null

# Canonicalize via JsonElement (not JsonNode) so ISO date strings stay strings — matching C#.
$cleanDoc = [System.Text.Json.JsonDocument]::Parse($jm.ToString())
$canonicalJson = ConvertTo-CanonicalJson $cleanDoc.RootElement
$payload = [System.Text.Encoding]::UTF8.GetBytes($canonicalJson)

# --- Resolve signing path ---------------------------------------------------
if (-not [string]::IsNullOrWhiteSpace($SignerCommand)) {
    # Managed/non-exportable signer (KMS/HSM/Key Vault). Convention:
    #   - The canonical payload is written to a temp file.
    #   - $SignerCommand is invoked with that file path appended as its final
    #     argument; the command reads the file, signs with the managed key, and
    #     prints ONLY the base64 ES256 signature to STDOUT.
    # No private key is placed in PATHVEER_META_SIGN_KEY.
    $payloadFile = Join-Path $env:TEMP ("pv-sign-payload-" + [guid]::NewGuid().ToString("N") + ".bin")
    [System.IO.File]::WriteAllBytes($payloadFile, $payload)

    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo.FileName = 'pwsh'
    $proc.StartInfo.ArgumentList.Add('-NoProfile')
    $proc.StartInfo.ArgumentList.Add('-File')
    $proc.StartInfo.ArgumentList.Add($SignerCommand)
    $proc.StartInfo.ArgumentList.Add($payloadFile)
    $proc.StartInfo.UseShellExecute = $false
    $proc.StartInfo.RedirectStandardOutput = $true
    $proc.StartInfo.RedirectStandardError = $true
    $proc.Start() | Out-Null
    $out = $proc.StandardOutput.ReadToEnd().Trim()
    $errOut = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    if ($proc.ExitCode -ne 0) { throw "Managed signer command failed (exit $($proc.ExitCode)): $errOut" }
    $sigB64 = $out
    if ([string]::IsNullOrWhiteSpace($sigB64)) { throw "Managed signer returned an empty signature." }
    # Verify against the trusted public key (PATHVEER_TRUSTED_META_KEYS), if set.
    $pubEnv = $env:PATHVEER_TRUSTED_META_KEYS
    if (-not [string]::IsNullOrWhiteSpace($pubEnv)) {
        $entry = $pubEnv.Split(';', [StringSplitOptions]::RemoveEmptyEntries)[0]
        if ($entry -match '^([^:]+):(.+)$') {
            $tPub = [System.Convert]::FromBase64String($Matches[2])
            if ($tPub.Length -eq 64) {
                $qp = [System.Security.Cryptography.ECPoint]::new()
                $qp.X = [byte[]]::new(32); [Array]::Copy($tPub, 0, $qp.X, 0, 32)
                $qp.Y = [byte[]]::new(32); [Array]::Copy($tPub, 32, $qp.Y, 0, 32)
                $kp = [System.Security.Cryptography.ECParameters]::new()
                $kp.Curve = [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256
                $kp.Q = $qp
                $vEcdsa = [System.Security.Cryptography.ECDsa]::Create(); $vEcdsa.ImportParameters($kp)
                if (-not $vEcdsa.VerifyData($payload, [System.Convert]::FromBase64String($sigB64), [System.Security.Cryptography.HashAlgorithmName]::SHA256)) {
                    throw "Managed signer signature failed verification against trusted public key."
                }
            }
        }
    }
    Remove-Item -LiteralPath $payloadFile -ErrorAction SilentlyContinue
} else {
    # Local raw-key path (default). Requires PATHVEER_META_SIGN_KEY (96-byte X|Y|D).
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
    $x = New-Object byte[] 32; [Array]::Copy($keyBytes, 0, $x, 0, 32)
    $y = New-Object byte[] 32; [Array]::Copy($keyBytes, 32, $y, 0, 32)
    $d = New-Object byte[] 32; [Array]::Copy($keyBytes, 64, $d, 0, 32)

    $ecp = [System.Security.Cryptography.ECParameters]::new()
    $ecp.Curve = [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256
    $ecp.Q = [System.Security.Cryptography.ECPoint]::new()
    $ecp.Q.X = $x; $ecp.Q.Y = $y; $ecp.D = $d

    $ecdsa = [System.Security.Cryptography.ECDsa]::Create()
    $ecdsa.ImportParameters($ecp)
    $signature = $ecdsa.SignData($payload, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    $sigB64 = [System.Convert]::ToBase64String($signature)

    # Self-verify before writing (sign -> verify, fail-closed).
    $verified = $ecdsa.VerifyData($payload, [System.Convert]::FromBase64String($sigB64), [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    if (-not $verified) { throw "Produced manifest signature failed self-verification." }
}

# --- Re-emit manifest WITH signature envelope ------------------------------
$original = [System.Text.Json.Nodes.JsonNode]::Parse($raw)
$original['signed'] = $true
$original['signature'] = [System.Text.Json.Nodes.JsonNode]::Parse(
    (ConvertTo-Json -Compress -InputObject ([ordered]@{
        algorithm = 'ES256'
        keyId     = $KeyId
        value     = $sigB64
    }))
)
[System.IO.File]::WriteAllText($ManifestPath, $original.ToString(), [System.Text.UTF8Encoding]::new($false))

Write-Host "  Manifest signed (keyId=$KeyId, alg=ES256)." -ForegroundColor Green
