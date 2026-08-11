<#
.SYNOPSIS
    Exports a PathVeer release-metadata signing key to a PKCS#12 / PFX backup.

.DESCRIPTION
    Reads a DPAPI-protected production (or staging) metadata signing key from the
    secure local store (produced by New-PathVeerMetadataKey.ps1) and exports it as
    a STANDARD PKCS#12 (.pfx) file encrypted with a recovery passphrase the
    operator supplies at runtime. This is the portable recovery copy intended for
    safekeeping in a password manager (e.g. KeePassXC); the DPAPI store alone is
    machine+user bound and is not sufficient recovery on its own.

    The export uses ONLY platform cryptography:
      * the private key is imported into a software ECDsa (P-256) instance,
      * a self-signed certificate is built around the public key
        (CertificateRequest.CreateSelfSigned),
      * the key+cert are exported as PFX via X509Certificate2.Export(Pfx, SecureString)
        — a standard, well-audited format. No custom cryptography is implemented.

    The recovery passphrase is converted to a SecureString and passed directly to
    the PFX export; it is NEVER written to stdout, logs, or any file.

.EXAMPLE
    # Prompts for the recovery passphrase (SecureString, not echoed):
    .\New-PathVeerMetadataKeyBackup.ps1 -KeyId pv-meta-prod-2026-01

.EXAMPLE
    # Explicit output path:
    .\New-PathVeerMetadataKeyBackup.ps1 -KeyId pv-meta-prod-2026-01 `
        -OutputPath C:\SecureBackup\pv-meta-prod-2026-01.pfx
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$KeyId,

    # Directory or file path for the .pfx. Defaults to the secret store next to
    # the DPAPI file. The file is ACL-restricted to the current user.
    [Parameter(Mandatory = $false)]
    [string]$OutputPath = '',

    # Secure store root. Defaults to %LOCALAPPDATA%\PathVeer\Secrets.
    [Parameter(Mandatory = $false)]
    [string]$StoreRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$storeRoot = if ($StoreRoot) { $StoreRoot } else {
    Join-Path $env:LOCALAPPDATA 'PathVeer\Secrets'
}
$keyFile = Join-Path $storeRoot ("metadata-signing-{0}.xml" -f $KeyId)
$metaFile = Join-Path $storeRoot ("metadata-signing-{0}.meta.json" -f $KeyId)

if (-not (Test-Path -LiteralPath $keyFile)) {
    throw "Key store not found for keyId '$KeyId' at $keyFile."
}
if (-not (Test-Path -LiteralPath $metaFile)) {
    throw "Key metadata not found at $metaFile."
}

# --- Load the DPAPI-protected private blob (never printed) -----------------
$ss = Import-Clixml -LiteralPath $keyFile
$blobB64 = [Runtime.InteropServices.Marshal]::PtrToStringUni(
    [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($ss))
[Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode(
    [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($ss)) | Out-Null

$keyBytes = [System.Convert]::FromBase64String($blobB64)
if ($keyBytes.Length -ne 96) { throw "Unexpected private-key blob length: $($keyBytes.Length)." }
$x = New-Object byte[] 32; [Array]::Copy($keyBytes, 0, $x, 0, 32)
$y = New-Object byte[] 32; [Array]::Copy($keyBytes, 32, $y, 0, 32)
$d = New-Object byte[] 32; [Array]::Copy($keyBytes, 64, $d, 0, 32)

$ecp = [System.Security.Cryptography.ECParameters]::new()
$ecp.Curve = [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256
$ecp.Q = [System.Security.Cryptography.ECPoint]::new()
$ecp.Q.X = $x; $ecp.Q.Y = $y; $ecp.D = $d

# --- Read the recovery passphrase (SecureString, never echoed) -------------
# Interactive: prompt. Non-interactive (piped): read one line from stdin.
$pass = $null
if ([System.Console]::IsInputRedirected) {
    $line = [System.Console]::In.ReadLine()
    if ([string]::IsNullOrEmpty($line)) { throw "Empty passphrase from stdin." }
    $pass = ConvertTo-SecureString $line -AsPlainText -Force
} else {
    $pass = Read-Host -AsSecureString -Prompt "Enter recovery passphrase for PFX export"
}
if ($null -eq $pass -or $pass.Length -eq 0) { throw "Empty passphrase refused." }

# --- Build a self-signed cert around the key and export PFX ----------------
$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
$ecdsa.ImportParameters($ecp)
$subject = "CN=PathVeer-Release-Metadata-$KeyId"
$req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
    $subject, $ecdsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
$notBefore = [datetime]::UtcNow
$notAfter = $notBefore.AddYears(100)
$cert = $req.CreateSelfSigned($notBefore, $notAfter)

$pfxBytes = $cert.Export(
    [System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $pass)

# --- Resolve output path ---------------------------------------------------
$resolved = if ($OutputPath) {
    if ((Get-Item -LiteralPath $OutputPath -ErrorAction SilentlyContinue) -is [System.IO.DirectoryInfo] -or
        ($OutputPath.EndsWith('\') -or $OutputPath.EndsWith('/'))) {
        Join-Path $OutputPath ("metadata-signing-{0}.pfx" -f $KeyId)
    } else { $OutputPath }
} else {
    Join-Path $storeRoot ("metadata-signing-{0}.pfx" -f $KeyId)
}
if (-not $resolved.EndsWith('.pfx')) { $resolved = $resolved + '.pfx' }

if ($PSCmdlet.ShouldProcess($resolved, "Write PKCS#12 backup")) {
    [System.IO.File]::WriteAllBytes($resolved, $pfxBytes)
    # ACL: current user only, break inheritance.
    $acl = New-Object System.Security.AccessControl.FileSecurity
    $acl.SetAccessRuleProtection($true, $false)
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        [System.Security.Principal.WindowsIdentity]::GetCurrent().Name,
        'FullControl', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $resolved -AclObject $acl
}

# --- Round-trip verify: re-import and confirm the public key fingerprint ---
$re = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($resolved, $pass)
# Derive the 64-byte Q.X||Q.Y from the cert's public key (framework-agnostic):
# ECPublicKeySpecification raw bytes are 0x04 || X[32] || Y[32] (uncompressed).
$raw = $re.PublicKey.EncodedKeyValue.RawData
if ($raw.Length -ne 65 -or $raw[0] -ne 0x04) { throw "Unexpected EC public-key encoding." }
$rtBuf = New-Object byte[] 64
[Array]::Copy($raw, 1, $rtBuf, 0, 64)
$rtFp = (Get-FileHash -InputStream ([System.IO.MemoryStream]::new($rtBuf)) -Algorithm SHA256).Hash.ToLowerInvariant()

$meta = Get-Content -LiteralPath $metaFile | ConvertFrom-Json
$expectedFp = $meta.fingerprint.ToLowerInvariant()
$ok = $rtFp -eq $expectedFp

"PFX backup written : $resolved"
"Algorithm          : ECDSA P-256 (ES256)"
"KeyId              : $KeyId"
"Round-trip fingerprint match: $($ok.ToString().ToUpper())  ($rtFp)"
if (-not $ok) {
    throw "PFX round-trip fingerprint $rtFp does not match expected $expectedFp. Aborting."
}
"Recovery backup OK. Store this .pfx in KeePassXC; the DPAPI store alone is machine-bound."
