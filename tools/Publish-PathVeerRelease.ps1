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
    [ValidateSet('Local', 'R2')]
    [string]$Backend = 'Local',

    # --- R2 backend options (ignored for Local) ------------------------------
    [Parameter(Mandatory = $false)]
    [string]$R2CredentialPath = '',

    [Parameter(Mandatory = $false)]
    [string]$R2ConfigPath = '',

    [Parameter(Mandatory = $false)]
    [switch]$ShowRetention,

    [Parameter(Mandatory = $false)]
    [switch]$ApplyRetention,

    [Parameter(Mandatory = $false)]
    [long]$StorageBudgetBytes = 0,

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

# --- Publication plan (backend-agnostic) -------------------------------------
$baseUri = [System.Uri]::new($PublicBaseUrl.TrimEnd('/') + '/')

function Public-Url([string]$Relative) {
    return ($baseUri.ToString().TrimEnd('/') + '/' + $Relative)
}

$rid = 'win-x64'

# Immutable object paths (versioned, never overwritten with different bytes).
$manifestHash = Get-Sha256 $ManifestPath
$immutable = @(
    @{ File = $SetupExe.FullName; Rel = "windows/$ReleaseVersion/$rid/$($SetupExe.Name)"; Hash = $actualInstallerHash; ContentType = 'application/octet-stream' }
    @{ File = $ManifestPath;      Rel = "windows/$ReleaseVersion/$rid/release-manifest.json"; Hash = $manifestHash; ContentType = 'application/json' }
)
if ($ZipPath) {
    $immutable += @{ File = $ZipPath; Rel = "windows/$ReleaseVersion/$rid/$($ZipCandidates[0].Name)"; Hash = $actualZipHash; ContentType = 'application/zip' }
}

# Mutable channel pointer (published LAST).
$latestRel = "windows/$Channel/latest.json"

# The signed manifest's installer URL must already point at the public base URL
# we are publishing to. Signed bytes are NEVER edited after signing; a mismatch
# means the release must be regenerated and re-signed with the correct base URL.
if ($Backend -eq 'R2') {
    $expectedInstallerUrl = Public-Url "windows/$ReleaseVersion/$rid/$($SetupExe.Name)"
    if ($manifest.installer.url -ne $expectedInstallerUrl) {
        throw "Signed manifest installer URL ($($manifest.installer.url)) does not match the publication target ($expectedInstallerUrl). Regenerate and re-sign the release with the correct base URL; signed manifests are never edited after signing."
    }
}

$publishResults = @()
$bytesUploaded  = [long]0

if ($Backend -eq 'Local') {
    # --- Backend: local filesystem origin ------------------------------------
    $PublishRoot = [System.IO.Path]::GetFullPath($PublishRoot)

    function Local-Path([string]$Relative) {
        $rel = $Relative -replace '/', [System.IO.Path]::DirectorySeparatorChar
        return Join-Path $PublishRoot $rel
    }
    $latestPath = Local-Path $latestRel

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

    foreach ($obj in $immutable) { $publishResults += Put-Immutable $obj }

    # --- Atomically update the channel latest pointer LAST -------------------
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
}
elseif ($Backend -eq 'R2') {
    # --- Backend: Cloudflare R2 (S3-compatible) ------------------------------
    Import-Module (Join-Path $PSScriptRoot 'PathVeerR2.psm1') -Force
    Import-Module AWS.Tools.S3 -ErrorAction Stop

    $ctx = Get-PathVeerR2Context -CredentialPath $R2CredentialPath -ConfigPath $R2ConfigPath

    if ($ctx.PublicBaseUrl.TrimEnd('/') -ne $PublicBaseUrl.TrimEnd('/')) {
        throw "R2 config PublicBaseUrl ($($ctx.PublicBaseUrl)) does not match -PublicBaseUrl ($PublicBaseUrl)."
    }

    Write-Host "  R2 backend:" -ForegroundColor Cyan
    foreach ($kv in (Get-PathVeerR2Diagnostics -Context $ctx).GetEnumerator()) {
        Write-Host ("    {0,-20} {1}" -f $kv.Key, $kv.Value)
    }

    # --- Snapshot the bucket (storage accounting + retention + stable proof) --
    # Sum-Bytes tolerates an empty listing (a brand-new bucket) under StrictMode,
    # where Measure-Object returns no Sum property at all.
    function Sum-Bytes([object[]]$Items, [string]$Property = 'Size') {
        if ($null -eq $Items -or @($Items).Count -eq 0) { return [long]0 }
        $m = @($Items) | Measure-Object -Property $Property -Sum
        if ($null -eq $m -or $null -eq $m.Sum) { return [long]0 }
        return [long]$m.Sum
    }

    $remoteObjects = @(Get-PathVeerR2ObjectList -Context $ctx -Prefix 'windows/')
    $currentBytes  = Sum-Bytes $remoteObjects 'Size'

    # Record the current stable pointer state so we can prove it is untouched.
    $stableKey  = Get-PathVeerR2ChannelPointerKey -Channel 'stable'
    $stableStat = $remoteObjects | Where-Object { $_.Key -eq $stableKey } | Select-Object -First 1
    $stableBefore = $null
    if ($stableStat) {
        $tmpStable = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-stable-before-" + [guid]::NewGuid().ToString('N') + ".json")
        Read-PathVeerR2ObjectToFile -Context $ctx -Key $stableKey -Destination $tmpStable | Out-Null
        $stableBefore = Get-Sha256 $tmpStable
        Remove-Item $tmpStable -Force -ErrorAction SilentlyContinue
        Write-Host "  stable pointer present (recorded for isolation proof): sha256=$stableBefore" -ForegroundColor DarkGray
    } else {
        Write-Host "  stable pointer absent; leaving windows/stable untouched." -ForegroundColor DarkGray
    }

    # --- Plan each immutable object against the remote state ------------------
    $plan = @()
    foreach ($obj in $immutable) {
        $stat = $remoteObjects | Where-Object { $_.Key -eq $obj.Rel } | Select-Object -First 1
        $remoteHash = $null
        if ($stat) {
            # Authoritative comparison: download the actual bytes and hash them.
            # S3/R2 ETag is NEVER used as a content hash (multipart ETags are not).
            $tmpV = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-verify-" + [guid]::NewGuid().ToString('N'))
            Read-PathVeerR2ObjectToFile -Context $ctx -Key $obj.Rel -Destination $tmpV | Out-Null
            $remoteHash = Get-Sha256 $tmpV
            Remove-Item $tmpV -Force -ErrorAction SilentlyContinue
        }
        $action = Get-PathVeerR2ImmutableAction -LocalSha256 $obj.Hash -RemoteSha256 $remoteHash -RemoteExists ([bool]$stat)
        if ($action -eq 'Reject') {
            throw "IMMUTABILITY VIOLATION: $($obj.Rel) already exists in R2 with a DIFFERENT SHA-256 (remote=$remoteHash local=$($obj.Hash)). Refusing to overwrite a published release. Publish a new version instead."
        }
        $plan += [pscustomobject]@{
            Rel    = $obj.Rel
            File   = $obj.File
            Hash   = $obj.Hash
            Action = $action
            Bytes  = [long](Get-Item -LiteralPath $obj.File).Length
        }
    }

    $newBytes = Sum-Bytes @($plan | Where-Object { $_.Action -eq 'Upload' }) 'Bytes'
    $budget = Test-PathVeerStorageBudget -CurrentBytes $currentBytes -NewBytes $newBytes -BudgetBytes $StorageBudgetBytes

    function Format-Bytes([long]$b) {
        if ($b -ge 1GB) { return ('{0:N2} GiB' -f ($b / 1GB)) }
        if ($b -ge 1MB) { return ('{0:N2} MiB' -f ($b / 1MB)) }
        if ($b -ge 1KB) { return ('{0:N2} KiB' -f ($b / 1KB)) }
        return "$b B"
    }

    # --- Retention model ------------------------------------------------------
    function Get-PointerVersion([string]$Key) {
        $s = $remoteObjects | Where-Object { $_.Key -eq $Key } | Select-Object -First 1
        if (-not $s) { return $null }
        $t = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-ptr-" + [guid]::NewGuid().ToString('N') + ".json")
        try {
            Read-PathVeerR2ObjectToFile -Context $ctx -Key $Key -Destination $t | Out-Null
            $j = Get-Content -Raw $t | ConvertFrom-Json
            return [string]$j.version
        } catch { return $null }
        finally { Remove-Item $t -Force -ErrorAction SilentlyContinue }
    }

    $releaseSets    = @(ConvertTo-PathVeerReleaseSet -Objects $remoteObjects)
    $stableLatestV  = Get-PointerVersion (Get-PathVeerR2ChannelPointerKey -Channel 'stable')
    $betaLatestV    = Get-PointerVersion (Get-PathVeerR2ChannelPointerKey -Channel 'beta')
    $upgradeFloor   = if ($manifest.PSObject.Properties['minimumUpgradeVersion']) { [string]$manifest.minimumUpgradeVersion } else { $null }
    $retention      = @(Get-PathVeerRetentionPlan -ReleaseSets $releaseSets `
                            -StableLatestVersion $stableLatestV `
                            -BetaLatestVersion   $betaLatestV `
                            -UpgradeFloorVersion $upgradeFloor)
    $eligible       = @($retention | Where-Object { $_.Eligible })

    # --- Report ---------------------------------------------------------------
    Write-Host ""
    Write-Host "  R2 publication plan" -ForegroundColor Cyan
    Write-Host ("    bucket                {0}" -f $ctx.Bucket)
    Write-Host ("    public base URL       {0}" -f $ctx.PublicBaseUrl)
    Write-Host ("    channel               {0}" -f $Channel)
    Write-Host ("    version               {0}" -f $ReleaseVersion)
    foreach ($p in $plan) {
        Write-Host ("    {0,-9} {1} ({2})" -f $p.Action, $p.Rel, (Format-Bytes $p.Bytes))
    }
    Write-Host ("    objects to upload     {0}" -f @($plan | Where-Object { $_.Action -eq 'Upload' }).Count)
    Write-Host ("    already identical     {0}" -f @($plan | Where-Object { $_.Action -eq 'NoOp' }).Count)
    Write-Host ("    projected bytes added {0}" -f (Format-Bytes $newBytes))
    Write-Host ("    current storage       {0}" -f (Format-Bytes $budget.CurrentBytes))
    Write-Host ("    projected storage     {0}" -f (Format-Bytes $budget.ProjectedBytes))
    Write-Host ("    storage budget        {0}" -f (Format-Bytes $budget.BudgetBytes))
    Write-Host ("    latest pointer action PUT {0} (LAST)" -f $latestRel)

    if ($ShowRetention -or -not $budget.Allowed) {
        Write-Host ""
        Write-Host "  Retention preview (stable keep=15, beta/RC keep=5)" -ForegroundColor Cyan
        if ($retention.Count -eq 0) {
            Write-Host "    (no hosted release object sets)"
        }
        foreach ($r in $retention) {
            Write-Host ("    {0,-16} {1,-6} rank={2,-3} bytes={3,-12} latest={4,-5} protected={5,-5} eligible={6}" -f `
                $r.Version, $r.Channel, $r.Rank, (Format-Bytes $r.Bytes), $r.IsLatestTarget, $r.Protected, $r.Eligible)
        }
    }

    if (-not $budget.Allowed) {
        Write-Host ""
        Write-Host "  STORAGE BUDGET EXCEEDED" -ForegroundColor Red
        Write-Host ("    projected {0} exceeds the {1} conservative threshold." -f (Format-Bytes $budget.ProjectedBytes), (Format-Bytes $budget.BudgetBytes))
        if ($eligible.Count -gt 0) {
            Write-Host ("    eligible for cleanup: {0} release set(s), {1}" -f $eligible.Count, (Format-Bytes (Sum-Bytes $eligible 'Bytes')))
            foreach ($e in $eligible) { Write-Host ("      {0} ({1}) {2}" -f $e.Version, $e.Channel, (Format-Bytes $e.Bytes)) }
            Write-Host "    Re-run with -ApplyRetention to delete the eligible release sets explicitly."
        } else {
            Write-Host "    No release sets are eligible for cleanup (all protected or within keep counts)."
        }
        throw "Publication STOPPED: projected R2 storage exceeds the 8 GiB application safety threshold."
    }

    # --- Explicit destructive retention (never implicit) ----------------------
    if ($ApplyRetention) {
        if ($WhatIf) {
            Write-Host "  [dry-run] -ApplyRetention would delete $($eligible.Count) eligible release set(s)." -ForegroundColor Cyan
        } elseif ($eligible.Count -eq 0) {
            Write-Host "  -ApplyRetention: nothing eligible; no deletions performed." -ForegroundColor DarkGray
        } else {
            foreach ($e in $eligible) {
                foreach ($k in $e.Keys) {
                    Remove-PathVeerR2Object -Context $ctx -Key $k | Out-Null
                    Write-Host "  retention deleted: $k" -ForegroundColor Yellow
                }
            }
        }
    }

    if ($WhatIf) {
        Write-Host ""
        Write-Host "  [dry-run] no objects were uploaded, deleted, or mutated in R2." -ForegroundColor Cyan
        foreach ($p in $plan) { $publishResults += 'DryRun' }
    }
    else {
        # --- 4/5. Upload immutable artifacts, then verify remote bytes --------
        foreach ($p in $plan) {
            if ($p.Action -eq 'NoOp') {
                Write-Host "  unchanged: $($p.Rel)" -ForegroundColor DarkGray
                $publishResults += 'Unchanged'
                continue
            }
            Write-PathVeerR2Object -Context $ctx -Key $p.Rel -File $p.File -Sha256 $p.Hash -Version $ReleaseVersion -Channel $Channel | Out-Null
            # Authoritative post-upload verification: read the object back and hash it.
            $tmpV = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-post-" + [guid]::NewGuid().ToString('N'))
            Read-PathVeerR2ObjectToFile -Context $ctx -Key $p.Rel -Destination $tmpV | Out-Null
            $back = Get-Sha256 $tmpV
            Remove-Item $tmpV -Force -ErrorAction SilentlyContinue
            if ($back -ne $p.Hash) { throw "Post-upload verification FAILED for $($p.Rel): remote=$back expected=$($p.Hash)." }
            Write-Host "  published + verified: $($p.Rel)" -ForegroundColor Green
            $bytesUploaded += $p.Bytes
            $publishResults += 'Published'
        }

        # --- 6/7/8. Channel pointer LAST, verbatim signed manifest bytes ------
        Write-PathVeerR2Object -Context $ctx -Key $latestRel -File $ManifestPath -Sha256 $manifestHash -Version $ReleaseVersion -Channel $Channel | Out-Null
        $tmpP = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-ptr-post-" + [guid]::NewGuid().ToString('N') + ".json")
        Read-PathVeerR2ObjectToFile -Context $ctx -Key $latestRel -Destination $tmpP | Out-Null
        $ptrHash = Get-Sha256 $tmpP
        Remove-Item $tmpP -Force -ErrorAction SilentlyContinue
        if ($ptrHash -ne $manifestHash) { throw "Channel pointer verification FAILED for $latestRel." }
        Write-Host "  published channel pointer LAST: $latestRel -> $(Public-Url $latestRel)" -ForegroundColor Green

        # --- Prove stable isolation -------------------------------------------
        if ($stableBefore) {
            $tmpS = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-stable-after-" + [guid]::NewGuid().ToString('N') + ".json")
            Read-PathVeerR2ObjectToFile -Context $ctx -Key $stableKey -Destination $tmpS | Out-Null
            $stableAfter = Get-Sha256 $tmpS
            Remove-Item $tmpS -Force -ErrorAction SilentlyContinue
            if ($stableAfter -ne $stableBefore) { throw "STABLE CHANNEL MUTATED during a $Channel publication (before=$stableBefore after=$stableAfter)." }
            Write-Host "  stable pointer unchanged (sha256=$stableAfter)" -ForegroundColor Green
        } else {
            $stillAbsent = @(Get-PathVeerR2ObjectList -Context $ctx -Prefix $stableKey)
            if ($stillAbsent.Count -ne 0) { throw "STABLE CHANNEL CREATED during a $Channel publication. Refusing." }
            Write-Host "  stable pointer still absent (untouched)" -ForegroundColor Green
        }
    }
}
else {
    throw "Unsupported backend: $Backend"
}

# --- Audit record -----------------------------------------------------------
$commit = (& git -C $RepoRoot rev-parse --short HEAD 2>$null)
if ($LASTEXITCODE -ne 0) { $commit = $null }
$auditObjects = @()
foreach ($obj in $immutable) {
    $auditObjects += @{ path = $obj.Rel; sha256 = $obj.Hash }
}
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
    objects         = $auditObjects
    channelPointer  = $latestRel
    bytesUploaded   = $bytesUploaded
    result          = if ($WhatIf) { 'DryRun' } else { 'Published' }
}
$auditFile = Join-Path $ReleaseDirectory "publish-audit-$Environment-$($Backend.ToLowerInvariant()).json"
if (-not $WhatIf) {
    $audit | ConvertTo-Json -Depth 6 | Set-Content -Path $auditFile -Encoding UTF8
    Write-Host "  Audit record written: $auditFile" -ForegroundColor DarkGray
}

Write-Host ""
if ($WhatIf) {
    Write-Host "DRY-RUN complete. No objects were uploaded." -ForegroundColor Cyan
} else {
    Write-Host "SUCCESS: published PathVeer $ReleaseVersion ($Channel) to $Environment via $Backend" -ForegroundColor Green
}
