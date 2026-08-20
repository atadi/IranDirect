<#
.SYNOPSIS
    Resolves signtool.exe deterministically for PathVeer win-x64 release tooling.

.DESCRIPTION
    The Windows 10 SDK ships signtool.exe under multiple RIDs
    (x64 / arm64 / x86). A naive -Recurse | Select-Object -First 1 can pick the
    arm64 binary on an x64 workstation (filesystem enumeration order), which then
    fails or behaves unexpectedly for x64 PE signing. This helper resolves in a
    deterministic priority order:

      1. -ToolPath (explicit, if it exists)            -- wins if provided
      2. x64 SDK binary                                 -- preferred for win-x64
      3. signtool on PATH                               -- if pre-resolved by env
      4. x86 SDK binary                                 -- last fallback
      5. arm64 SDK binary                               -- only if nothing else

    It never silently chooses arm64 merely due to directory enumeration order.
    On failure it throws (caller decides fail-closed behavior).

.PARAMETER ToolPath
    Optional explicit signtool.exe path. If it exists, it is returned as-is.

.EXAMPLE
    $st = .\tools\Find-PathVeerSignTool.ps1
    & $st sign /fd sha256 ...
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ToolPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-First([string[]]$candidates) {
    foreach ($c in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($c) -and (Test-Path -LiteralPath $c)) {
            return $c
        }
    }
    return $null
}

# 1. Explicit path wins.
if (-not [string]::IsNullOrWhiteSpace($ToolPath)) {
    if (Test-Path -LiteralPath $ToolPath) { return $ToolPath }
    throw "Explicit signtool path not found: $ToolPath"
}

# Collect every signtool.exe under the Windows 10 SDK bin tree, grouped by RID.
$kitsRoot = Join-Path $env:ProgramFiles(x86) 'Windows Kits\10\bin'
$signtools = @()
if (Test-Path -LiteralPath $kitsRoot) {
    $signtools = @(Get-ChildItem -Path $kitsRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName })
}

# Build RID-priority candidate lists. We prefer x64, then PATH, then x86, then
# arm64 — never arm64 due to enumeration order.
$x64 = $signtools | Where-Object { $_ -match '\\x64\\signtool\.exe$' }
$x86 = $signtools | Where-Object { $_ -match '\\x86\\signtool\.exe$' }
$arm64 = $signtools | Where-Object { $_ -match '\\arm64\\signtool\.exe$' }

$onPath = $null
if (Get-Command signtool -ErrorAction SilentlyContinue) {
    $onPath = (Get-Command signtool).Source
}

$winning = Resolve-First @(
    ($x64 | Select-Object -First 1),
    $onPath,
    ($x86 | Select-Object -First 1),
    ($arm64 | Select-Object -First 1)
)

if ([string]::IsNullOrWhiteSpace($winning)) {
    throw "signtool.exe not found. Install the Windows 10/11 SDK (or add signtool to PATH)."
}

# Sanity: prefer x64; warn if we had to fall back past x64.
if ($winning -match '\\arm64\\signtool\.exe$' -and $x64.Count -gt 0) {
    Write-Warning "Resolved arm64 signtool but an x64 binary exists; review SDK layout."
}

return $winning
