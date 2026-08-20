<#
.SYNOPSIS
    Phase 37.1 — Authenticode signing for PathVeer release artifacts.

.DESCRIPTION
    Signs every signable payload under a release directory with Authenticode
    (SHA-256 + RFC3161 timestamp). Secret-safe by design:

      * No certificate, PFX, key or password is ever written to the repo.
      * Production signing reads credentials from environment variables / a
        secret store injected by CI. Local dev builds pass a test certificate
        or run unsigned.
      * If no usable signing material is present and -FailIfUnavailable is NOT
        set, the script reports UNSIGNED and exits 0 (developer mode).
      * If -FailIfUnavailable IS set (Release/Signed), missing credentials are
        a hard failure so an unsignable artifact can never masquerade as
        production-ready.

    Supported credential sources (in priority order):
      1. Azure Sign Tool (AZURE_* env vars) — recommended CI path; no file on disk.
      2. Local PFX via env var PATHVEER_SIGN_PFX + PATHVEER_SIGN_PASSWORD
         (developer / CI secret mount; never committed).
      3. Existing installed certificate by SHA1 thumbprint
         (PATHVEER_SIGN_THUMBPRINT) for a code-signing cert in the user store.

.PARAMETER ReleaseRoot
    Release directory produced by New-PathVeerRelease.ps1.

.PARAMETER FailIfUnavailable
    Fail (exit 1) when no signing material is available.

.EXAMPLE
    .\tools\Sign-PathVeerArtifacts.ps1 -ReleaseRoot artifacts/releases/1.0.0/win-x64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseRoot,

    [Parameter(Mandatory = $false)]
    [switch]$FailIfUnavailable
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ReleaseRoot)) {
    throw "ReleaseRoot not found: $ReleaseRoot"
}

$FilesToSign = @(
    'PathVeerSetup-*.exe',
    'PathVeer-*/Service/PathVeer.Service.exe',
    'PathVeer-*/Cli/PathVeer.Cli.exe',
    'PathVeer-*/Tray/PathVeer.Tray.exe'
)

$TimestampUrl = 'http://timestamp.digicert.com'

function Write-Step([string]$m) { Write-Host $m -ForegroundColor Cyan }

# --- Resolve signing backend -------------------------------------------------
$AzureConfigured = ($env:AZURE_KEY_VAULT_URI -and $env:AZURE_CLIENT_ID -and
                    $env:AZURE_TENANT_ID -and $env:AZURE_CLIENT_SECRET)
$PfxConfigured  = ($env:PATHVEER_SIGN_PFX -and $env:PATHVEER_SIGN_PASSWORD)
$ThumbConfigured = ($env:PATHVEER_SIGN_THUMBPRINT)
$DevThumbConfigured = ($env:PATHVEER_DEV_CODESIGN_THUMBPRINT)

# A development (self-signed) code-signing certificate may NEVER be combined with
# a production signing source. Mixing them would let a dev-signed artifact be
# presented through a production credential path.
if ($DevThumbConfigured -and ($AzureConfigured -or $PfxConfigured -or $ThumbConfigured)) {
    throw "Development signing (PATHVEER_DEV_CODESIGN_THUMBPRINT) must not be combined with a production signing source (AZURE_*, PATHVEER_SIGN_PFX, PATHVEER_SIGN_THUMBPRINT). Use a dev certificate OR a production certificate, not both."
}

$Backend = $null
if ($AzureConfigured)      { $Backend = 'AzureSignTool' }
elseif ($PfxConfigured)    { $Backend = 'Pfx' }
elseif ($ThumbConfigured)  { $Backend = 'Thumbprint' }
elseif ($DevThumbConfigured) { $Backend = 'DevThumbprint' }

if ($Backend -eq $null) {
    if ($FailIfUnavailable) {
        throw "Release/Signed was requested but no signing credentials are available (set AZURE_* or PATHVEER_SIGN_* environment variables)."
    }
    Write-Step "No signing credentials detected. Artifacts remain UNSIGNED (developer mode)."
    Write-Host "  Set env AZURE_KEY_VAULT_URI / AZURE_CLIENT_ID / AZURE_TENANT_ID / AZURE_CLIENT_SECRET" -ForegroundColor DarkGray
    Write-Host "  or PATHVEER_SIGN_PFX / PATHVEER_SIGN_PASSWORD for production signing." -ForegroundColor DarkGray
    exit 0
}

Write-Step "Signing backend: $Backend"

# --- Resolve signtool deterministically (prefer x64 SDK on win-x64) ---------
$signtool = $null
try {
    $signtool = & "$PSScriptRoot/Find-PathVeerSignTool.ps1"
} catch {
    # Missing signtool is a hard error only when signing was actually requested.
    if ($FailIfUnavailable) { throw "signtool.exe not available: $_" }
    Write-Step "signtool.exe not found. Artifacts remain UNSIGNED (developer mode)."
    exit 0
}

# --- Collect files -----------------------------------------------------------
$targets = New-Object System.Collections.Generic.List[string]
foreach ($pattern in $FilesToSign) {
    $resolved = Join-Path $ReleaseRoot $pattern
    foreach ($f in Resolve-Path -Path $resolved -ErrorAction SilentlyContinue) {
        if (-not $targets.Contains($f.Path)) { $targets.Add($f.Path) }
    }
}

if ($targets.Count -eq 0) {
    throw "No signable files matched under $ReleaseRoot."
}

Write-Host "  $($targets.Count) file(s) to sign."

# --- Sign each file ----------------------------------------------------------
foreach ($file in $targets) {
    Write-Host "  signing: $file" -ForegroundColor DarkGray

    switch ($Backend) {
        'AzureSignTool' {
            if (-not (Get-Command azuresigntool -ErrorAction SilentlyContinue)) {
                throw "AzureSignTool not installed. Install via 'dotnet tool install -g AzureSignTool'."
            }
            $argsList = @(
                'sign',
                '--azure-key-vault-uri', $env:AZURE_KEY_VAULT_URI,
                '--azure-key-vault-client-id', $env:AZURE_CLIENT_ID,
                '--azure-key-vault-client-secret', $env:AZURE_CLIENT_SECRET,
                '--azure-key-vault-tenant-id', $env:AZURE_TENANT_ID,
                '--azure-key-vault-certificate', ($env:AZURE_KEY_VAULT_CERTIFICATE ?? 'PathVeer'),
                '--file-digest', 'sha256',
                '--timestamp-rfc3161', $TimestampUrl,
                '--timestamp-digest', 'sha256',
                $file
            )
            & azuresigntool @argsList
            if ($LASTEXITCODE -ne 0) { throw "AzureSignTool failed for $file." }
        }
        'Pfx' {
            if (-not (Test-Path $env:PATHVEER_SIGN_PFX)) {
                throw "PATHVEER_SIGN_PFX points to a missing file: $($env:PATHVEER_SIGN_PFX)"
            }
            & $signtool sign /fd sha256 /tr $TimestampUrl /td sha256 `
                /f $env:PATHVEER_SIGN_PFX /p $env:PATHVEER_SIGN_PASSWORD `
                $file
            if ($LASTEXITCODE -ne 0) { throw "signtool failed for $file." }
        }
        'Thumbprint' {
            & $signtool sign /fd sha256 /tr $TimestampUrl /td sha256 `
                /sha1 $env:PATHVEER_SIGN_THUMBPRINT $file
            if ($LASTEXITCODE -ne 0) { throw "signtool failed for $file." }
        }
        'DevThumbprint' {
            # Self-signed development certificate. NEVER timestamped against a
            # public CA (a self-signed chain has no trusted timestamp authority),
            # and NEVER used for production publication — Publish-PathVeerRelease
            # hard-fails if dev signing is combined with a production environment.
            & $signtool sign /fd sha256 `
                /sha1 $env:PATHVEER_DEV_CODESIGN_THUMBPRINT $file
            if ($LASTEXITCODE -ne 0) { throw "signtool failed for $file (dev certificate). Check PATHVEER_DEV_CODESIGN_THUMBPRINT and that the dev root is trusted." }
        }
    }
}

Write-Host "SUCCESS: artifacts signed with backend $Backend." -ForegroundColor Green
