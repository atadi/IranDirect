<#
.SYNOPSIS
    Provisions a PathVeer release-metadata signing key (ECDSA P-256 / ES256).

.DESCRIPTION
    Generates an ES256 (ECDSA, NIST P-256 / secp256r1) key pair using platform
    cryptography (System.Security.Cryptography.ECDsa) — no hand-rolled elliptic
    curve maths, no online generator, nothing leaves this machine.

    The PRIVATE key is written only to the machine-local protected secret store,
    encrypted with Windows DPAPI bound to the current user. Critically, the
    private blob is stored as a SecureString: Export-Clixml encrypts SecureString
    members with DPAPI, but leaves ordinary [string] members as PLAINTEXT. Storing
    the blob as a plain string would produce a file that merely looks protected.

    The PUBLIC key is not secret. It is emitted to stdout (and optionally to a
    file) as base64 of the 64-byte Q.X||Q.Y blob that
    PathVeer.Core.Update.ReleaseSignatureVerifier consumes, together with a
    SHA-256 fingerprint of that canonical representation so operators can
    independently identify the trust root.

    The private key is NEVER printed, never returned, never placed in an
    environment variable and never written unencrypted to disk.

.PARAMETER KeyId
    Stable, non-secret key identifier, e.g. pv-meta-prod-2026-01.

.PARAMETER StoreRoot
    Directory for the protected key store. Defaults to
    %LOCALAPPDATA%\PathVeer\Secrets.

.PARAMETER PublicKeyOutPath
    Optional path to write the public trust material (JSON). Safe to commit.

.PARAMETER Force
    Overwrite an existing key file for this KeyId. Refused by default, because
    silently replacing a signing key destroys the ability to verify anything
    already signed with it.

.EXAMPLE
    .\tools\New-PathVeerMetadataKey.ps1 -KeyId pv-meta-prod-2026-01
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$KeyId,

    [Parameter(Mandatory = $false)][string]$StoreRoot = '',
    [Parameter(Mandatory = $false)][string]$PublicKeyOutPath = '',
    [Parameter(Mandatory = $false)][switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw "DPAPI protection is Windows-only. Provision the release key on the Windows release workstation."
}

if ([string]::IsNullOrWhiteSpace($StoreRoot)) {
    $StoreRoot = Join-Path $env:LOCALAPPDATA 'PathVeer\Secrets'
}
if (-not (Test-Path $StoreRoot)) {
    New-Item -ItemType Directory -Path $StoreRoot -Force | Out-Null
}

$keyPath = Join-Path $StoreRoot ("metadata-signing-{0}.xml" -f $KeyId)
if ((Test-Path $keyPath) -and -not $Force) {
    throw "A key for '$KeyId' already exists at $keyPath. Refusing to overwrite: anything already signed with it would become unverifiable. Use -Force only if you are certain, or choose a new KeyId (rotation)."
}

# --- Generate with platform cryptography ------------------------------------
$ecdsa = [System.Security.Cryptography.ECDsa]::Create(
    [System.Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
try {
    $p = $ecdsa.ExportParameters($true)

    # Canonical serializations used across PathVeer:
    #   public  : Q.X[32] || Q.Y[32]          (64 bytes) — verifier trust set
    #   private : Q.X[32] || Q.Y[32] || D[32] (96 bytes) — signer input
    $pub = New-Object byte[] 64
    [Array]::Copy($p.Q.X, 0, $pub, 0, 32)
    [Array]::Copy($p.Q.Y, 0, $pub, 32, 32)

    $priv = New-Object byte[] 96
    [Array]::Copy($p.Q.X, 0, $priv, 0, 32)
    [Array]::Copy($p.Q.Y, 0, $priv, 32, 32)
    [Array]::Copy($p.D,   0, $priv, 64, 32)

    $pubB64  = [Convert]::ToBase64String($pub)
    $privB64 = [Convert]::ToBase64String($priv)

    # Fingerprint over the canonical 64-byte public representation.
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $fingerprint = ([BitConverter]::ToString($sha.ComputeHash($pub)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }

    # --- Store the private key, DPAPI-protected -----------------------------
    # Export-Clixml encrypts a top-level SecureString (or one inside a PSCredential)
    # with DPAPI, writing a protected <SS /> node. The SAME SecureString as a plain
    # property of a PSCustomObject is serialized as its plaintext String — which is
    # exactly what we must avoid. So the private blob lives in its OWN file as a bare
    # DPAPI-protected SecureString, and the non-secret metadata goes in a sidecar
    # JSON (safe to commit / read).
    $secure = ConvertTo-SecureString -String $privB64 -AsPlainText -Force
    $keyPath = Join-Path $StoreRoot ("metadata-signing-{0}.xml" -f $KeyId)   # DPAPI <SS/> blob
    $metaPath = Join-Path $StoreRoot ("metadata-signing-{0}.meta.json" -f $KeyId)  # public, safe

    $secure | Export-Clixml -LiteralPath $keyPath

    # Verify at rest: real DPAPI <SS> node present, private blob NOT in clear text.
    $onDisk = Get-Content -Raw -LiteralPath $keyPath
    if ($onDisk -notmatch '<SS') {
        Remove-Item -LiteralPath $keyPath -Force
        throw "Refusing to keep key file: DPAPI SecureString node absent (private key would be at rest in plaintext)."
    }
    if ($onDisk.Contains($privB64)) {
        Remove-Item -LiteralPath $keyPath -Force
        throw "Refusing to keep key file: private key found in PLAINTEXT on disk."
    }

    # Restrict ACL to the current user only.
    try {
        $acl = Get-Acl -LiteralPath $keyPath
        $acl.SetAccessRuleProtection($true, $false)   # break inheritance
        foreach ($r in @($acl.Access)) { $acl.RemoveAccessRule($r) | Out-Null }
        $me = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
            $me, 'FullControl', 'None', 'None', 'Allow')))
        Set-Acl -LiteralPath $keyPath -AclObject $acl
        $aclState = "restricted to $me"
    } catch {
        $aclState = "WARNING: could not tighten ACL ($($_.Exception.Message))"
    }

    # Public trust material (safe to commit).
    $public = [ordered]@{
        keyId       = $KeyId
        algorithm   = 'ES256'
        curve       = 'nistP256'
        publicKey   = $pubB64
        fingerprint = $fingerprint
        createdUtc  = (Get-Date).ToUniversalTime().ToString('o')
    }
    if (-not [string]::IsNullOrWhiteSpace($PublicKeyOutPath)) {
        $dir = Split-Path -Parent $PublicKeyOutPath
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        [System.IO.File]::WriteAllText(
            $PublicKeyOutPath,
            ($public | ConvertTo-Json -Depth 4),
            [System.Text.UTF8Encoding]::new($false))
    }

    # Sidecar metadata next to the key (no secret material).
    [System.IO.File]::WriteAllText(
        $metaPath,
        ($public | ConvertTo-Json -Depth 4),
        [System.Text.UTF8Encoding]::new($false))

    Write-Host ""
    Write-Host "Release-metadata signing key provisioned." -ForegroundColor Green
    Write-Host "  KeyId        : $KeyId"
    Write-Host "  Algorithm    : ES256 (ECDSA nistP256, SHA-256)"
    Write-Host "  Private key  : DPAPI (CurrentUser) at $keyPath"
    Write-Host "  ACL          : $aclState"
    Write-Host "  Public key   : $pubB64"
    Write-Host "  Fingerprint  : $fingerprint"
    Write-Host "  Metadata     : $metaPath"
    if (-not [string]::IsNullOrWhiteSpace($PublicKeyOutPath)) {
        Write-Host "  Public trust : $PublicKeyOutPath"
    }
    Write-Host ""
    Write-Host "  The private key was never printed and exists only in the protected store." -ForegroundColor DarkGray
    Write-Host "  RECOVERY: DPAPI is bound to this user AND this machine. If the profile or" -ForegroundColor Yellow
    Write-Host "  machine is lost, this key is unrecoverable. Arrange a protected backup." -ForegroundColor Yellow

    # Emit the public record so callers can pipe it; private material excluded.
    [pscustomobject]$public
}
finally {
    $ecdsa.Dispose()
    $privB64 = $null
    $priv = $null
}
