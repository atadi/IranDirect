param(
    [string]$CurrentMilestone = "Verify from AI/CURRENT.md",
    [string]$NextMilestone = "Verify from AI/CURRENT.md",
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$IncludeSuccessfulLogs
)

$ErrorActionPreference = "Continue"

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$outputPath = Join-Path $root "AI-LOCAL-STATE.md"
$evidenceDirectory = Join-Path $root "AI-EVIDENCE"

New-Item `
    -ItemType Directory `
    -Path $evidenceDirectory `
    -Force |
    Out-Null

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Content
    )

    [System.IO.File]::WriteAllText(
        $Path,
        $Content,
        [System.Text.UTF8Encoding]::new($false))
}

function Invoke-Captured {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Command
    )

    try {
        $text = & $Command 2>&1 | Out-String

        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Text = $text.TrimEnd()
        }
    }
    catch {
        return [pscustomobject]@{
            ExitCode = 1
            Text = $_.Exception.Message
        }
    }
}

function Get-GitStatusSummary {
    $lines = @(git status --porcelain=v1 2>$null)

    $summary = [ordered]@{
        Modified = 0
        Added = 0
        Deleted = 0
        Renamed = 0
        Untracked = 0
        Other = 0
    }

    foreach ($line in $lines) {
        if ($line.StartsWith("??")) {
            $summary.Untracked++
            continue
        }

        $code = $line.Substring(0, 2)

        if ($code -match "R") {
            $summary.Renamed++
        }
        elseif ($code -match "D") {
            $summary.Deleted++
        }
        elseif ($code -match "A") {
            $summary.Added++
        }
        elseif ($code -match "M") {
            $summary.Modified++
        }
        else {
            $summary.Other++
        }
    }

    return [pscustomobject]@{
        Lines = $lines
        Summary = $summary
        IsClean = $lines.Count -eq 0
    }
}

function Get-TestCount {
    param([string]$Output)

    if ($Output -match
        'Passed:\s*(\d+).*Skipped:\s*(\d+).*Total:\s*(\d+)') {
        return [pscustomobject]@{
            Passed = [int]$matches[1]
            Skipped = [int]$matches[2]
            Total = [int]$matches[3]
        }
    }

    if ($Output -match
        'total:\s*(\d+),\s*failed:\s*(\d+),\s*succeeded:\s*(\d+),\s*skipped:\s*(\d+)') {
        return [pscustomobject]@{
            Passed = [int]$matches[3]
            Skipped = [int]$matches[4]
            Total = [int]$matches[1]
        }
    }

    return $null
}

$generatedAt = Get-Date
$branch = (git branch --show-current 2>$null).Trim()
$commit = (git rev-parse HEAD 2>$null).Trim()
$latestCommit = (git log -1 --oneline 2>$null).Trim()
$branchDetails = (git branch -vv 2>$null | Out-String).TrimEnd()
$gitState = Get-GitStatusSummary

$upstream = "(none)"
$ahead = 0
$behind = 0

try {
    $resolvedUpstream =
        (git rev-parse `
            --abbrev-ref `
            --symbolic-full-name `
            "@{u}" `
            2>$null).Trim()

    if ($resolvedUpstream) {
        $upstream = $resolvedUpstream

        $counts =
            (git rev-list `
                --left-right `
                --count `
                "$upstream...HEAD" `
                2>$null).Trim() -split "\s+"

        if ($counts.Count -ge 2) {
            $behind = [int]$counts[0]
            $ahead = [int]$counts[1]
        }
    }
}
catch {
}

$dotnetInfo = Invoke-Captured { dotnet --info }

$buildResult = [pscustomobject]@{
    ExitCode = 0
    Text = "Skipped by request."
}

if (-not $SkipBuild) {
    $buildResult =
        Invoke-Captured { dotnet build --nologo }
}

$testResult = [pscustomobject]@{
    ExitCode = 0
    Text = "Skipped by request."
}

if (-not $SkipTests) {
    $testResult =
        Invoke-Captured { dotnet test --nologo }
}

$testCount = Get-TestCount $testResult.Text

Write-Utf8NoBom `
    -Path (Join-Path $evidenceDirectory "build.log") `
    -Content $buildResult.Text

Write-Utf8NoBom `
    -Path (Join-Path $evidenceDirectory "tests.log") `
    -Content $testResult.Text

Write-Utf8NoBom `
    -Path (Join-Path $evidenceDirectory "dotnet-info.txt") `
    -Content $dotnetInfo.Text

Write-Utf8NoBom `
    -Path (Join-Path $evidenceDirectory "git-status.txt") `
    -Content (
        $branchDetails +
        [Environment]::NewLine +
        [Environment]::NewLine +
        ($gitState.Lines -join [Environment]::NewLine))

$serviceProcesses =
    @(Get-Process IranDirect.Service -ErrorAction SilentlyContinue)

$registeredService =
    Get-Service -Name IranDirect -ErrorAction SilentlyContinue

$dataDirectory = "C:\ProgramData\IranDirect"
$dataDirectoryExists = Test-Path $dataDirectory

$stateSummary = "Not found"
$statePath = Join-Path $dataDirectory "state.json"

try {
    if (Test-Path $statePath) {
        $state = Get-Content $statePath -Raw | ConvertFrom-Json

        $stateSummary =
            "Enabled=$($state.Enabled); " +
            "Gateway=$($state.Gateway); " +
            "Interface=$($state.InterfaceName) " +
            "($($state.InterfaceIndex)); " +
            "Prefixes=$($state.PrefixCount); " +
            "LastError=$($state.LastError)"
    }
}
catch {
    $stateSummary =
        "Unreadable: $($_.Exception.Message)"
}

$routeInventoryCount = "Not found"
$routeInventoryPath =
    Join-Path $dataDirectory "route-inventory.json"

try {
    if (Test-Path $routeInventoryPath) {
        $routeInventory =
            Get-Content $routeInventoryPath -Raw |
            ConvertFrom-Json

        $routeInventoryCount =
            [string]$routeInventory.Routes.Count
    }
}
catch {
    $routeInventoryCount = "Unreadable"
}

$endpointInventoryCount = "Not found"
$endpointInventoryPath =
    Join-Path $dataDirectory "endpoint-inventory.json"

try {
    if (Test-Path $endpointInventoryPath) {
        $endpointInventory =
            Get-Content $endpointInventoryPath -Raw |
            ConvertFrom-Json

        $endpointInventoryCount =
            [string]$endpointInventory.Endpoints.Count
    }
}
catch {
    $endpointInventoryCount = "Unreadable"
}

$candidateRoutes = @()

try {
    $candidateRoutes =
        @(Get-NetRoute `
            -AddressFamily IPv4 `
            -ErrorAction Stop |
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
        Sort-Object DestinationPrefix)

    $routeEvidence =
        $candidateRoutes |
        Format-Table -AutoSize |
        Out-String

    Write-Utf8NoBom `
        -Path (
            Join-Path `
                $evidenceDirectory `
                "candidate-routes.txt") `
        -Content $routeEvidence
}
catch {
    Write-Utf8NoBom `
        -Path (
            Join-Path `
                $evidenceDirectory `
                "candidate-routes.txt") `
        -Content (
            "Unavailable: " +
            $_.Exception.Message)
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

$testSummary =
    if ($testCount) {
        "$testStatus " +
        "(passed=$($testCount.Passed), " +
        "skipped=$($testCount.Skipped), " +
        "total=$($testCount.Total))"
    }
    else {
        $testStatus
    }

$processSummary =
    if ($serviceProcesses.Count -eq 0) {
        "Stopped"
    }
    else {
        "Running (PID: " +
        (($serviceProcesses.Id |
            ForEach-Object { [string]$_ }) -join ", ") +
        ")"
    }

$serviceSummary =
    if ($null -eq $registeredService) {
        "Not registered"
    }
    else {
        "$($registeredService.Status); " +
        "StartType=$($registeredService.StartType)"
    }

$workingTreeSummary =
    if ($gitState.IsClean) {
        "Clean"
    }
    else {
        "Dirty"
    }

$successLogs = ""

if ($IncludeSuccessfulLogs) {
    $successLogs = @"

## Successful Verification Output

### Build

````text
$($buildResult.Text)
````

### Tests

````text
$($testResult.Text)
````
"@
}

$failureDetails = ""

if ($buildResult.ExitCode -ne 0) {
    $failureDetails += @"

## Build Failure Details

Evidence: `AI-EVIDENCE/build.log`

````text
$($buildResult.Text)
````
"@
}

if ($testResult.ExitCode -ne 0) {
    $failureDetails += @"

## Test Failure Details

Evidence: `AI-EVIDENCE/tests.log`

````text
$($testResult.Text)
````
"@
}

$content = @"
# IranDirect Local State Snapshot

> Generated locally at $($generatedAt.ToString("o")).
>
> Do not commit this file. It contains machine-specific and time-sensitive evidence.

## Handoff Summary

- Machine: ``$env:COMPUTERNAME``
- Repository: ``$root``
- Current milestone: $CurrentMilestone
- Next milestone: $NextMilestone
- Branch: ``$branch``
- Commit: ``$commit``
- Upstream: ``$upstream``
- Ahead: $ahead
- Behind: $behind
- Working tree: **$workingTreeSummary**
- Build: **$buildStatus**
- Tests: **$testSummary**
- IranDirect process: $processSummary
- Windows service: $serviceSummary
- ProgramData present: $dataDirectoryExists
- Owned routes: $routeInventoryCount
- Protected endpoints: $endpointInventoryCount
- Candidate protected /32 routes: $($candidateRoutes.Count)

## Working Tree Classification

- Modified: $($gitState.Summary.Modified)
- Added: $($gitState.Summary.Added)
- Deleted: $($gitState.Summary.Deleted)
- Renamed: $($gitState.Summary.Renamed)
- Untracked: $($gitState.Summary.Untracked)
- Other: $($gitState.Summary.Other)

Evidence: `AI-EVIDENCE/git-status.txt`

## Runtime Summary

- `state.json`: $stateSummary
- ProgramData path: ``$dataDirectory``

Candidate `/32` routes are diagnostic evidence only and do not establish ownership.

Evidence: `AI-EVIDENCE/candidate-routes.txt`

## Evidence Files

- `AI-EVIDENCE/build.log`
- `AI-EVIDENCE/tests.log`
- `AI-EVIDENCE/dotnet-info.txt`
- `AI-EVIDENCE/git-status.txt`
- `AI-EVIDENCE/candidate-routes.txt`

## Required AI Interpretation

1. Committed source and tests define current implementation facts.
2. Accepted ADRs define architectural intent.
3. This snapshot defines local checkout and runtime evidence only.
4. Compare this snapshot with the committed repository.
5. Treat the snapshot as stale after code, Git, build, test, service, or route changes.
6. Do not mutate infrastructure from diagnostic evidence alone.
$successLogs
$failureDetails
"@

Write-Utf8NoBom `
    -Path $outputPath `
    -Content $content

Write-Host "AI local state written to:"
Write-Host $outputPath
Write-Host ""
Write-Host "Evidence written to:"
Write-Host $evidenceDirectory
Write-Host ""
Write-Host "Build: $buildStatus"
Write-Host "Tests: $testSummary"
Write-Host "Working tree: $workingTreeSummary"

if ($buildResult.ExitCode -ne 0 -or
    $testResult.ExitCode -ne 0) {
    exit 1
}