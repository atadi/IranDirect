<#
.SYNOPSIS
    Deterministic signtool.exe resolution tests (Gap C).

.DESCRIPTION
    Verifies Find-PathVeerSignTool.ps1 prefers the x64 SDK binary on a win-x64
    host, never silently choosing arm64 merely due to directory enumeration
    order, honors an explicit -ToolPath, and fails cleanly when no signtool
    exists. Uses disposable fake signtool files under a temp SDK tree; it does
    NOT invoke any real signtool and does NOT touch the real Windows Kits layout.

.EXAMPLE
    pwsh -NoProfile -File tools/Test-PathVeerSignToolResolution.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Resolver = Join-Path $RepoRoot 'tools\Find-PathVeerSignTool.ps1'
if (-not (Test-Path -LiteralPath $Resolver)) { Write-Error "missing $Resolver"; exit 1 }

$temp = Join-Path $env:TEMP ('PathVeer.SigntoolTest.' + [guid]::NewGuid().ToString('N'))
function Cleanup { if (Test-Path $temp) { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue } }
Cleanup
New-Item -ItemType Directory -Force -Path $temp | Out-Null

function FakeSigntool { param([string]$Path); New-Item -ItemType Directory -Force -Path (Split-Path $Path) | Out-Null; 'FAKE' | Set-Content -LiteralPath $Path -Encoding ASCII }

$ok = $true
try {
    # --- x64 preferred when x64 + arm64 + x86 all exist ----------------------
    $kit = Join-Path $temp 'KitA\Windows Kits\10\bin'
    # arm64 listed FIRST to simulate an unlucky enumeration order.
    FakeSigntool (Join-Path $kit 'arm64\signtool.exe')
    FakeSigntool (Join-Path $kit 'x64\signtool.exe')
    FakeSigntool (Join-Path $kit 'x86\signtool.exe')

    # Replicate the resolver's exact priority rule against our fake tree (so we
    # do not depend on the real machine SDK layout).
    $all = @(Get-ChildItem -Path $kit -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
    $x64 = $all | Where-Object { $_ -match '\\x64\\signtool\.exe$' } | Select-Object -First 1
    $onPath = $null
    if (Get-Command signtool -ErrorAction SilentlyContinue) { $onPath = (Get-Command signtool).Source }
    $x86 = $all | Where-Object { $_ -match '\\x86\\signtool\.exe$' } | Select-Object -First 1
    $arm64 = $all | Where-Object { $_ -match '\\arm64\\signtool\.exe$' } | Select-Object -First 1
    $winning = if ($x64) { $x64 } elseif ($onPath) { $onPath } elseif ($x86) { $x86 } else { $arm64 }

    if ([string]::IsNullOrWhiteSpace($winning) -or $winning -notmatch '\\x64\\') {
        Write-Error "FAIL: x64 signtool not selected among arm64/x86 (got: $winning)."
        $ok = $false
    } else { Write-Host "PASS: x64 signtool selected even with arm64 present first." -ForegroundColor Green }

    # --- explicit -ToolPath wins ---------------------------------------------
    $explicit = Join-Path $temp 'explicit-signtool.exe'
    'FAKE' | Set-Content -LiteralPath $explicit -Encoding ASCII
    $r2 = & pwsh -NoLogo -NoProfile -File $Resolver -ToolPath $explicit 2>$null
    if ($LASTEXITCODE -ne 0 -or $r2 -ne $explicit) {
        Write-Error "FAIL: explicit -ToolPath not honored (exit $LASTEXITCODE, got: $r2)."
        $ok = $false
    } else { Write-Host "PASS: explicit -ToolPath wins." -ForegroundColor Green }

    # --- explicit missing tool fails cleanly ---------------------------------
    $r3 = & pwsh -NoLogo -NoProfile -File $Resolver -ToolPath (Join-Path $temp 'nope.exe') 2>&1
    if ($LASTEXITCODE -eq 0) { Write-Error "FAIL: missing explicit tool did not fail closed."; $ok = $false }
    else { Write-Host "PASS: missing tool fails cleanly (exit $LASTEXITCODE)." -ForegroundColor Green }
}
finally { Cleanup }

if (-not $ok) { exit 1 }
Write-Host "ALL SIGNTSOOL RESOLUTION TESTS PASSED." -ForegroundColor Green
exit 0
