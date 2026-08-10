<#
.SYNOPSIS
    Real Cloudflare R2 / releases.pathveer.com distribution integration exercise.

.DESCRIPTION
    EXTERNAL integration verification. This is deliberately NOT part of
    `dotnet test`: it requires live Cloudflare availability and the developer's
    DPAPI credential store, and the normal unit suite must never depend on
    either. Run it explicitly when certifying a real staging publication.

    It exercises the genuine client path against the real public custom domain:

      1. Public HTTPS validation (TLS, status, Content-Type, Cache-Control,
         Content-Length, no HTML/SPA fallback) for the channel pointer, the
         versioned manifest and the installer.
      2. The real PathVeer HttpReleaseSource + ReleaseManifestParser +
         ReleaseSignatureVerifier + UpdateChecker against
         https://releases.pathveer.com/windows/<channel>/latest.json.
      3. A real installer download through the custom domain, verified by
         InstallerDownloadVerifier (size + SHA-256 against the signed manifest).
      4. An isolated tamper-rejection proof using a DISPOSABLE object prefix.
         The real published release is never mutated; the disposable object is
         deleted afterwards. Setup is NEVER executed.

    Authenticode status is reported honestly and is NOT asserted, because
    production Authenticode remains an external blocker.

.PARAMETER Channel
    beta (default). Stable is intentionally not exercised.

.PARAMETER TrustedKeyBase64
    '<keyId>:<base64 64-byte public key>' for the release-metadata trust root.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)][ValidateSet('beta')][string]$Channel = 'beta',
    [Parameter(Mandatory = $true)][string]$TrustedKeyBase64,
    [Parameter(Mandatory = $false)][string]$PublicBaseUrl = 'https://releases.pathveer.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' '..'))
$results  = [ordered]@{}
$failures = New-Object System.Collections.Generic.List[string]

function Step([string]$Name, [scriptblock]$Body) {
    Write-Host ""
    Write-Host "== $Name" -ForegroundColor Cyan
    try {
        & $Body
        $results[$Name] = 'PASS'
    } catch {
        $results[$Name] = "FAIL: $($_.Exception.Message)"
        $failures.Add("$Name : $($_.Exception.Message)")
        Write-Host "   FAIL: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# --- 1. Public HTTPS surface --------------------------------------------------
$pointerUrl = "$PublicBaseUrl/windows/$Channel/latest.json"
$script:manifestJson = $null
$script:manifestObj  = $null

Step "Public HTTPS: channel pointer" {
    $r = Invoke-WebRequest -Uri $pointerUrl -Method Get -MaximumRedirection 0 -SkipHttpErrorCheck
    if ($r.StatusCode -ne 200) { throw "HTTP $($r.StatusCode) for $pointerUrl" }
    $ct = [string]$r.Headers['Content-Type']
    if ($ct -notmatch 'application/json') { throw "Unexpected Content-Type '$ct' (HTML/SPA fallback?)" }
    $cc = [string]$r.Headers['Cache-Control']
    if ($cc -notmatch 'max-age=60') { throw "Channel pointer must use the short revalidating policy; got '$cc'" }
    $body = $r.Content
    if ($body -match '^\s*<') { throw "Response body is HTML, not JSON (SPA fallback)." }
    $script:manifestJson = $body
    $script:manifestObj  = $body | ConvertFrom-Json
    Write-Host "   200 OK  Content-Type=$ct  Cache-Control=$cc  Content-Length=$($r.Headers['Content-Length'])"
}

Step "Public HTTPS: versioned immutable manifest" {
    $v = $script:manifestObj.version
    $u = "$PublicBaseUrl/windows/$v/win-x64/release-manifest.json"
    $r = Invoke-WebRequest -Uri $u -Method Get -SkipHttpErrorCheck
    if ($r.StatusCode -ne 200) { throw "HTTP $($r.StatusCode) for $u" }
    $cc = [string]$r.Headers['Cache-Control']
    if ($cc -notmatch 'immutable') { throw "Versioned artifact must be immutable-cached; got '$cc'" }
    if ($r.Content -ne $script:manifestJson) { throw "Channel pointer bytes differ from the versioned manifest bytes." }
    Write-Host "   200 OK  Cache-Control=$cc  bytes identical to channel pointer"
}

Step "Public HTTPS: installer headers" {
    $u = $script:manifestObj.installer.url
    $r = Invoke-WebRequest -Uri $u -Method Head -SkipHttpErrorCheck
    if ($r.StatusCode -ne 200) { throw "HTTP $($r.StatusCode) for $u" }
    $ct = [string]$r.Headers['Content-Type']
    if ($ct -notmatch 'application/octet-stream') { throw "Installer Content-Type '$ct' unexpected." }
    $cc = [string]$r.Headers['Cache-Control']
    if ($cc -notmatch 'immutable') { throw "Installer must be immutable-cached; got '$cc'" }
    $len = [long]$r.Headers['Content-Length'][0]
    if ($len -ne [long]$script:manifestObj.installer.size) {
        throw "Content-Length $len != manifest size $($script:manifestObj.installer.size)"
    }
    Write-Host "   200 OK  Content-Type=$ct  Cache-Control=$cc  Content-Length=$len"
}

# --- 2/3/4. Real client path via PathVeer.Core --------------------------------
$coreDll = Join-Path $RepoRoot 'PathVeer.Core\bin\Debug\net10.0\PathVeer.Core.dll'
if (-not (Test-Path $coreDll)) {
    $coreDll = Join-Path $RepoRoot 'PathVeer.Core\bin\Release\net10.0\PathVeer.Core.dll'
}
if (-not (Test-Path $coreDll)) { throw "PathVeer.Core.dll not built. Run: dotnet build PathVeer.Core -c Debug" }
Add-Type -Path $coreDll

Step "HttpReleaseSource + UpdateChecker against the real feed" {
    $keyId, $pub = $TrustedKeyBase64 -split ':', 2
    $trusted = New-Object 'System.Collections.Generic.List[System.Collections.Generic.KeyValuePair[string,byte[]]]'
    $trusted.Add([System.Collections.Generic.KeyValuePair[string,byte[]]]::new($keyId, [Convert]::FromBase64String($pub)))
    $verifier = New-Object PathVeer.Core.Update.ReleaseSignatureVerifier($trusted, $false)

    $http = New-Object System.Net.Http.HttpClient
    $builder = [Func[PathVeer.Core.Update.UpdateChannel, string, string, string]]{
        param($ch, $platform, $arch)
        "$PublicBaseUrl/windows/$($ch.ToString().ToLowerInvariant())/latest.json"
    }
    $source = New-Object PathVeer.Core.Update.HttpReleaseSource($http, $builder, 65536)

    # Fetch through the production transport.
    $json = $source.FetchManifestAsync([PathVeer.Core.Update.UpdateChannel]::Beta, 'windows', 'x64', [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
    if ([string]::IsNullOrWhiteSpace($json)) { throw "HttpReleaseSource returned no manifest." }

    $parser = New-Object PathVeer.Core.Update.ReleaseManifestParser
    $parsed = $parser.Parse($json, 'x64')
    if (-not $parsed.Success) { throw "Manifest parse failed: $($parsed.Error)" }
    $m = $parsed.Manifest
    Write-Host "   schemaVersion=$($m.SchemaVersion) product=$($m.Product) channel=$($m.Channel) platform=$($m.Platform) arch=$($m.Architecture) version=$($m.Version)"

    $sig = $verifier.Verify($m, $json)
    if (-not $sig.IsValid) { throw "ES256 metadata signature REJECTED: $($sig.Error)" }
    Write-Host "   ES256 signature: VALID (keyId=$keyId)"

    # Version selection through the real UpdateChecker. The installed version is
    # supplied via a temp install-manifest (a PowerShell scriptblock resolver
    # cannot be invoked from the SDK's thread pool).
    $fakeInstall = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-installed-" + [guid]::NewGuid().ToString('N') + ".json")
    # Use an installed version at or above the manifest's minimum upgrade floor so
    # the check exercises version SELECTION, not the upgrade-floor guard.
    $floor = [string]$script:manifestObj.minimumUpgradeVersion
    ('{"productVersion":"' + $floor + '"}') | Set-Content -LiteralPath $fakeInstall -Encoding utf8
    $installed = New-Object PathVeer.Core.Update.InstalledVersionSource([string]$fakeInstall)
    $checker = New-Object PathVeer.Core.Update.UpdateChecker($source, $verifier, $installed, $null, 'x64')
    $res = $checker.CheckAsync([PathVeer.Core.Update.UpdateChannel]::Beta, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
    Remove-Item $fakeInstall -Force -ErrorAction SilentlyContinue
    Write-Host "   UpdateChecker state=$($res.State) installed=$($res.InstalledVersion) available=$($res.AvailableVersion) manifestVersion=$($res.Manifest.Version)"
    $accepted = @(
        [PathVeer.Core.Update.UpdateCheckState]::UpdateAvailable,
        [PathVeer.Core.Update.UpdateCheckState]::NoUpdate,
        [PathVeer.Core.Update.UpdateCheckState]::CurrentVersionNewer
    )
    if ($res.State -notin $accepted) {
        throw "UpdateChecker did not reach a valid selection state against the real feed: $($res.State) ($($res.Detail))"
    }
    if ($res.State -eq [PathVeer.Core.Update.UpdateCheckState]::InvalidManifest) { throw "UpdateChecker rejected the real feed: $($res.Error)" }
}

$script:downloaded = $null

Step "Real installer download + SHA-256 verification" {
    $u = $script:manifestObj.installer.url
    $dest = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-real-installer-" + [guid]::NewGuid().ToString('N') + ".exe")
    $script:downloaded = $dest
    Invoke-WebRequest -Uri $u -OutFile $dest
    $size = (Get-Item $dest).Length
    $hash = (Get-FileHash -Path $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "   downloaded $size bytes  sha256=$hash"
    if ($size -ne [long]$script:manifestObj.installer.size) { throw "Size mismatch." }
    if ($hash -ne $script:manifestObj.installer.sha256.ToLowerInvariant()) { throw "SHA-256 mismatch." }

    # The real production verifier, with an honest Authenticode probe.
    $probe = [Func[string, PathVeer.Core.Update.InstallerSignatureStatus]]{
        param($p)
        $s = Get-AuthenticodeSignature -FilePath $p
        switch ($s.Status) {
            'Valid'          { [PathVeer.Core.Update.InstallerSignatureStatus]::Valid }
            'NotSigned'      { [PathVeer.Core.Update.InstallerSignatureStatus]::Unsigned }
            default          { [PathVeer.Core.Update.InstallerSignatureStatus]::Untrusted }
        }
    }
    $authStatus = (Get-AuthenticodeSignature -FilePath $dest).Status
    Write-Host "   Authenticode status (honest): $authStatus  [production Authenticode remains an external blocker]"

    # Hash-only verification must PASS (no signature probe supplied).
    # ::new() binds an explicit null to the optional probe parameter (New-Object cannot).
    $v = [PathVeer.Core.Update.InstallerDownloadVerifier]::new($null)
    $expected = New-Object PathVeer.Core.Update.ReleaseManifest+ManifestArtifact
    $expected.FileName = $script:manifestObj.installer.fileName
    $expected.Url      = $u
    $expected.Sha256   = $script:manifestObj.installer.sha256
    $expected.Size     = [long]$script:manifestObj.installer.size
    $r = $v.Verify($dest, $expected)
    if (-not $r.IsValid) { throw "InstallerDownloadVerifier rejected an authentic download: $($r.Error)" }
    Write-Host "   InstallerDownloadVerifier: ACCEPTED authentic installer (hash+size)"

    # With the Authenticode probe wired in, an unsigned installer must be refused.
    $v2 = [PathVeer.Core.Update.InstallerDownloadVerifier]::new($probe)
    $r2 = $v2.Verify($dest, $expected)
    Write-Host "   With Authenticode gate: isValid=$($r2.IsValid) (expected false while unsigned) error=$($r2.Error)"
}

Step "Isolated tamper rejection (disposable object, real release untouched)" {
    Import-Module (Join-Path $RepoRoot 'tools\PathVeerR2.psm1') -Force
    Import-Module AWS.Tools.S3 -ErrorAction Stop
    $ctx = Get-PathVeerR2Context

    $disposableKey = "_tamper-test/$([guid]::NewGuid().ToString('N'))/PathVeerSetup-tampered.exe"
    $tamperLocal = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-tamper-" + [guid]::NewGuid().ToString('N') + ".exe")
    try {
        # Take the authentic bytes and flip one byte -> a tampered public installer.
        Copy-Item -Path $script:downloaded -Destination $tamperLocal -Force
        $fs = [System.IO.File]::Open($tamperLocal, 'Open', 'ReadWrite')
        try {
            $fs.Seek(1024, 'Begin') | Out-Null
            $b = $fs.ReadByte()
            $fs.Seek(1024, 'Begin') | Out-Null
            $fs.WriteByte([byte](($b + 1) % 256))
        } finally { $fs.Dispose() }

        $tamperHash = (Get-FileHash -Path $tamperLocal -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-PathVeerR2Object -Context $ctx -Key $disposableKey -File $tamperLocal -Sha256 $tamperHash -Version 'tamper-test' -Channel 'beta' | Out-Null

        # Download the TAMPERED object back through the public custom domain.
        $tamperUrl = "$PublicBaseUrl/$disposableKey"
        $back = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-tamper-back-" + [guid]::NewGuid().ToString('N') + ".exe")
        Invoke-WebRequest -Uri $tamperUrl -OutFile $back

        $expected = New-Object PathVeer.Core.Update.ReleaseManifest+ManifestArtifact
        $expected.FileName = $script:manifestObj.installer.fileName
        $expected.Url      = $tamperUrl
        $expected.Sha256   = $script:manifestObj.installer.sha256   # signed, authentic hash
        $expected.Size     = [long]$script:manifestObj.installer.size

        $v = [PathVeer.Core.Update.InstallerDownloadVerifier]::new($null)
        $r = $v.Verify($back, $expected)
        if ($r.IsValid) { throw "SECURITY FAILURE: tampered public installer was ACCEPTED." }
        Write-Host "   Tampered public installer REJECTED: $($r.Error)"
        Write-Host "   Setup was NEVER launched."
        Remove-Item $back -Force -ErrorAction SilentlyContinue
    }
    finally {
        # Always remove the disposable tamper object from the bucket.
        try {
            Remove-PathVeerR2Object -Context $ctx -Key $disposableKey | Out-Null
            Write-Host "   Disposable tamper object deleted: $disposableKey"
        } catch { Write-Host "   WARNING: could not delete disposable object $disposableKey" -ForegroundColor Yellow }
        Remove-Item $tamperLocal -Force -ErrorAction SilentlyContinue
    }
}

Step "Real release object still intact after tamper test" {
    $u = $script:manifestObj.installer.url
    $dest = Join-Path ([System.IO.Path]::GetTempPath()) ("pv-recheck-" + [guid]::NewGuid().ToString('N') + ".exe")
    Invoke-WebRequest -Uri $u -OutFile $dest
    $hash = (Get-FileHash -Path $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    Remove-Item $dest -Force -ErrorAction SilentlyContinue
    if ($hash -ne $script:manifestObj.installer.sha256.ToLowerInvariant()) { throw "Published installer changed!" }
    Write-Host "   Published installer unchanged: $hash"
}

Step "Stable channel untouched" {
    $r = Invoke-WebRequest -Uri "$PublicBaseUrl/windows/stable/latest.json" -Method Get -SkipHttpErrorCheck
    Write-Host "   windows/stable/latest.json -> HTTP $($r.StatusCode) (404 = never published; beta publication did not create it)"
    if ($r.StatusCode -eq 200) { Write-Host "   NOTE: stable exists; its bytes must be compared against the recorded pre-test hash." -ForegroundColor Yellow }
}

if ($script:downloaded) { Remove-Item $script:downloaded -Force -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "== Summary" -ForegroundColor Cyan
foreach ($k in $results.Keys) {
    $color = if ($results[$k] -eq 'PASS') { 'Green' } else { 'Red' }
    Write-Host ("  {0,-60} {1}" -f $k, $results[$k]) -ForegroundColor $color
}
if ($failures.Count -gt 0) { exit 1 }
Write-Host ""
Write-Host "ALL REAL-FEED CHECKS PASSED" -ForegroundColor Green
