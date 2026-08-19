<#
.SYNOPSIS
    Syntax + read-only execution gate for the installer operator-state helper.

.DESCRIPTION
    Regression gate so a committed helper can never again ship with a
    PowerShell parser error (devsign.10 closure: the helper initially had a
    missing closing parenthesis at line 72).

    1. Parses tools/Test-PathVeerInstallerOperatorState.ps1 with
       [System.Management.Automation.Language.Parser]::ParseFile and asserts
       parser error count == 0.
    2. Executes the helper read-only and asserts it completes without throwing
       and emits the expected section markers. It must NOT mutate the machine;
       this is verified by contract (the helper only queries state) — we assert
       it returns a success exit code and prints the "no changes were made"
       footer.

    Exits non-zero on failure so it can gate CI / manual certification.

.EXAMPLE
    pwsh -NoProfile -File tools/Test-PathVeerInstallerOperatorState.SyntaxGate.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$Helper = Join-Path $PSScriptRoot 'Test-PathVeerInstallerOperatorState.ps1'
if (-not (Test-Path -LiteralPath $Helper)) {
    Write-Error "Helper not found: $Helper"
    exit 1
}

# --- 1. Syntax gate ------------------------------------------------------
$parseErrors = $null
$null = [System.Management.Automation.Language.Parser]::ParseFile($Helper, [ref]$null, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) {
    Write-Error ("Helper has {0} parser error(s):" -f $parseErrors.Count)
    foreach ($e in $parseErrors) { Write-Error $e.Message }
    exit 1
}
Write-Host "PASS: helper parses with 0 parser errors." -ForegroundColor Green

# --- 2. Read-only execution gate ------------------------------------------
$out = & pwsh -NoLogo -NoProfile -File $Helper 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error ("Helper execution failed (exit {0}). Output:`n{1}" -f $LASTEXITCODE, ($out -join "`n"))
    exit 1
}
$joined = $out -join "`n"
if ($joined -notmatch 'READ-ONLY' -or $joined -notmatch 'no changes were made') {
    Write-Error "Helper did not emit expected read-only section markers."
    exit 1
}
Write-Host "PASS: helper executed read-only without mutation markers." -ForegroundColor Green
Write-Host "`nALL GATES PASSED." -ForegroundColor Green
exit 0
