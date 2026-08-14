<#
.SYNOPSIS
    Builds a versioned, self-contained PathVeer installation package.

.DESCRIPTION
    Phase 36.7 packaging step.

    Produces a package directory that the installer consumes. The installer
    NEVER copies files out of the working tree — it only ever reads a package
    produced here, so an installation is reproducible and does not depend on a
    developer checkout existing at install time.

    Layout produced:

        <OutputDirectory>\PathVeer-<version>\
            Service\PathVeer.Service.exe   (+ runtime files)
            Cli\PathVeer.Cli.exe
            Tray\PathVeer.Tray.exe
            package-hashes.sha256
            package.json

    package-hashes.sha256 provides INTEGRITY only (detects corruption or
    truncation). It does NOT provide authenticity — that requires Authenticode
    signing, which is a documented release prerequisite and is deliberately not
    simulated here.

.PARAMETER Version
    Product version stamped into the package, e.g. 1.0.0.

.PARAMETER OutputDirectory
    Where the package directory is created. Defaults to <repo>\artifacts\packages.

.EXAMPLE
    .\tools\New-PathVeerPackage.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    # Accepts a SemVer core with an optional pre-release suffix (e.g. 1.0.0-beta.1),
    # matching New-PathVeerRelease.ps1 so beta/RC releases can be packaged.
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $false)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $RepoRoot 'artifacts\packages'
}

$PackageRoot = Join-Path $OutputDirectory "PathVeer-$Version"

# Self-contained deployment is REQUIRED for the certified clean-Windows baseline,
# which intentionally does NOT preinstall .NET. A framework-dependent package fails at
# runtime on such a host (Service Control Manager Event 7009 + ".NET location: Not found").
# Self-contained win-x64 bundles the .NET 10 runtime (and the WindowsDesktop runtime for
# Tray) into each component so Service, CLI and Tray run with no machine-wide .NET.
# Larger package, but no external prerequisite on the target.
$SelfContained = $true

$Components = @(
    @{ Name = 'Service'; Project = 'PathVeer.Service\PathVeer.Service.csproj' }
    @{ Name = 'Cli';     Project = 'PathVeer.Cli\PathVeer.Cli.csproj' }
    @{ Name = 'Tray';    Project = 'PathVeer.Tray\PathVeer.Tray.csproj' }
)

function Write-Step {
    param([string]$Message)

    Write-Host $Message -ForegroundColor Cyan
}

function Publish-Component {
    param(
        [string]$Name,
        [string]$ProjectRelativePath
    )

    $projectPath = Join-Path $RepoRoot $ProjectRelativePath

    if (-not (Test-Path $projectPath)) {
        throw "Project not found: $projectPath"
    }

    $destination = Join-Path $PackageRoot $Name

    Write-Step "Publishing $Name..."

    & dotnet publish `
        $projectPath `
        --configuration Release `
        --runtime $RuntimeIdentifier `
        --self-contained:$SelfContained `
        -p:Version=$Version `
        -p:InformationalVersion=$Version `
        --output $destination

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Name (exit $LASTEXITCODE)."
    }

    $expectedExe = Join-Path $destination "PathVeer.$Name.exe"

    if (-not (Test-Path $expectedExe)) {
        throw "Expected executable missing after publish: $expectedExe"
    }
}

function New-HashManifest {
    Write-Step 'Computing SHA-256 package manifest...'

    $hashFile = Join-Path $PackageRoot 'package-hashes.sha256'

    if (Test-Path $hashFile) {
        Remove-Item $hashFile -Force
    }

    $lines = New-Object System.Collections.Generic.List[string]

    Get-ChildItem -Path $PackageRoot -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($PackageRoot.Length + 1)
            $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $lines.Add("$hash $relative")
        }

    Set-Content -Path $hashFile -Value $lines -Encoding ASCII

    Write-Host "  $($lines.Count) files hashed." -ForegroundColor DarkGray
}

function New-PackageMetadata {
    $metadata = [ordered]@{
        product           = 'PathVeer'
        version           = $Version
        runtimeIdentifier = $RuntimeIdentifier
        selfContained     = $SelfContained
        createdAtUtc      = (Get-Date).ToUniversalTime().ToString('o')
        signed            = $false
    }

    $path = Join-Path $PackageRoot 'package.json'

    $metadata |
        ConvertTo-Json -Depth 4 |
        Set-Content -Path $path -Encoding UTF8
}

# ---------------------------------------------------------------------------

if (Test-Path $PackageRoot) {
    Write-Step "Removing previous package at $PackageRoot..."
    Remove-Item $PackageRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $PackageRoot -Force | Out-Null

foreach ($component in $Components) {
    Publish-Component `
        -Name $component.Name `
        -ProjectRelativePath $component.Project
}

New-PackageMetadata
New-HashManifest

Write-Host ''
Write-Host "SUCCESS: package created" -ForegroundColor Green
Write-Host "  $PackageRoot" -ForegroundColor DarkGray
Write-Host ''
Write-Host 'Install with:' -ForegroundColor Cyan
Write-Host "  .\tools\Install-PathVeer.ps1 -PackageDirectory `"$PackageRoot`"" -ForegroundColor DarkGray
Write-Host ''
Write-Warning 'This package is NOT code-signed. Sign the executables and the installer before production distribution.'
