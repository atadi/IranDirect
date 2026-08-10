<#
.SYNOPSIS
    Secret-leak scan for the PathVeer R2 distribution path.

.DESCRIPTION
    Asserts that the live R2 Access Key ID and Secret Access Key never appear in
    the repository working tree or in any generated release/audit artifact.

    This is an EXTERNAL guard (it needs the DPAPI credential store to know what
    to look for) and is deliberately not part of `dotnet test`. The deterministic
    redaction behaviour itself is covered by unit tests using FAKE secrets.

    The real secret is only ever held in memory here; it is never printed and
    never written anywhere.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)][string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $RepoRoot 'tools\PathVeerR2.psm1') -Force

$loaded = Get-PathVeerR2Credential
$cred = $loaded.Credential
$akid = $cred.UserName
$ptr  = [System.Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($cred.Password)
try { $secret = [System.Runtime.InteropServices.Marshal]::PtrToStringUni($ptr) }
finally { [System.Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($ptr) }

$hits = New-Object System.Collections.Generic.List[string]

# Skip build output and the unpacked release payload (large, non-authored bytes).
$skip = @('\bin\', '\obj\', '\.git\', '\artifacts\releases\')

$files = Get-ChildItem -Path $RepoRoot -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
        $p = $_.FullName
        -not ($skip | Where-Object { $p -like "*$_*" })
    }

foreach ($f in $files) {
    try { $t = [System.IO.File]::ReadAllText($f.FullName) } catch { continue }
    if ($t.Contains($secret)) { $hits.Add("SECRET in $($f.FullName)") }
    if ($t.Contains($akid))   { $hits.Add("ACCESS KEY ID in $($f.FullName)") }
}

# Generated release + audit artifacts must also be clean.
$generated = Get-ChildItem -Path (Join-Path $RepoRoot 'artifacts\releases') -Recurse -File -Include *.json, *.txt -ErrorAction SilentlyContinue
foreach ($f in $generated) {
    $t = [System.IO.File]::ReadAllText($f.FullName)
    if ($t.Contains($secret)) { $hits.Add("SECRET in generated artifact $($f.FullName)") }
    if ($t.Contains($akid))   { $hits.Add("ACCESS KEY ID in generated artifact $($f.FullName)") }
}

$secret = $null

if ($hits.Count -gt 0) {
    Write-Host "SECRET-LEAK SCAN: FAILED" -ForegroundColor Red
    foreach ($h in $hits) { Write-Host "  $h" -ForegroundColor Red }
    exit 1
}

Write-Host "SECRET-LEAK SCAN: CLEAN" -ForegroundColor Green
Write-Host "  Scanned $($files.Count) working-tree file(s) and $($generated.Count) generated artifact(s)."
Write-Host "  Neither the R2 Access Key ID nor the Secret Access Key appears anywhere."
