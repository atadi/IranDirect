param(
    [string]$CurrentMilestone = "Verify from project-state.md",
    [string]$NextMilestone = "Verify from session-handoff.md",
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = "Continue"

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$outputPath = Join-Path $root "AI-LOCAL-STATE.md"
$generatedAt = Get-Date
$hostname = $env:COMPUTERNAME
$repositoryPath = $root

function Invoke-Captured {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Command
    )

    try {
        $text = & $Command 2>&1 | Out-String
        return @{
            ExitCode = $LASTEXITCODE
            Text = $text.TrimEnd()
        }
    }
    catch {
        return @{
            ExitCode = 1
            Text = $_.Exception.Message
        }
    }
}

function Markdown-CodeBlock {
    param(
        [string]$Text,
        [string]$Language = "text"
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        $Text = "(none)"
    }

    return @"
````$Language
$Text
````
"@
}

$branch = (git branch --show-current 2>$null).Trim()
$commit = (git rev-parse HEAD 2>$null).Trim()
$shortCommit = (git rev-parse --short HEAD 2>$null).Trim()
$latestCommit = (git log -1 --oneline 2>$null).Trim()
$workingTree = (git status --short 2>$null | Out-String).TrimEnd()
$branchDetails = (git branch -vv 2>$null | Out-String).TrimEnd()

$upstream = ""
$aheadBehind = ""

try {
    $upstream =
        (git rev-parse --abbrev-ref --symbolic-full-name "@{u}" 2>$null).
            Trim()

    if ($upstream) {
        $counts =
            (git rev-list --left-right --count "$upstream...HEAD" 2>$null).
                Trim() -split "\s+"

        if ($counts.Count -ge 2) {
            $aheadBehind =
                "Ahead: $($counts[1]); Behind: $($counts[0])"
        }
    }
}
catch {
    $upstream = "(none)"
}

$dotnetInfo =
    Invoke-Captured { dotnet --info }

$buildResult = @{
    ExitCode = 0
    Text = "Skipped by request."
}

if (-not $SkipBuild) {
    $buildResult =
        Invoke-Captured { dotnet build --nologo }
}

$testResult = @{
    ExitCode = 0
    Text = "Skipped by request."
}

if (-not $SkipTests) {
    $testResult =
        Invoke-Captured { dotnet test --nologo }
}

$serviceProcesses =
    Get-Process IranDirect.Service -ErrorAction SilentlyContinue |
    Select-Object Id, ProcessName, StartTime, Path |
    Format-Table -AutoSize |
    Out-String

$serviceRegistration =
    Get-Service -Name IranDirect -ErrorAction SilentlyContinue |
    Select-Object Name, DisplayName, Status, StartType |
    Format-Table -AutoSize |
    Out-String

$dataDirectory = "C:\ProgramData\IranDirect"

$dataFiles =
    if (Test-Path $dataDirectory) {
        Get-ChildItem $dataDirectory -Force |
        Select-Object Name, Length, LastWriteTime |
        Format-Table -AutoSize |
        Out-String
    }
    else {
        "Directory not found."
    }

$stateJson =
    if (Test-Path (Join-Path $dataDirectory "state.json")) {
        Get-Content (Join-Path $dataDirectory "state.json") -Raw
    }
    else {
        "(not found)"
    }

$routeInventorySummary = "(not found)"

try {
    $routeInventoryPath =
        Join-Path $dataDirectory "route-inventory.json"

    if (Test-Path $routeInventoryPath) {
        $inventory =
            Get-Content $routeInventoryPath -Raw |
            ConvertFrom-Json

        $routeInventorySummary =
            "Owned routes: $($inventory.Routes.Count)"
    }
}
catch {
    $routeInventorySummary =
        "Unreadable: $($_.Exception.Message)"
}

$endpointInventorySummary = "(not found)"

try {
    $endpointInventoryPath =
        Join-Path $dataDirectory "endpoint-inventory.json"

    if (Test-Path $endpointInventoryPath) {
        $inventory =
            Get-Content $endpointInventoryPath -Raw |
            ConvertFrom-Json

        $endpointInventorySummary =
            "Protected endpoints: $($inventory.Endpoints.Count)"
    }
}
catch {
    $endpointInventorySummary =
        "Unreadable: $($_.Exception.Message)"
}

$endpointRoutes =
    try {
        Get-NetRoute -AddressFamily IPv4 -ErrorAction Stop |
        Where-Object {
            $_.DestinationPrefix -like "*/32" -and
            $_.RouteMetric -le 5
        } |
        Select-Object `
            DestinationPrefix,
            NextHop,
            InterfaceIndex,
            RouteMetric,
            PolicyStore |
        Sort-Object DestinationPrefix |
        Format-Table -AutoSize |
        Out-String
    }
    catch {
        "Unavailable: $($_.Exception.Message)"
    }

$buildStatus =
    if ($buildResult.ExitCode -eq 0) {
        "PASS"
    }
    else {
        "FAIL"
    }

$testStatus =
    if ($testResult.ExitCode -eq 0) {
        "PASS"
    }
    else {
        "FAIL"
    }

$workingTreeStatus =
    if ([string]::IsNullOrWhiteSpace($workingTree)) {
        "Clean"
    }
    else {
        "Dirty"
    }

$content = @"
# IranDirect Local State Snapshot

> Generated locally. Do not commit this file.
>
> This document describes the developer's current checkout and Windows runtime.
> It does not replace committed source code, tests, ADRs, or project-state documentation.

## Snapshot Metadata

- Generated: $($generatedAt.ToString("o"))
- Machine: $hostname
- Repository path: ``$repositoryPath``
- Current milestone: $CurrentMilestone
- Next milestone: $NextMilestone

## Access Boundary

A remote AI session may inspect the committed repository but cannot infer this
machine's checkout, uncommitted files, build result, Windows services, route
table, or ProgramData contents.

Use this snapshot only when its timestamp is recent enough for the current task.

## Git State

- Branch: ``$branch``
- Commit: ``$commit``
- Short commit: ``$shortCommit``
- Upstream: ``$upstream``
- Ahead/behind: $aheadBehind
- Working tree: **$workingTreeStatus**
- Latest commit: ``$latestCommit``

### Branch Details

$(Markdown-CodeBlock $branchDetails)

### Working Tree

$(Markdown-CodeBlock $workingTree)

## Verification

- Build: **$buildStatus**
- Tests: **$testStatus**

### Build Output

$(Markdown-CodeBlock $buildResult.Text)

### Test Output

$(Markdown-CodeBlock $testResult.Text)

## .NET Environment

$(Markdown-CodeBlock $dotnetInfo.Text)

## IranDirect Process and Service

### Running Process

$(Markdown-CodeBlock $serviceProcesses)

### Registered Windows Service

$(Markdown-CodeBlock $serviceRegistration)

## ProgramData

Path: ``$dataDirectory``

### Files

$(Markdown-CodeBlock $dataFiles)

### state.json

$(Markdown-CodeBlock $stateJson "json")

### Inventories

- $routeInventorySummary
- $endpointInventorySummary

## Candidate Protected /32 Routes

This is diagnostic evidence only. It is not an ownership list.

$(Markdown-CodeBlock $endpointRoutes)

## Required AI Interpretation

1. Treat committed source and tests as authoritative for implementation facts.
2. Treat accepted ADRs as authoritative for architectural intent.
3. Treat this file as evidence of the local checkout and runtime only.
4. Report differences between this snapshot and the committed repository.
5. Do not assume this snapshot remains current after any command or code change.
6. Do not mutate routes merely because a route appears in this diagnostic list.
"@

[System.IO.File]::WriteAllText(
    $outputPath,
    $content,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "AI local state written to:"
Write-Host $outputPath
Write-Host ""
Write-Host "Build: $buildStatus"
Write-Host "Tests: $testStatus"
Write-Host "Working tree: $workingTreeStatus"

if ($buildResult.ExitCode -ne 0 -or
    $testResult.ExitCode -ne 0) {
    exit 1
}