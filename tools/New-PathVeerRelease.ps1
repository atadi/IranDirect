<#
.SYNOPSIS
    Phase 37.1 — single entrypoint that builds a complete PathVeer release.

.DESCRIPTION
    Orchestrates a deterministic release build:

        1. New-PathVeerPackage.ps1   -> versioned component package
        2. dotnet publish PathVeer.Setup (self-contained single-file)
        3. Sign-PathVeerArtifacts.ps1 -> Authenticode (if credentials present)
        4. Assemble                       artifacts/releases/<ver>/<ver>-win-x64/
           with:
             PathVeerSetup-<ver>-win-x64.exe
             PathVeer-<ver>-win-x64.zip
             PathVeerSetup-<ver>-win-x64.exe.sha256
             checksums.txt
             release-manifest.json

    The embedded PowerShell deployment script remains the single source of
    install truth; this script never re-implements migration/state logic.

.PARAMETER Version
    Product version, e.g. 1.0.0 or 1.0.0-beta.1.

.PARAMETER Mode
    Development/Unsigned  (default): no signing attempted, fails only on build
    Release/Unsigned      : build + package + hash, no signing
    Release/Signed        : also Authenticode-sign every artifact that has a
                            signable payload; FAILS if signing is configured but
                            unavailable.

.PARAMETER OutputDirectory
    Root for artifacts. Defaults to <repo>\artifacts\releases.

.EXAMPLE
    .\tools\New-PathVeerRelease.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [ValidateSet('Development/Unsigned', 'Release/Unsigned', 'Release/Signed')]
    [string]$Mode = 'Development/Unsigned',

    [Parameter(Mandatory = $false)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $false)]
    [ValidateSet('stable', 'beta')]
    [string]$Channel = 'stable',

    [Parameter(Mandatory = $false)]
    [string]$RuntimeIdentifier = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $RepoRoot 'artifacts\releases'
}

$PackageStep  = Join-Path $PSScriptRoot 'New-PathVeerPackage.ps1'
$SignStep     = Join-Path $PSScriptRoot 'Sign-PathVeerArtifacts.ps1'
$SignManifestStep = Join-Path $PSScriptRoot 'Sign-ReleaseManifest.ps1'

$VersionedReleaseRoot = Join-Path $OutputDirectory "$Version"
$ArchReleaseRoot      = Join-Path $VersionedReleaseRoot $RuntimeIdentifier
$SetupExeName         = "PathVeerSetup-$Version-$RuntimeIdentifier.exe"
$ZipName              = "PathVeer-$Version-$RuntimeIdentifier.zip"

function Write-Step([string]$Message) {
    Write-Host $Message -ForegroundColor Cyan
}

function Assert-Command([string]$Name, [string]$OnFail) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw $OnFail
    }
}

# ---------------------------------------------------------------------------
Write-Step "Phase 37.1 release build: PathVeer $Version ($Mode)"
Write-Host "  Repo:    $RepoRoot"
Write-Host "  Output:  $ArchReleaseRoot"
Write-Host ""

Assert-Command dotnet "dotnet SDK is required."

# 1. Component package
Write-Step "1/4 Building component package..."
& pwsh -NoLogo -NoProfile -File $PackageStep `
    -Version $Version `
    -OutputDirectory (Join-Path $OutputDirectory "packages") `
    -RuntimeIdentifier $RuntimeIdentifier
if ($LASTEXITCODE -ne 0) { throw "Package build failed." }

$PackageDir = Join-Path $OutputDirectory "packages\PathVeer-$Version"
if (-not (Test-Path $PackageDir)) { throw "Package directory missing: $PackageDir" }

# 2. Bootstrapper (self-contained single-file)
Write-Step "2/4 Publishing PathVeerSetup bootstrapper..."
$SetupProject = Join-Path $RepoRoot 'PathVeer.Setup\PathVeer.Setup.csproj'
$SetupPublish = Join-Path $ArchReleaseRoot 'setup-publish'

& dotnet publish $SetupProject `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $SetupPublish
if ($LASTEXITCODE -ne 0) { throw "Bootstrapper publish failed." }

$PublishedSetup = Join-Path $SetupPublish "PathVeer.Setup.exe"
if (-not (Test-Path $PublishedSetup)) { throw "Bootstrapper exe missing after publish." }

# Assemble release folder: setup exe + package folder.
New-Item -ItemType Directory -Force -Path $ArchReleaseRoot | Out-Null
Copy-Item -Path $PublishedSetup -Destination (Join-Path $ArchReleaseRoot $SetupExeName) -Force
$PackageDest = Join-Path $ArchReleaseRoot "PathVeer-$Version"
New-Item -ItemType Directory -Force -Path $PackageDest | Out-Null
# Copy package *contents* (not the folder itself) so the layout is
#   win-x64/PathVeer-1.0.0/{Service,Cli,Tray,...}
# and not a nested win-x64/PathVeer-1.0.0/PathVeer-1.0.0.
Copy-Item -Path (Join-Path $PackageDir '*') -Destination $PackageDest -Recurse -Force

# The bootstrapper is published to a temp subfolder; remove it from the release.
if (Test-Path $SetupPublish) { Remove-Item $SetupPublish -Recurse -Force }

# 3. Signing (mode-dependent)
$SignedMode = $Mode -eq 'Release/Signed'
if ($SignedMode) {
    Write-Step "3/4 Authenticode signing..."
    & pwsh -NoLogo -NoProfile -File $SignStep `
        -ReleaseRoot $ArchReleaseRoot `
        -FailIfUnavailable:$true
    if ($LASTEXITCODE -ne 0) { throw "Signing failed (Release/Signed requires usable credentials)." }
}
else {
    Write-Step "3/4 Signing SKIPPED (mode '$Mode')."
}

# 4. Integrity artifacts + manifest
Write-Step "4/4 Hashes, checksums and release manifest..."

$SignedFlag = $SignedMode

function Get-Sha256([string]$Path) {
    (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$checksums = New-Object System.Collections.Generic.List[string]
$setupPath  = Join-Path $ArchReleaseRoot $SetupExeName
$setupHash  = Get-Sha256 $setupPath
"$setupHash  $SetupExeName" | Set-Content -Path (Join-Path $ArchReleaseRoot "$SetupExeName.sha256") -Encoding ASCII
$checksums.Add("$setupHash  $SetupExeName")

# Zip the package for portable distribution.
$zipPath = Join-Path $ArchReleaseRoot $ZipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $ArchReleaseRoot "PathVeer-$Version") -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = Get-Sha256 $zipPath
"$zipHash  $ZipName" | Set-Content -Path (Join-Path $ArchReleaseRoot "$ZipName.sha256") -Encoding ASCII
$checksums.Add("$zipHash  $ZipName")

# Hash every remaining local file in the release (recursive), excluding the
# .sha256 sidecars we just wrote and the two primary artifacts already added
# above. Relative paths use Path.GetRelativePath so slash direction cannot
# corrupt them. Deduped for a clean manifest.
$excludeNames = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]@($SetupExeName, $ZipName),
    [System.StringComparer]::OrdinalIgnoreCase)

Get-ChildItem -Path $ArchReleaseRoot -Recurse -File |
    Where-Object { $_.Extension -ne '.sha256' -and -not $excludeNames.Contains($_.Name) } |
    Sort-Object FullName |
    ForEach-Object {
        $rel = [System.IO.Path]::GetRelativePath($ArchReleaseRoot, $_.FullName)
        $entry = "$(Get-Sha256 $_.FullName)  $rel"
        if (-not $checksums.Contains($entry)) { $checksums.Add($entry) }
    }

Set-Content -Path (Join-Path $ArchReleaseRoot 'checksums.txt') -Value $checksums -Encoding ASCII

# Minimum direct-upgrade floor: same major.minor, patch 0 (pre-1.0 builds cannot
# take a 1.0 installer directly). Kept simple for v1; tighten as migrations appear.
$minUpgrade = if ($Version -match '^(\d+)\.(\d+)\.') { "$($Matches[1]).$($Matches[2]).0" } else { '1.0.0' }
$installerUrl  = "https://releases.pathveer.com/windows/$Channel/PathVeerSetup-$Version-$RuntimeIdentifier.exe"
$packageUrl    = "https://releases.pathveer.com/windows/$Channel/PathVeer-$Version-$RuntimeIdentifier.zip"

$manifest = [ordered]@{
    schemaVersion          = 1
    product                = 'PathVeer'
    version                = $Version
    channel                = $Channel
    platform               = 'windows'
    architecture           = 'x64'
    publishedAtUtc         = (Get-Date).ToUniversalTime().ToString('o')
    minimumUpgradeVersion  = $minUpgrade
    installer              = [ordered]@{
        fileName = $SetupExeName
        url      = $installerUrl
        sha256   = $setupHash
        size     = (Get-Item (Join-Path $ArchReleaseRoot $SetupExeName)).Length
    }
    packageArchive         = [ordered]@{
        fileName = $ZipName
        url      = $packageUrl
        sha256   = $zipHash
        size     = (Get-Item $zipPath).Length
    }
    signed                 = $SignedFlag
    releaseMode            = $Mode
    components             = @('Service', 'Cli', 'Tray')
}

$manifest | ConvertTo-Json -Depth 4 |
    Set-Content -Path (Join-Path $ArchReleaseRoot 'release-manifest.json') -Encoding UTF8

# 5. Manifest signature (separate metadata key, ES256) — after generation so it
# covers the final manifest bytes. Fail-closed under Release/Signed.
$ManifestPath = Join-Path $ArchReleaseRoot 'release-manifest.json'
if ($SignedMode) {
    Write-Step "5/5 Signing release manifest (ES256)..."
    & pwsh -NoLogo -NoProfile -File $SignManifestStep `
        -ManifestPath $ManifestPath `
        -KeyId 'pv-meta-2026' `
        -FailIfUnavailable:$true
    if ($LASTEXITCODE -ne 0) { throw "Manifest signing failed (Release/Signed requires a metadata signing key)." }
}
else {
    Write-Step "5/5 Manifest signature SKIPPED (mode '$Mode')."
}

Write-Host ""
Write-Host "SUCCESS: release built" -ForegroundColor Green
Write-Host "  $ArchReleaseRoot"
Write-Host ""
Write-Host "Contents:" -ForegroundColor Cyan
Get-ChildItem -Path $ArchReleaseRoot | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor DarkGray }

if (-not $SignedFlag) {
    Write-Warning "This release is NOT code-signed. Sign with -Mode Release/Signed before public distribution."
}
