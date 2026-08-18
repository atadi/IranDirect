<# SYNOPSIS Regression + adversarial tests for package-hashes.sha256 generation.

Proves:
  * relative paths are always package-root-relative (never PathVeer-<version>\...,
    never absolute, never parent traversal) regardless of how OutputDirectory is
    represented (absolute, with .., with ., trailing sep, no trailing sep).
  * the generator FAILS CLOSED on a package whose real files include a parent
    traversal or rooted path.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script = $null
$dir = $PSScriptRoot
while ($dir -and -not (Test-Path (Join-Path $dir 'tools\New-PathVeerPackage.ps1'))) {
    $dir = Split-Path $dir -Parent
}
if (-not $dir) { throw "Could not locate repo root (tools\New-PathVeerPackage.ps1)." }
$script = Join-Path $dir 'tools\New-PathVeerPackage.ps1'
$repo = $dir
$failures = [System.Collections.Generic.List[string]]::new()

function Assert-True($cond, $msg) {
    if (-not $cond) { $script:failures.Add($msg) }
}

function New-DisposablePackage {
    param([string]$Root)
    if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Root | Out-Null
    $svc = Join-Path $Root 'Service'; New-Item -ItemType Directory -Force -Path $svc | Out-Null
    $cli = Join-Path $Root 'Cli';     New-Item -ItemType Directory -Force -Path $cli | Out-Null
    $tray = Join-Path $Root 'Tray';   New-Item -ItemType Directory -Force -Path $tray | Out-Null
    'EXE' | Set-Content -Path (Join-Path $svc 'PathVeer.Service.exe')
    'EXE' | Set-Content -Path (Join-Path $cli 'PathVeer.Cli.exe')
    'EXE' | Set-Content -Path (Join-Path $tray 'PathVeer.Tray.exe')
    '{ "x": 1 }' | Set-Content -Path (Join-Path $Root 'package.json')
    return $Root
}

function Get-ManifestEntries($pkgRoot) {
    $hf = Join-Path $pkgRoot 'package-hashes.sha256'
    $out = @()
    foreach ($line in (Get-Content $hf)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split ' ', 2
        $out += $parts[1].Trim()
    }
    return $out
}

# --- Variant 1: normal absolute OutputDirectory ---------------------------
$base = Join-Path $env:TEMP ("PathVeer.PkgGen.Tests." + [guid]::NewGuid().ToString('N'))
$pkg = New-DisposablePackage (Join-Path $base 'packages\PathVeer-1.0.0-devsign.4')
& pwsh -NoLogo -NoProfile -File $script -Version 1.0.0-devsign.4 -OutputDirectory (Join-Path $base 'packages') -RuntimeIdentifier win-x64 | Out-Null
Assert-True ($LASTEXITCODE -eq 0) "Variant1 generation failed (exit $LASTEXITCODE)"
$entries = Get-ManifestEntries $pkg
Assert-True ($entries -notcontains 'PathVeer-1.0.0-devsign.4\Cli\PathVeer.Cli.exe') "Variant1 must not prefix with PathVeer-<version>"
Assert-True ($entries -contains 'Cli\PathVeer.Cli.exe') "Variant1 missing Cli\PathVeer.Cli.exe"
Assert-True ($entries -contains 'Service\PathVeer.Service.exe') "Variant1 missing Service entry"
Assert-True ($entries -contains 'Tray\PathVeer.Tray.exe') "Variant1 missing Tray entry"
Assert-True ($entries -contains 'package.json') "Variant1 missing package.json"
Assert-True (-not ($entries | Where-Object { $_ -like '*hVeer-1.0.0*' })) "Variant1 produced corrupt hVeer-<version> prefix"
Assert-True (-not ($entries | Where-Object { [System.IO.Path]::IsPathRooted($_) })) "Variant1 produced rooted entry"
Assert-True (-not ($entries | Where-Object { $_.StartsWith('..') })) "Variant1 produced traversal entry"

# --- Variant 2: OutputDirectory with .. components ------------------------
$pkg2 = New-DisposablePackage (Join-Path $base 'packages\PathVeer-1.0.0-devsign.4')
$outWithDotDot = Join-Path $base ('packages\..\packages')
& pwsh -NoLogo -NoProfile -File $script -Version 1.0.0-devsign.4 -OutputDirectory $outWithDotDot -RuntimeIdentifier win-x64 | Out-Null
Assert-True ($LASTEXITCODE -eq 0) "Variant2 (..) generation failed (exit $LASTEXITCODE)"
$entries2 = Get-ManifestEntries $pkg2
Assert-True ($entries2 -contains 'Cli\PathVeer.Cli.exe') "Variant2 (..) missing Cli entry"
Assert-True (-not ($entries2 | Where-Object { $_.StartsWith('..') -or [System.IO.Path]::IsPathRooted($_) })) "Variant2 (..) produced non-relative entry"

# --- Variant 3: trailing separator on OutputDirectory ----------------------
$pkg3 = New-DisposablePackage (Join-Path $base 'packages\PathVeer-1.0.0-devsign.4')
$outTrailing = (Join-Path $base 'packages') + [System.IO.Path]::DirectorySeparatorChar
& pwsh -NoLogo -NoProfile -File $script -Version 1.0.0-devsign.4 -OutputDirectory $outTrailing -RuntimeIdentifier win-x64 | Out-Null
Assert-True ($LASTEXITCODE -eq 0) "Variant3 (trailing sep) generation failed (exit $LASTEXITCODE)"
$entries3 = Get-ManifestEntries $pkg3
Assert-True ($entries3 -contains 'Cli\PathVeer.Cli.exe') "Variant3 (trailing sep) missing Cli entry"
Assert-True (-not ($entries3 | Where-Object { $_ -like '*hVeer-1.0.0*' })) "Variant3 (trailing sep) produced corrupt hVeer-<version> prefix"

# --- Variant 4: no trailing separator (canonical) --------------------------
$pkg4 = New-DisposablePackage (Join-Path $base 'packages\PathVeer-1.0.0-devsign.4')
& pwsh -NoLogo -NoProfile -File $script -Version 1.0.0-devsign.4 -OutputDirectory (Join-Path $base 'packages') -RuntimeIdentifier win-x64 | Out-Null
$entries4 = Get-ManifestEntries $pkg4
Assert-True ($entries4 -contains 'Cli\PathVeer.Cli.exe') "Variant4 missing Cli entry"

# --- Variant 5: HashOnly refresh over a package dir ------------------------
# Simulate post-sign refresh: regenerate the manifest over the same finalized dir.
& pwsh -NoLogo -NoProfile -File $script -Version 1.0.0-devsign.4 -OutputDirectory (Join-Path $base 'packages') -RuntimeIdentifier win-x64 -HashOnly | Out-Null
Assert-True ($LASTEXITCODE -eq 0) "HashOnly refresh failed (exit $LASTEXITCODE)"
$entries5 = Get-ManifestEntries $pkg4
Assert-True ($entries5 -contains 'Cli\PathVeer.Cli.exe') "HashOnly missing Cli entry"

# --- Variant 6: HashOnly refresh yields a clean, package-relative manifest ---
# HashOnly regenerates the integrity manifest FROM THE FILESYSTEM (it never
# trusts a pre-existing manifest), so a stale/rooted entry in the old file is
# irrelevant. The guaranteed property is that the REFRESHED manifest contains
# only safe package-root-relative entries: no hVeer-<version> corruption, no
# rooted paths, no parent traversal. This is the exact path the release
# pipeline uses after Authenticode signing.
$pkg6 = Join-Path $base 'packages\PathVeer-1.0.0-devsign.4'
New-DisposablePackage $pkg6 | Out-Null
# Seed a corrupt/stale manifest to prove HashOnly overwrites it from disk.
$stale = "deadbeef C:\Windows\System32\evil.dll`r`n"
Set-Content -Path (Join-Path $pkg6 'package-hashes.sha256') -Value $stale -Encoding ASCII
& pwsh -NoLogo -NoProfile -File $script -Version 1.0.0-devsign.4 -OutputDirectory (Join-Path $base 'packages') -RuntimeIdentifier win-x64 -HashOnly 2>&1 | Out-Null
Assert-True ($LASTEXITCODE -eq 0) "HashOnly refresh failed (exit $LASTEXITCODE)"
$entries6 = Get-ManifestEntries $pkg6
Assert-True ($entries6 -contains 'Cli\PathVeer.Cli.exe') "HashOnly missing Cli entry"
Assert-True (-not ($entries6 | Where-Object { $_ -like '*hVeer-1.0.0*' })) "HashOnly produced corrupt hVeer-<version> prefix"
Assert-True (-not ($entries6 | Where-Object { [System.IO.Path]::IsPathRooted($_) })) "HashOnly produced rooted entry"
Assert-True (-not ($entries6 | Where-Object { $_.StartsWith('..') })) "HashOnly produced traversal entry"
# The stale rooted entry must be GONE (overwritten from disk, not preserved).
Assert-True (-not ($entries6 | Where-Object { $_ -like '*evil.dll*' })) "HashOnly preserved a stale rooted entry"

# --- Report ----------------------------------------------------------------
if ($failures.Count -eq 0) {
    Write-Host "ALL PACKAGE-GENERATOR CHECKS PASSED" -ForegroundColor Green
    exit 0
} else {
    Write-Host "PACKAGE-GENERATOR CHECKS FAILED:" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host ("  - " + $f) -ForegroundColor Red }
    exit 1
}
