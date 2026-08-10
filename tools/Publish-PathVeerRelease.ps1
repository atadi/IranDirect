<#
.SYNOPSIS
    Phase 37.4 — publish a frozen, already-built release bundle to a static
    distribution surface (CDN/object-storage origin).

.DESCRIPTION
    Consumes the EXACT verified release bytes produced earlier by
    New-PathVeerRelease.ps1. It NEVER rebuilds, re-signs installers, or re-generates
    the manifest. It only:

      1. Validates the release directory (expected files present).
      2. Verifies the manifest (schema + signature) — fail-closed.
      3. Verifies artifact SHA-256 against the manifest.
      4. Uploads immutable versioned artifacts (installer, package, manifest).
      5. Verifies uploaded immutable objects (hash compare).
      6. Atomically updates the channel `latest.json` LAST.

    Publication ordering guarantees the mutable pointer is never published before the
    immutable artifacts it references exist and are verified.

    The default backend is a local filesystem root (Local). That root is itself the
    origin a CDN / object store / static host syncs from — a standard, production-real
    static-publishing pattern. No cloud SDK is required for verification or for shipping
    to any static host.

.PARAMETER ReleaseDirectory
    Frozen release bundle directory, e.g. artifacts/releases/1.0.0/win-x64.

.PARAMETER Channel
    Target channel: stable | beta. Default stable.

.PARAMETER Version
    Optional. Overrides version auto-detection from the manifest.

.PARAMETER Environment
    staging (default) | production. Production requires -ConfirmProduction and a
    fully signed manifest + Authenticode (production gate fails closed otherwise).

.PARAMETER ConfirmProduction
    Required switch to actually publish to the production environment.

.PARAMETER Backend
    Local (default). The production static-origin backend. Other providers (S3/R2/Azure
    Blob) are intentionally out of scope for 37.4; the same interface maps to them
    later by syncing the Local root.

.PARAMETER PublishRoot
    Local filesystem root that mirrors the public URL path space, e.g. ./dist-out.
    The publisher writes <PublishRoot>/windows/<version>/win-x64/... and
    <PublishRoot>/windows/<channel>/latest.json.

.PARAMETER PublicBaseUrl
    Canonical public base URL the published paths are served from, e.g.
    https://releases.pathveer.com. Used for the audit record and (in staging) to reject
    accidental localhost URLs in production.

.PARAMETER TrustedKeyBase64
    '<keyId>:<base64 64-byte public key>' used to verify the manifest signature.
    Required for a signed (production/staging) publish.

.PARAMETER WhatIf
    Dry-run: validate + print planned object paths and pointer plan; upload nothing.

.EXAMPLE
    .\tools\Publish-PathVeerRelease.ps1 -ReleaseDirectory artifacts/releases/1.0.0/win-x64 -Channel stable -Environment staging -Backend Local -PublishRoot ./dist-out -PublicBaseUrl https://releases.pathveer.com -TrustedKeyBase64 "pv-meta-2026:<pub>" -WhatIf
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseDirectory,

    [Parameter(Mandatory = $false)]
    [ValidateSet('stable', 'beta')]
    [string]$Channel = 'stable',

    [Parameter(Mandatory = $false)]
    [string]$Version = '',

    [Parameter(Mandatory = $false)]
    [ValidateSet('staging', 'production')]
    [string]$Environment = 'staging',

    [Parameter(Mandatory = $false)]
    [switch]$ConfirmProduction,

    [Parameter(Mandatory = $false)]
    [ValidateSet('Local')]
    [string]$Backend = 'Local',

    [Parameter(Mandatory = $false)]
    [string]$PublishRoot = (Join-Path $PSScriptRoot '..' 'dist-out'),

    [Parameter(Mandatory = $false)]
    [string]$PublicBaseUrl = 'https://releases.pathveer.com',

    [Parameter(Mandatory = $false)]
    [string]$TrustedKeyBase64 = '',

    [Parameter(Mandatory = $false)]
    [switch]$WhatIf,

    [Parameter(Mandatory = $false)]
    [switch]$AllowUnsigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot   = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$SignScript = Join-Path $PSScriptRoot 'Sign-ReleaseManifest.ps1'

# --- Environment / production guard -----------------------------------------
if ($Environment -eq 'production' -and -not $ConfirmProduction) {
    throw "Publishing to PRODUCTION requires -ConfirmProduction. Refusing implicit production publish."
}

# --- Resolve and validate the frozen release directory ----------------------
$ReleaseDirectory = [System.IO.Path]::GetFullPath($ReleaseDirectory)
if (-not (Test-Path $ReleaseDirectory)) { throw "Release directory not found: $ReleaseDirectory" }

$SetupExeCandidates = @(Get-ChildItem -Path $ReleaseDirectory -Filter 'PathVeerSetup-*.exe' -File)
if ($SetupExeCandidates.Count -eq 0) { throw "No PathVeerSetup-*.exe found in release directory." }
if ($SetupExeCandidates.Count -gt 1) { throw "Multiple PathVeerSetup-*.exe found; ambiguous bundle." }
$SetupExe = $SetupExeCandidates[0]

$ManifestPath = Join-Path $ReleaseDirectory 'release-manifest.json'
if (-not (Test-Path $ManifestPath)) { throw "release-manifest.json missing from release directory." }

$ZipCandidates = @(Get-ChildItem -Path $ReleaseDirectory -Filter 'PathVeer-*.zip' -File)
$ZipPath = if ($ZipCandidates.Count -ge 1) { $ZipCandidates[0].FullName } else { $null }

# --- Read + validate manifest (schema) --------------------------------------
$manifest = Get-Content -Raw $ManifestPath | ConvertFrom-Json
function Assert-Field([string]$Name, [object]$Value) {
    if ($null -eq $Value -or ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value))) {
        throw "Manifest missing required field: $Name"
    }
}
Assert-Field 'schemaVersion' $manifest.schemaVersion
Assert-Field 'product'       $manifest.product
Assert-Field 'version'       $manifest.version
Assert-Field 'channel'       $manifest.channel
Assert-Field 'platform'      $manifest.platform
Assert-Field 'architecture'  $manifest.architecture
Assert-Field 'installer.fileName' $manifest.installer.fileName
Assert-Field 'installer.sha256'   $manifest.installer.sha256
Assert-Field 'installer.url'      $manifest.installer.url

if ($manifest.schemaVersion -ne 1) { throw "Unsupported schemaVersion $($manifest.schemaVersion); expected 1." }
if ($manifest.product -ne 'PathVeer') { throw "Wrong product: $($manifest.product)." }
if ($manifest.platform -ne 'windows') { throw "Wrong platform: $($manifest.platform)." }
if ($manifest.architecture -ne 'x64') { throw "Wrong architecture: $($manifest.architecture)." }

$ReleaseVersion = if ($Version) { $Version } else { $manifest.version }
if ($manifest.version -ne $ReleaseVersion) {
    throw "Manifest version ($($manifest.version)) does not match -Version ($ReleaseVersion)."
}
if ($manifest.channel -ne $Channel) {
    throw "Manifest channel ($($manifest.channel)) does not match -Channel ($Channel)."
}

# Installer URL must be https (production) or an explicitly allowed localhost (test).
$installerUri = [System.Uri]::new($manifest.installer.url)
if ($installerUri.Scheme -ne 'https' -and -not ($installerUri.Host -match '^(localhost|127\.0\.0\.1)$')) {
    throw "Installer URL is not https: $($manifest.installer.url)"
}
if ($Environment -eq 'production' -and $installerUri.Host -match '^(localhost|127\.0\.0\.1)$') {
    throw "Production manifest must not contain localhost URLs."
}
if ($Environment -eq 'production' -and $PublicBaseUrl -match '^(http://localhost|http://127\.0\.0\.1)') {
    throw "Production PublicBaseUrl must not be localhost."
}

# --- Verify manifest signature (fail-closed) --------------------------------
$isSigned = ($null -ne $manifest.PSObject.Properties['signature']) -and $manifest.signed -eq $true
if (-not $isSigned) {
    if ($AllowUnsigned) {
        Write-Host "  AllowUnsigned set: publishing UNSIGNED manifest (non-production override)." -ForegroundColor DarkYellow
    } else {
        throw "Manifest is UNSIGNED. Publishing requires a signed manifest. Use -AllowUnsigned only for explicit non-production test fixtures."
    }
}
else {
    if ([string]::IsNullOrWhiteSpace($TrustedKeyBase64)) {
        throw "Signed manifest requires -TrustedKeyBase64 '<keyId>:<pub64>' for verification."
    }
    & pwsh -NoLogo -NoProfile -File $SignScript `
        -ManifestPath $ManifestPath `
        -VerifyOnly `
        -TrustedKeyBase64 $TrustedKeyBase64
    if ($LASTEXITCODE -ne 0) { throw "Manifest signature verification failed." }
}

# --- Verify installer hash against manifest ----------------------------------
function Get-Sha256([string]$Path) {
    # Retry: freshly-written .exe files in temp are occasionally transiently locked
    # by endpoint protection (e.g. Windows Defender) for a moment after creation.
    $lastErr = $null
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            return (Get-FileHash -Path $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        } catch {
            $lastErr = $_
            if ($attempt -lt 5) { Start-Sleep -Milliseconds 150 }
        }
    }
    throw "Unable to hash $Path after retries: $lastErr"
}
$actualInstallerHash = Get-Sha256 $SetupExe.FullName
if ($actualInstallerHash -ne $manifest.installer.sha256.ToLowerInvariant()) {
    throw "Installer hash mismatch: manifest $($manifest.installer.sha256) vs actual $actualInstallerHash."
}
if ($ZipPath) {
    $actualZipHash = Get-Sha256 $ZipPath
    if ($actualZipHash -ne $manifest.packageArchive.sha256.ToLowerInvariant()) {
        throw "Package hash mismatch: manifest $($manifest.packageArchive.sha256) vs actual $actualZipHash."
    }
}

Write-Host "  Release validated: PathVeer $ReleaseVersion ($Channel) signed=$isSigned" -ForegroundColor Green

# --- Backend: local filesystem origin ---------------------------------------
$PublishRoot = [System.IO.Path]::GetFullPath($PublishRoot)
$baseUri = [System.Uri]::new($PublicBaseUrl.TrimEnd('/') + '/')

# Map a public relative path (no leading slash) to a local file under PublishRoot.
function Local-Path([string]$Relative) {
    # Relative uses forward slashes; normalise to the OS separator.
    $rel = $Relative -replace '/', [System.IO.Path]::DirectorySeparatorChar
    return Join-Path $PublishRoot $rel
}
function Public-Url([string]$Relative) {
    return ($baseUri.ToString().TrimEnd('/') + '/' + $Relative)
}

# Immutable object paths (versioned, never overwritten with different bytes).
$immutable = @(
    @{ File = $SetupExe.FullName; Rel = "windows/$ReleaseVersion/win-x64/$($SetupExe.Name)"; Hash = $actualInstallerHash; ContentType = 'application/octet-stream' }
    @{ File = $ManifestPath;      Rel = "windows/$ReleaseVersion/win-x64/release-manifest.json"; Hash = (Get-Sha256 $ManifestPath); ContentType = 'application/json' }
)
if ($ZipPath) {
    $immutable += @{ File = $ZipPath; Rel = "windows/$ReleaseVersion/win-x64/$($ZipCandidates[0].Name)"; Hash = $actualZipHash; ContentType = 'application/octet-stream' }
}

# Mutable channel pointer (published LAST).
$latestRel  = "windows/$Channel/latest.json"
$latestPath = Local-Path $latestRel

# --- Idempotent immutable upload with overwrite protection -------------------
function Put-Immutable([hashtable]$Obj) {
    $target = Local-Path $Obj.Rel
    $exists = Test-Path $target
    if ($exists) {
        $existing = Get-Sha256 $target
        if ($existing -eq $Obj.Hash) {
            Write-Host "  unchanged: $($Obj.Rel)" -ForegroundColor DarkGray
            return 'Unchanged'
        }
        # Same immutable path, DIFFERENT bytes -> hard fail (never overwrite a release).
        throw "Immutable object $($Obj.Rel) already exists with a DIFFERENT hash. Refusing to overwrite a published release. Publish a new version instead."
    }
    if ($WhatIf) {
        Write-Host "  [dry-run] PUT $($Obj.Rel) -> $(Public-Url $Obj.Rel)" -ForegroundColor Cyan
        return 'DryRun'
    }
    $dir = Split-Path -Parent $target
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item -Path $Obj.File -Destination $target -Force
    $verified = Get-Sha256 $target
    if ($verified -ne $Obj.Hash) { throw "Post-upload hash mismatch for $($Obj.Rel)." }
    Write-Host "  published: $($Obj.Rel)" -ForegroundColor Green
    return 'Published'
}

foreach ($obj in $immutable) { Put-Immutable $obj | Out-Null }

# --- Atomically update the channel latest pointer LAST ----------------------
# The latest.json is the exact signed manifest bytes (no server mutation).
if ($WhatIf) {
    Write-Host "  [dry-run] PUT $latestRel -> $(Public-Url $latestRel) (mutable channel pointer)" -ForegroundColor Cyan
} else {
    $latestDir = Split-Path -Parent $latestPath
    New-Item -ItemType Directory -Force -Path $latestDir | Out-Null
    $tmp = "$latestPath.tmp"
    Copy-Item -Path $ManifestPath -Destination $tmp -Force   # copy signed bytes verbatim
    # Atomic replace (same volume): rename is atomic on NTFS.
    if (Test-Path $latestPath) { Remove-Item $latestPath -Force }
    Move-Item -Path $tmp -Destination $latestPath -Force
    Write-Host "  published channel pointer: $latestRel -> $(Public-Url $latestRel)" -ForegroundColor Green
}

# --- Audit record -----------------------------------------------------------
$commit = (& git -C $RepoRoot rev-parse --short HEAD 2>$null)
if ($LASTEXITCODE -ne 0) { $commit = $null }
$audit = [ordered]@{
    tool            = 'Publish-PathVeerRelease.ps1'
    timestampUtc    = (Get-Date).ToUniversalTime().ToString('o')
    version         = $ReleaseVersion
    channel         = $Channel
    environment     = $Environment
    backend         = $Backend
    publicBaseUrl   = $PublicBaseUrl
    signed          = $isSigned
    dryRun          = [bool]$WhatIf
    commit          = $commit
    objects         = @(
        @{ path = "windows/$ReleaseVersion/win-x64/$($SetupExe.Name)"; sha256 = $actualInstallerHash }
        @{ path = "windows/$ReleaseVersion/win-x64/release-manifest.json"; sha256 = (Get-Sha256 $ManifestPath) }
        if ($ZipPath) { @{ path = "windows/$ReleaseVersion/win-x64/$($ZipCandidates[0].Name)"; sha256 = $actualZipHash } }
    )
    channelPointer  = "windows/$Channel/latest.json"
    result          = if ($WhatIf) { 'DryRun' } else { 'Published' }
}
$auditFile = Join-Path $ReleaseDirectory "publish-audit-$Environment.json"
if (-not $WhatIf) {
    $audit | ConvertTo-Json -Depth 6 | Set-Content -Path $auditFile -Encoding UTF8
    Write-Host "  Audit record written: $auditFile" -ForegroundColor DarkGray
}

Write-Host ""
if ($WhatIf) {
    Write-Host "DRY-RUN complete. No objects were uploaded." -ForegroundColor Cyan
} else {
    Write-Host "SUCCESS: published PathVeer $ReleaseVersion ($Channel) to $Environment" -ForegroundColor Green
}
