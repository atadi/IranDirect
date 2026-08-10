<#
.SYNOPSIS
    PathVeer R2 (S3-compatible) distribution backend helpers.

.DESCRIPTION
    Smallest reliable S3 client mechanism for PathVeer release publication to
    Cloudflare R2. Chosen mechanism: the official AWS.Tools.S3 PowerShell module
    (modular AWS Tools for PowerShell). Rationale:

      * Already an official, maintained SigV4 implementation — no hand-rolled
        signing (explicitly prohibited by the release contract).
      * Modular: only the S3 service module is required (no monolithic SDK).
      * Supports every operation PathVeer needs (head/stat, put, get, list,
        delete) with -EndpointUrl + -ForcePathStyleAddressing, which is exactly
        what Cloudflare R2's S3 API requires.
      * No project dependency is added to the shipped product; this is a
        build/release-time tool dependency only.

    Credentials are loaded from a Windows DPAPI-protected PSCredential file
    (Import-Clixml). The secret access key is only ever materialised in-process
    as a transient string handed directly to the AWS cmdlet parameter. It is
    never written to disk, never placed in an environment variable, never
    logged, and never included in any diagnostic output.

    R2 notes:
      * R2 does not implement the full AWS checksum-trailer behaviour used by
        newer SDK defaults, so uploads pass -DisableDefaultChecksumValidation.
      * ETag is NOT treated as a content hash anywhere in this module.
        PathVeer's own SHA-256 is always authoritative.
#>

Set-StrictMode -Version Latest

$script:PathVeerR2Region = 'auto'
# Bounded (not unlimited) timeout for large release-artifact transfers.
$script:PathVeerR2TimeoutMinutes = 30

# ---------------------------------------------------------------------------
# Credential / config loading
# ---------------------------------------------------------------------------

function Get-PathVeerSecretsRoot {
    [CmdletBinding()]
    param()
    $localAppData = [Environment]::GetFolderPath('LocalApplicationData')
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        throw "LOCALAPPDATA could not be resolved; cannot locate the PathVeer secret store."
    }
    return (Join-Path (Join-Path $localAppData 'PathVeer') 'Secrets')
}

function Resolve-PathVeerR2CredentialPath {
    <#
    .SYNOPSIS
        Resolves the DPAPI credential file path using the documented precedence.
    .DESCRIPTION
        Precedence:
            1. explicit -CredentialPath parameter
            2. default DPAPI path  %LOCALAPPDATA%\PathVeer\Secrets\r2-credential.xml
        There is deliberately NO plaintext / environment-variable fallback.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$CredentialPath = '',
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$SecretsRoot = ''
    )
    if (-not [string]::IsNullOrWhiteSpace($CredentialPath)) {
        return [pscustomobject]@{ Path = [System.IO.Path]::GetFullPath($CredentialPath); Source = 'Explicit' }
    }
    $root = if ([string]::IsNullOrWhiteSpace($SecretsRoot)) { Get-PathVeerSecretsRoot } else { $SecretsRoot }
    return [pscustomobject]@{ Path = (Join-Path $root 'r2-credential.xml'); Source = 'DPAPI' }
}

function Resolve-PathVeerR2ConfigPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$ConfigPath = '',
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$SecretsRoot = ''
    )
    if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
        return [System.IO.Path]::GetFullPath($ConfigPath)
    }
    $root = if ([string]::IsNullOrWhiteSpace($SecretsRoot)) { Get-PathVeerSecretsRoot } else { $SecretsRoot }
    return (Join-Path $root 'r2-config.json')
}

function Get-PathVeerR2Config {
    <#
    .SYNOPSIS
        Loads the NON-SECRET R2 configuration (endpoint, bucket, public base URL).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$ConfigPath = '',
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$SecretsRoot = ''
    )
    $path = Resolve-PathVeerR2ConfigPath -ConfigPath $ConfigPath -SecretsRoot $SecretsRoot
    if (-not (Test-Path -LiteralPath $path)) {
        throw "R2 configuration not found: $path. Create it with Endpoint/Bucket/PublicBaseUrl fields."
    }
    $raw = Get-Content -LiteralPath $path -Raw
    try { $cfg = $raw | ConvertFrom-Json } catch { throw "R2 configuration at $path is not valid JSON." }

    foreach ($field in @('Endpoint', 'Bucket', 'PublicBaseUrl')) {
        if (-not $cfg.PSObject.Properties[$field] -or [string]::IsNullOrWhiteSpace([string]$cfg.$field)) {
            throw "R2 configuration missing required field: $field ($path)."
        }
    }

    $endpoint = ([string]$cfg.Endpoint).Trim()
    if ($endpoint -notmatch '^https?://') { $endpoint = "https://$endpoint" }
    $publicBase = ([string]$cfg.PublicBaseUrl).Trim().TrimEnd('/')
    if ($publicBase -notmatch '^https://') {
        throw "R2 PublicBaseUrl must be https: $publicBase"
    }

    return [pscustomobject]@{
        Endpoint      = $endpoint
        Bucket        = ([string]$cfg.Bucket).Trim()
        PublicBaseUrl = $publicBase
        ConfigPath    = $path
    }
}

function Get-PathVeerR2Credential {
    <#
    .SYNOPSIS
        Loads the DPAPI-protected R2 credential (PSCredential via Import-Clixml).
    .OUTPUTS
        PSCustomObject: Credential (PSCredential), Source, Path.
        The secret is NEVER returned as a plain string by this function.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$CredentialPath = '',
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$SecretsRoot = ''
    )
    $resolved = Resolve-PathVeerR2CredentialPath -CredentialPath $CredentialPath -SecretsRoot $SecretsRoot
    if (-not (Test-Path -LiteralPath $resolved.Path)) {
        throw "R2 credential not found at $($resolved.Path). Create a DPAPI-protected PSCredential with Export-Clixml. Plaintext credential fallback is deliberately not supported."
    }
    try {
        $obj = Import-Clixml -LiteralPath $resolved.Path
    } catch {
        throw "R2 credential at $($resolved.Path) could not be decrypted. DPAPI-protected files are bound to the creating Windows user account on this machine."
    }
    if ($null -eq $obj -or $obj -isnot [System.Management.Automation.PSCredential]) {
        throw "R2 credential at $($resolved.Path) is not a PSCredential object."
    }
    if ([string]::IsNullOrWhiteSpace($obj.UserName)) {
        throw "R2 credential at $($resolved.Path) has an empty Access Key ID."
    }
    if ($null -eq $obj.Password -or $obj.Password.Length -eq 0) {
        throw "R2 credential at $($resolved.Path) has an empty Secret Access Key."
    }
    return [pscustomobject]@{
        Credential = $obj
        Source     = $resolved.Source
        Path       = $resolved.Path
    }
}

function Get-PathVeerR2Context {
    <#
    .SYNOPSIS
        Builds a combined R2 context (config + credential) for backend operations.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$CredentialPath = '',
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$ConfigPath = '',
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$SecretsRoot = ''
    )
    $cfg  = Get-PathVeerR2Config     -ConfigPath     $ConfigPath     -SecretsRoot $SecretsRoot
    $cred = Get-PathVeerR2Credential -CredentialPath $CredentialPath -SecretsRoot $SecretsRoot
    return [pscustomobject]@{
        Endpoint         = $cfg.Endpoint
        Bucket           = $cfg.Bucket
        PublicBaseUrl    = $cfg.PublicBaseUrl
        ConfigPath       = $cfg.ConfigPath
        CredentialSource = $cred.Source
        CredentialPath   = $cred.Path
        Credential       = $cred.Credential
    }
}

function Get-PathVeerR2Diagnostics {
    <#
    .SYNOPSIS
        Redacted, safe-to-print diagnostics for an R2 context. Never contains key material.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][object]$Context)
    return [ordered]@{
        'Credential source'    = $Context.CredentialSource
        'Credential loaded'    = 'yes'
        'Bucket'               = $Context.Bucket
        'Endpoint configured'  = 'yes'
        'Public base URL'      = $Context.PublicBaseUrl
    }
}

# ---------------------------------------------------------------------------
# Secret redaction guard
# ---------------------------------------------------------------------------

function Protect-PathVeerSecretText {
    <#
    .SYNOPSIS
        Redacts any occurrence of known secret material from an arbitrary text blob.
    .DESCRIPTION
        Defence in depth: applied to every message emitted by the R2 backend
        (including caught exception text from the AWS SDK) so a secret can never
        reach stdout/stderr/audit output even if a lower layer echoes it.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)][AllowEmptyString()][AllowNull()][string]$Text,
        [Parameter(Mandatory = $false)][string[]]$Secret = @()
    )
    if ([string]::IsNullOrEmpty($Text)) { return $Text }
    $out = $Text
    foreach ($s in $Secret) {
        if (-not [string]::IsNullOrWhiteSpace($s) -and $s.Length -ge 8) {
            $out = $out.Replace($s, '***REDACTED***')
        }
    }
    return $out
}

function Invoke-PathVeerR2WithCredential {
    <#
    .SYNOPSIS
        Runs a scriptblock with transient plaintext credentials, guaranteeing the
        secret is scrubbed from any thrown exception message and from memory.
    .DESCRIPTION
        The scriptblock receives a hashtable of common AWS cmdlet parameters
        (AccessKey/SecretKey/Region/EndpointUrl/ForcePathStyleAddressing/BucketName)
        to splat. Any exception is re-thrown with secret material redacted.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Context,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )
    $net = [System.Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($Context.Credential.Password)
    try {
        $secret = [System.Runtime.InteropServices.Marshal]::PtrToStringUni($net)

        $common = @{
            AccessKey                 = $Context.Credential.UserName
            SecretKey                 = $secret
            Region                    = $script:PathVeerR2Region
            EndpointUrl               = $Context.Endpoint
            ForcePathStyleAddressing  = $true
            BucketName                = $Context.Bucket
        }

        # Release installers are >100 MiB; the SDK's default per-request timeout
        # cancels such uploads on a slow link. Give S3 operations a generous
        # bounded timeout. The AWS type is only available once AWS.Tools.S3 is
        # loaded, so this is best-effort: policy-only callers (and the unit test
        # suite, which never talks to Cloudflare) must not require the SDK.
        if ('Amazon.S3.AmazonS3Config' -as [type]) {
            $clientConfig = New-Object Amazon.S3.AmazonS3Config
            $clientConfig.ServiceURL           = $Context.Endpoint
            $clientConfig.ForcePathStyle       = $true
            $clientConfig.AuthenticationRegion = $script:PathVeerR2Region
            $clientConfig.Timeout              = [TimeSpan]::FromMinutes($script:PathVeerR2TimeoutMinutes)
            $clientConfig.MaxErrorRetry        = 5
            $common['ClientConfig'] = $clientConfig
        }
        try {
            return & $Action $common
        } catch {
            $msg = Protect-PathVeerSecretText -Text ($_.Exception.Message) -Secret @($secret)
            throw "R2 operation failed: $msg"
        } finally {
            $common['SecretKey'] = $null
            $secret = $null
        }
    } finally {
        [System.Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($net)
    }
}

# ---------------------------------------------------------------------------
# Object operations
# ---------------------------------------------------------------------------

function Get-PathVeerR2ObjectContentType {
    <#
    .SYNOPSIS
        Maps a release artifact file name to its published Content-Type.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$FileName)
    $name = [System.IO.Path]::GetFileName($FileName)
    $ext  = [System.IO.Path]::GetExtension($name)
    if ([string]::IsNullOrEmpty($ext)) { return 'application/octet-stream' }
    switch ($ext.ToLowerInvariant()) {
        '.json'   { return 'application/json' }
        '.exe'    { return 'application/octet-stream' }
        '.zip'    { return 'application/zip' }
        '.txt'    { return 'text/plain' }
        '.sha256' { return 'text/plain' }
        '.msi'    { return 'application/octet-stream' }
        default   { return 'application/octet-stream' }
    }
}

function Get-PathVeerR2CachePolicy {
    <#
    .SYNOPSIS
        Cache-Control policy for a public release key.
    .DESCRIPTION
        Versioned immutable artifacts are cached for a year and marked immutable.
        Mutable channel pointers (windows/<channel>/latest.json) use a short
        revalidating policy so a new release is discovered promptly.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Key)
    if ($Key -match '^windows/(stable|beta)/latest\.json$') {
        return 'public, max-age=60, must-revalidate'
    }
    return 'public, max-age=31536000, immutable'
}

function Get-PathVeerR2ReleaseKey {
    <#
    .SYNOPSIS
        Maps a release artifact to its canonical public object key.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $false)][string]$RuntimeIdentifier = 'win-x64'
    )
    return "windows/$Version/$RuntimeIdentifier/" + [System.IO.Path]::GetFileName($FileName)
}

function Get-PathVeerR2ChannelPointerKey {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][ValidateSet('stable', 'beta')][string]$Channel)
    return "windows/$Channel/latest.json"
}

function Get-PathVeerR2ObjectStat {
    <#
    .SYNOPSIS
        HEAD/stat a single object. Returns $null when absent.
    .NOTES
        ETag is deliberately NOT returned as a content hash. The PathVeer SHA-256
        stored in object metadata (pathveer-sha256) is advisory only; the
        authoritative check downloads bytes and hashes them.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Context,
        [Parameter(Mandatory = $true)][string]$Key
    )
    return Invoke-PathVeerR2WithCredential -Context $Context -Action {
        param($common)
        $listArgs = @{
            BucketName               = $common.BucketName
            AccessKey                = $common.AccessKey
            SecretKey                = $common.SecretKey
            Region                   = $common.Region
            EndpointUrl              = $common.EndpointUrl
            ForcePathStyleAddressing = $common.ForcePathStyleAddressing
            Key                      = $Key
        }
        $found = Get-S3Object @listArgs -ErrorAction SilentlyContinue | Where-Object { $_.Key -eq $Key } | Select-Object -First 1
        if ($null -eq $found) { return $null }
        [pscustomobject]@{
            Key          = $found.Key
            Size         = [long]$found.Size
            LastModified = $found.LastModified
        }
    }
}

function Get-PathVeerR2ObjectList {
    <#
    .SYNOPSIS
        Lists objects under a prefix (paginated by the cmdlet).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Context,
        [Parameter(Mandatory = $false)][string]$Prefix = 'windows/'
    )
    return Invoke-PathVeerR2WithCredential -Context $Context -Action {
        param($common)
        $args2 = @{
            BucketName               = $common.BucketName
            AccessKey                = $common.AccessKey
            SecretKey                = $common.SecretKey
            Region                   = $common.Region
            EndpointUrl              = $common.EndpointUrl
            ForcePathStyleAddressing = $common.ForcePathStyleAddressing
            KeyPrefix                = $Prefix
        }
        $items = @(Get-S3Object @args2 -ErrorAction Stop)
        return @($items | ForEach-Object {
            [pscustomobject]@{
                Key          = $_.Key
                Size         = [long]$_.Size
                LastModified = $_.LastModified
            }
        })
    }
}

function Write-PathVeerR2Object {
    <#
    .SYNOPSIS
        Uploads a single object with content type, cache policy and PathVeer metadata.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Context,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$File,
        [Parameter(Mandatory = $true)][string]$Sha256,
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$Version = '',
        [Parameter(Mandatory = $false)][AllowEmptyString()][string]$Channel = ''
    )
    if (-not (Test-Path -LiteralPath $File)) { throw "Upload source not found: $File" }
    $contentType = Get-PathVeerR2ObjectContentType -FileName $Key
    $cache       = Get-PathVeerR2CachePolicy -Key $Key
    $meta        = @{ 'pathveer-sha256' = $Sha256.ToLowerInvariant() }
    if ($Version) { $meta['pathveer-version'] = $Version }
    if ($Channel) { $meta['pathveer-channel'] = $Channel }

    Invoke-PathVeerR2WithCredential -Context $Context -Action {
        param($common)
        $putArgs = @{
            BucketName                       = $common.BucketName
            AccessKey                        = $common.AccessKey
            SecretKey                        = $common.SecretKey
            Region                           = $common.Region
            EndpointUrl                      = $common.EndpointUrl
            ForcePathStyleAddressing         = $common.ForcePathStyleAddressing
            Key                              = $Key
            File                             = $File
            ContentType                      = $contentType
            Metadata                         = $meta
            HeaderCollection                 = @{ 'Cache-Control' = $cache }
            DisableDefaultChecksumValidation = $true
            # Cloudflare R2 does not implement STREAMING-AWS4-HMAC-SHA256-PAYLOAD,
            # which newer AWS SDKs use by default. Payload signing is disabled so
            # the request uses a plain SigV4 signature over HTTPS. Integrity is
            # still fully enforced by PathVeer's own SHA-256 read-back verification.
            DisablePayloadSigning            = $true
            Force                            = $true
        }
        Write-S3Object @putArgs -ErrorAction Stop | Out-Null
        return $true
    } | Out-Null

    return [pscustomobject]@{ Key = $Key; ContentType = $contentType; CacheControl = $cache; Sha256 = $Sha256.ToLowerInvariant() }
}

function Read-PathVeerR2ObjectToFile {
    <#
    .SYNOPSIS
        Downloads an object to a local file (used for authoritative hash verification).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Context,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    $dir = Split-Path -Parent $Destination
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Invoke-PathVeerR2WithCredential -Context $Context -Action {
        param($common)
        $getArgs = @{
            BucketName               = $common.BucketName
            AccessKey                = $common.AccessKey
            SecretKey                = $common.SecretKey
            Region                   = $common.Region
            EndpointUrl              = $common.EndpointUrl
            ForcePathStyleAddressing = $common.ForcePathStyleAddressing
            Key                      = $Key
            File                     = $Destination
        }
        Read-S3Object @getArgs -ErrorAction Stop | Out-Null
        return $true
    } | Out-Null
    return $Destination
}

function Remove-PathVeerR2Object {
    <#
    .SYNOPSIS
        Deletes a single object. Only ever called from an explicit retention operation.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Context,
        [Parameter(Mandatory = $true)][string]$Key
    )
    Invoke-PathVeerR2WithCredential -Context $Context -Action {
        param($common)
        $delArgs = @{
            BucketName               = $common.BucketName
            AccessKey                = $common.AccessKey
            SecretKey                = $common.SecretKey
            Region                   = $common.Region
            EndpointUrl              = $common.EndpointUrl
            ForcePathStyleAddressing = $common.ForcePathStyleAddressing
            Key                      = $Key
            Force                    = $true
        }
        Remove-S3Object @delArgs -ErrorAction Stop | Out-Null
        return $true
    } | Out-Null
    return $Key
}

# ---------------------------------------------------------------------------
# Immutability decision (pure — unit testable without R2)
# ---------------------------------------------------------------------------

function Get-PathVeerR2ImmutableAction {
    <#
    .SYNOPSIS
        Decides what to do when writing an immutable versioned object.
    .DESCRIPTION
        Pure policy function (no network):
            absent                         -> Upload
            present + same SHA-256         -> NoOp
            present + different SHA-256    -> Reject (hard fail)
        RemoteSha256 must come from an authoritative PathVeer hash (downloaded
        bytes or the pathveer-sha256 metadata verified by download) — NEVER from
        an S3 ETag, which is not a content hash for multipart objects.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$LocalSha256,
        [Parameter(Mandatory = $false)][AllowNull()][AllowEmptyString()][string]$RemoteSha256 = $null,
        [Parameter(Mandatory = $false)][bool]$RemoteExists = $false
    )
    if (-not $RemoteExists) { return 'Upload' }
    if ([string]::IsNullOrWhiteSpace($RemoteSha256)) { return 'Verify' }
    if ($RemoteSha256.ToLowerInvariant() -eq $LocalSha256.ToLowerInvariant()) { return 'NoOp' }
    return 'Reject'
}

# ---------------------------------------------------------------------------
# Storage budget
# ---------------------------------------------------------------------------

$script:PathVeerStorageBudgetBytes = 8GB   # 8 GiB conservative app-level threshold

function Get-PathVeerStorageBudgetBytes { return [long]$script:PathVeerStorageBudgetBytes }

function Test-PathVeerStorageBudget {
    <#
    .SYNOPSIS
        Evaluates the 8 GiB conservative application-level storage safety threshold.
    .OUTPUTS
        CurrentBytes, NewBytes, ProjectedBytes, BudgetBytes, Allowed (bool).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][long]$CurrentBytes,
        [Parameter(Mandatory = $true)][long]$NewBytes,
        [Parameter(Mandatory = $false)][long]$BudgetBytes = 0
    )
    if ($BudgetBytes -le 0) { $BudgetBytes = Get-PathVeerStorageBudgetBytes }
    $projected = $CurrentBytes + $NewBytes
    return [pscustomobject]@{
        CurrentBytes   = $CurrentBytes
        NewBytes       = $NewBytes
        ProjectedBytes = $projected
        BudgetBytes    = $BudgetBytes
        Allowed        = ($projected -le $BudgetBytes)
    }
}

# ---------------------------------------------------------------------------
# Semantic retention (pure — unit testable without R2)
# ---------------------------------------------------------------------------

function ConvertTo-PathVeerReleaseSet {
    <#
    .SYNOPSIS
        Groups a flat object listing into coherent release version object sets.
    .DESCRIPTION
        Only keys under windows/<version>/<rid>/ are considered releases. Channel
        pointers (windows/<channel>/latest.json) are never part of a release set.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Objects)
    $groups = @{}
    foreach ($o in $Objects) {
        $k = [string]$o.Key
        if ($k -match '^windows/(?<ver>[^/]+)/(?<rid>[^/]+)/(?<file>.+)$') {
            $ver = $Matches['ver']
            if ($ver -in @('stable', 'beta')) { continue }   # channel pointer namespace
            if (-not $groups.ContainsKey($ver)) {
                $groups[$ver] = [pscustomobject]@{
                    Version = $ver
                    Keys    = New-Object System.Collections.Generic.List[string]
                    Bytes   = [long]0
                }
            }
            $groups[$ver].Keys.Add($k)
            $groups[$ver].Bytes += [long]$o.Size
        }
    }
    return @($groups.Values)
}

function Get-PathVeerReleaseChannelFromVersion {
    <#
    .SYNOPSIS
        Classifies a semantic version string into a release channel.
    .DESCRIPTION
        A version with a pre-release suffix (-beta.N, -rc.N, ...) is beta/RC.
        A plain x.y.z version is stable.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Version)
    if ($Version -match '-') { return 'beta' }
    return 'stable'
}

function Get-PathVeerVersionSortKey {
    <#
    .SYNOPSIS
        Produces a lexicographically sortable key for a semantic version.
    .DESCRIPTION
        Format: NNNNN.NNNNN.NNNNN.<pre> where a plain release sorts ABOVE any
        pre-release of the same core (release marker '~' > '!' + pre-release).
        Numeric pre-release identifiers are zero-padded so beta.10 > beta.9.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Version)
    $core, $pre = ($Version -split '-', 2)
    $nums = @($core -split '\.' | ForEach-Object { [int]($_ -replace '\D', '0') })
    while ($nums.Count -lt 3) { $nums += 0 }
    $key = ('{0:D5}.{1:D5}.{2:D5}.' -f $nums[0], $nums[1], $nums[2])
    if ([string]::IsNullOrEmpty($pre)) { return $key + '~' }
    $preKey = (($pre -split '\.') | ForEach-Object {
        if ($_ -match '^\d+$') { '{0:D10}' -f [int]$_ } else { $_ }
    }) -join '.'
    return $key + '!' + $preKey
}

function Compare-PathVeerVersion {
    <#
    .SYNOPSIS
        SemVer-ish comparison: numeric core descending, release > pre-release.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$A, [Parameter(Mandatory = $true)][string]$B)
    function Split-V([string]$v) {
        $core, $pre = ($v -split '-', 2)
        $nums = @($core -split '\.' | ForEach-Object { [int]($_ -replace '\D', '0') })
        while ($nums.Count -lt 3) { $nums += 0 }
        return @{ Nums = $nums; Pre = $pre }
    }
    $x = Split-V $A; $y = Split-V $B
    for ($i = 0; $i -lt 3; $i++) {
        if ($x.Nums[$i] -ne $y.Nums[$i]) { return [int]($x.Nums[$i]).CompareTo([int]$y.Nums[$i]) }
    }
    $xp = [string]$x.Pre; $yp = [string]$y.Pre
    if ([string]::IsNullOrEmpty($xp) -and [string]::IsNullOrEmpty($yp)) { return 0 }
    if ([string]::IsNullOrEmpty($xp)) { return 1 }
    if ([string]::IsNullOrEmpty($yp)) { return -1 }
    return [string]::CompareOrdinal($xp, $yp)
}

function Get-PathVeerRetentionPlan {
    <#
    .SYNOPSIS
        Computes a semantic, release-level retention plan.
    .DESCRIPTION
        Policy:
            stable channel : keep the newest 15 stable releases
            beta/RC        : keep the newest 5 beta/RC releases
        Protected releases are NEVER eligible, regardless of count:
            * the version currently targeted by windows/stable/latest.json
            * the version currently targeted by windows/beta/latest.json
            * the supported upgrade-floor release, when hosted
            * any explicitly pinned version
        Retention operates at the release version level: a release is only ever
        removed as a coherent object set.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$ReleaseSets,
        [Parameter(Mandatory = $false)][AllowEmptyString()][AllowNull()][string]$StableLatestVersion = $null,
        [Parameter(Mandatory = $false)][AllowEmptyString()][AllowNull()][string]$BetaLatestVersion = $null,
        [Parameter(Mandatory = $false)][AllowEmptyString()][AllowNull()][string]$UpgradeFloorVersion = $null,
        [Parameter(Mandatory = $false)][string[]]$PinnedVersions = @(),
        [Parameter(Mandatory = $false)][int]$KeepStable = 15,
        [Parameter(Mandatory = $false)][int]$KeepBeta = 5
    )
    $protected = New-Object System.Collections.Generic.HashSet[string]
    foreach ($p in @($StableLatestVersion, $BetaLatestVersion, $UpgradeFloorVersion) + @($PinnedVersions)) {
        if (-not [string]::IsNullOrWhiteSpace($p)) { [void]$protected.Add($p) }
    }

    $rows = @()
    foreach ($channel in @('stable', 'beta')) {
        $keep = if ($channel -eq 'stable') { $KeepStable } else { $KeepBeta }
        $inChannel = @($ReleaseSets | Where-Object { (Get-PathVeerReleaseChannelFromVersion $_.Version) -eq $channel })
        # Newest first, using a total-order sort key derived from the semantic version.
        $sorted = @($inChannel | Sort-Object -Descending -Property @{ Expression = { Get-PathVeerVersionSortKey -Version $_.Version } })
        $rank = 0
        foreach ($set in $sorted) {
            $rank++
            $isProtected = $protected.Contains($set.Version)
            $isLatest    = ($set.Version -eq $StableLatestVersion) -or ($set.Version -eq $BetaLatestVersion)
            $eligible    = (-not $isProtected) -and ($rank -gt $keep)
            $rows += [pscustomobject]@{
                Version        = $set.Version
                Channel        = $channel
                Bytes          = [long]$set.Bytes
                Rank           = $rank
                IsLatestTarget = $isLatest
                Protected      = $isProtected
                Eligible       = $eligible
                Keys           = @($set.Keys)
            }
        }
    }
    return @($rows)
}

Export-ModuleMember -Function `
    Get-PathVeerSecretsRoot, Resolve-PathVeerR2CredentialPath, Resolve-PathVeerR2ConfigPath, `
    Get-PathVeerR2Config, Get-PathVeerR2Credential, Get-PathVeerR2Context, Get-PathVeerR2Diagnostics, `
    Protect-PathVeerSecretText, Invoke-PathVeerR2WithCredential, `
    Get-PathVeerR2ObjectContentType, Get-PathVeerR2CachePolicy, Get-PathVeerR2ReleaseKey, `
    Get-PathVeerR2ChannelPointerKey, Get-PathVeerR2ObjectStat, Get-PathVeerR2ObjectList, `
    Write-PathVeerR2Object, Read-PathVeerR2ObjectToFile, Remove-PathVeerR2Object, `
    Get-PathVeerR2ImmutableAction, Get-PathVeerStorageBudgetBytes, Test-PathVeerStorageBudget, `
    ConvertTo-PathVeerReleaseSet, Get-PathVeerReleaseChannelFromVersion, Compare-PathVeerVersion, `
    Get-PathVeerVersionSortKey, Get-PathVeerRetentionPlan
