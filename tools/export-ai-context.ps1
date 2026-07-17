param(
    [string]$OutputPath = "",
    [switch]$SkipLocalStateRefresh,
    [string]$CurrentMilestone = "Verify from AI/CURRENT.md",
    [string]$NextMilestone = "Verify from AI/CURRENT.md"
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath =
        Join-Path $root "AI-CONTEXT-BUNDLE.zip"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $root $OutputPath
}

if (-not $SkipLocalStateRefresh) {
    & (Join-Path $PSScriptRoot "update-ai-local-state.ps1") `
        -CurrentMilestone $CurrentMilestone `
        -NextMilestone $NextMilestone

    if ($LASTEXITCODE -ne 0) {
        throw "Local-state generation failed."
    }
}

$localStatePath = Join-Path $root "AI-LOCAL-STATE.md"

if (-not (Test-Path $localStatePath)) {
    throw "AI-LOCAL-STATE.md was not found."
}

$branch = (git branch --show-current).Trim()
$commit = (git rev-parse HEAD).Trim()
$status = @(git status --porcelain=v1)

$tempRoot = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    ("IranDirect-AI-" + [Guid]::NewGuid().ToString("N"))

$repoDirectory = Join-Path $tempRoot "repository"
New-Item -ItemType Directory -Path $repoDirectory -Force |
    Out-Null

try {
    $archivePath = Join-Path $tempRoot "repository.zip"

    git archive `
        --format=zip `
        --output=$archivePath `
        HEAD

    if ($LASTEXITCODE -ne 0) {
        throw "git archive failed."
    }

    Expand-Archive `
        -Path $archivePath `
        -DestinationPath $repoDirectory `
        -Force

    Copy-Item `
        -Path $localStatePath `
        -Destination (
            Join-Path $tempRoot "AI-LOCAL-STATE.md") `
        -Force

    $manifest = @"
# IranDirect AI Context Bundle

Generated: $((Get-Date).ToString("o"))

Branch: $branch

Commit: $commit

Working tree at export: $(
    if ($status.Count -eq 0) { "Clean" } else { "Dirty" })

## Contents

- `repository/` — committed repository exactly at the commit above
- `AI-LOCAL-STATE.md` — local checkout and runtime evidence

## Required Boot Order

1. `repository/AI-START-HERE.md`
2. `repository/docs/architecture-knowledge-base/AI/CURRENT.md`
3. `repository/docs/architecture-knowledge-base/AI/SESSION-PROTOCOL.md`
4. task-relevant AKB files
5. task-relevant source and tests
6. root `AI-LOCAL-STATE.md`

## Access Level

Treat this bundle as:

Level B — committed repository snapshot plus local-state evidence.

The repository snapshot cannot mutate the developer's machine.
Runtime commands must still be executed by the developer.
"@

    [System.IO.File]::WriteAllText(
        (Join-Path $tempRoot "BUNDLE-MANIFEST.md"),
        $manifest,
        [System.Text.UTF8Encoding]::new($false))

    if (Test-Path $OutputPath) {
        Remove-Item $OutputPath -Force
    }

    Compress-Archive `
        -Path (
            Join-Path $tempRoot "*") `
        -DestinationPath $OutputPath `
        -CompressionLevel Optimal

    Write-Host "AI context bundle created:"
    Write-Host $OutputPath
    Write-Host ""
    Write-Host "Branch: $branch"
    Write-Host "Commit: $commit"
    Write-Host "Working tree: $(
        if ($status.Count -eq 0) { 'Clean' } else { 'Dirty' })"
}
finally {
    Remove-Item `
        -Path $tempRoot `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue
}