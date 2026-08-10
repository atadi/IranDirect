<# .SYNOPSIS
    Phase 37.6 — verify a frozen release artifact against recorded certification hashes.

.DESCRIPTION
    Recomputes SHA-256 of the installer and package from a release bundle and
    compares against expected values (recorded in the certification matrix).
    Used by GATE-9 (update download) and GATE-12 (immutable publication).
    Read-only; does not modify the bundle.

.PARAMETER ReleaseDirectory
    Path to the release bundle directory (contains PathVeerSetup-*.exe and PathVeer-*.zip).
.PARAMETER ExpectedInstallerSha256
    Expected installer SHA-256 (lowercase).
.PARAMETER ExpectedPackageSha256
    Expected package SHA-256 (lowercase).
.PARAMETER ManifestPath
    Optional manifest to additionally verify against the installer hash field.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ReleaseDirectory,
    [string]$ExpectedInstallerSha256 = '',
    [string]$ExpectedPackageSha256 = '',
    [string]$ManifestPath = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$setup = Get-ChildItem $ReleaseDirectory -Filter PathVeerSetup-*.exe -ErrorAction SilentlyContinue | Select-Object -First 1
$zip   = Get-ChildItem $ReleaseDirectory -Filter PathVeer-*.zip -ErrorAction SilentlyContinue | Select-Object -First 1

$result = [ordered]@{
    capturedUtc          = (Get-Date).ToUniversalTime().ToString('o')
    installerPresent     = ($null -ne $setup)
    packagePresent       = ($null -ne $zip)
    installerSha256      = if ($setup) { (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
    packageSha256        = if ($zip)   { (Get-FileHash $zip   -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
    installerMatch       = $false
    packageMatch         = $false
}

if ($ExpectedInstallerSha256) { $result.installerMatch = ($result.installerSha256 -eq $ExpectedInstallerSha256.ToLowerInvariant()) }
if ($ExpectedPackageSha256)   { $result.packageMatch   = ($result.packageSha256   -eq $ExpectedPackageSha256.ToLowerInvariant()) }

if ($ManifestPath -and (Test-Path $ManifestPath)) {
    $jm = [System.Text.Json.Nodes.JsonNode]::Parse((Get-Content -Raw $ManifestPath))
    $manifestInstallerSha = ([string]$jm['installer']['sha256']).ToLowerInvariant()
    $result.manifestInstallerSha256 = $manifestInstallerSha
    $result.manifestInstallerMatch  = ($result.installerSha256 -eq $manifestInstallerSha)
}

$result | ConvertTo-Json -Depth 4
$allOk = $result.installerPresent -and $result.packagePresent -and
         ($ExpectedInstallerSha256 -eq '' -or $result.installerMatch) -and
         ($ExpectedPackageSha256   -eq '' -or $result.packageMatch)
Write-Host ("Artifact verification: " + $(if($allOk){'PASS'}else{'CHECK FAILED'})) -ForegroundColor $(if($allOk){'Green'}else{'Red'})
exit $(if($allOk){0}else{1})
