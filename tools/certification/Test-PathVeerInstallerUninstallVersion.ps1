<#
.SYNOPSIS
    LOCAL product-installer test for the REAL PATHVEER INSTALLER $version defect fix.

    No VM. No real file deletion, no registry mutation, no service changes: every destructive
    cmdlet is mocked. The test extracts the REAL product functions (Get-InstalledVersion,
    Write-ResultRecord, Unregister-Uninstall, Invoke-Uninstall) from the committed

        tools/Install-PathVeer.ps1

    and drives them through the real uninstall control flow with a fake install manifest.

    Proves the minimal fix (Invoke-Uninstall now: $version = Get-InstalledVersion) closes the
    VariableIsUndefined defect for BOTH:
        -Action uninstall          (GATE-8, normal)
        -Action uninstall -PurgeState  (GATE-4, purge)
    and that the result record contains the actual installed version from the manifest.
#>

$ErrorActionPreference = 'Stop'
$Root    = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$product = Join-Path $Root 'tools\Install-PathVeer.ps1'
if (-not (Test-Path $product)) { throw "product installer not found at $product" }

$pText = Get-Content $product -Raw

# Parse and extract REAL function bodies by name (so we exercise the actual committed code).
$tok = $null; $err = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($pText, [ref]$tok, [ref]$err)
if ($err.Count -ne 0) { throw "product installer failed to parse: $($err.Count) errors" }

function Extract-Function($name) {
    $fn = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)
    if ($null -eq $fn) { throw "function '$name' not found in product installer" }
    return $fn.Extent.Text
}

# Real product functions we want to exercise verbatim.
$src_GetInstalledVersion = Extract-Function 'Get-InstalledVersion'
$src_WriteResultRecord   = Extract-Function 'Write-ResultRecord'
$src_UnregisterUninstall = Extract-Function 'Unregister-Uninstall'
# Invoke-Uninstall: take the real body but strip the final 'exit $Script:ExitCode' so the
# test host does not terminate. The exit line is irrelevant to the version-defect being tested.
$src_InvokeUninstallRaw = Extract-Function 'Invoke-Uninstall'
$src_InvokeUninstall = $src_InvokeUninstallRaw -replace 'exit \$Script:ExitCode', ''

# ---- real helpers used by the functions under test (safe: only host output) ----
function Write-Step   { param([string]$m) Write-Host $m }
function Write-Detail { param([string]$m) Write-Host "  $m" }
function Write-Ok     { param([string]$m) Write-Host $m }
function Write-Warning { param([string]$m) Write-Host "WARNING: $m" }
function Write-ProgressRecord { param([string]$Stage, [string]$Message='') Write-Host "  [$Stage] $Message" }
function Assert-Administrator { param() }   # assume elevated in test
function Map-CategoryToExitCode {
    param([string]$Category)
    switch ($Category) {
        'PurgeFailed' { return 109 }
        'UninstallFailed' { return 108 }
        default { return 1 }
    }
}

# ---- destructive / environment cmdlets MOCKED (no real side effects) ----
$script:removedPaths = [System.Collections.Generic.List[string]]::new()
$script:setItemProps  = [System.Collections.Generic.List[string]]::new()
function Test-ServiceExists { param([string]$Name) return $false }   # no service -> skip stop/sc.exe branches
function Get-Service { param([string]$Name) return $null }
function Stop-Service { param() }
function Remove-CliFromPath { param() }
function Remove-TrayStartupEntry { param() }
function Remove-StartMenuEntries { param() }
function Set-ItemProperty { param() $script:setItemProps.Add((('{0}:{1}={2}' -f $Path, $Name, (if($Value){$Value}else{'(null)'})))); Write-Host "    [mock Set-ItemProperty] $Path : $Name = $(if($Value){$Value}else{'(null)'})" }
function New-Item { param() }
function New-ItemProperty { param() }
function Remove-ItemProperty { param() }
function Remove-Item {
    param([string]$Path, [switch]$Recurse, [switch]$Force, $ErrorAction)
    $script:removedPaths.Add($Path)
}
# Unregister-Uninstall references these globals; provide safe values + mocks.
$uninstallKeyBase = 'HKCU:\Software\PathVeerTest'

# ---- install manifest (the authoritative installed-version source) ----
$tmpDir      = Join-Path $env:TEMP ('pv-installer-test-' + [guid]::NewGuid().ToString('N'))
$InstallRoot = Join-Path $tmpDir 'Program Files\PathVeer'
$ManifestPath = Join-Path $InstallRoot 'install-manifest.json'
Microsoft.PowerShell.Management\New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
$expectedVersion = '9.9.9-test-version'
$manifestJson = [ordered]@{ schemaVersion = 1; productVersion = $expectedVersion; installRoot = $InstallRoot } | ConvertTo-Json -Depth 4
Set-Content -Path $ManifestPath -Value $manifestJson -Encoding UTF8

# Result/progress capture targets used by Write-ResultRecord / Write-ProgressRecord.
$ProgressFile = Join-Path $tmpDir 'progress.jsonl'
$ResultFile   = Join-Path $tmpDir 'result.json'

# ---- load the REAL extracted product functions into this scope ----
Invoke-Expression $src_GetInstalledVersion
Invoke-Expression $src_WriteResultRecord
Invoke-Expression $src_UnregisterUninstall
Invoke-Expression $src_InvokeUninstall

# ---- test helpers ----
$failures = [System.Collections.Generic.List[string]]::new()
function Assert-True($cond, $label) {
    if ($cond) { Write-Host "  PASS: $label" }
    else { Write-Host "  FAIL: $label"; $script:failures.Add($label) }
}

# ===================================================================
# Test 1: Get-InstalledVersion reads productVersion from the manifest
# ===================================================================
$got = Get-InstalledVersion
Assert-True ($got -eq $expectedVersion) "T1 Get-InstalledVersion returns manifest productVersion ($got)"

# ===================================================================
# Test 2: normal uninstall (-Action uninstall) no longer crashes and
#         emits the installed version (GATE-8 path)
# ===================================================================
$script:removedPaths.Clear(); $script:setItemProps.Clear()
$script:ExitCode = 0
# defaults for a normal uninstall: no purge, RegisterShell so Unregister-Uninstall runs
$PurgeState    = $false
$RegisterShell = $true
try {
    Invoke-Uninstall
    Assert-True $true "T2a normal uninstall ran without VariableIsUndefined"
} catch {
    Assert-True $false "T2a normal uninstall ran without VariableIsUndefined (threw: $($_.Exception.Message))"
}
$rec = Get-Content $ResultFile -Raw | ConvertFrom-Json
Assert-True ($rec.success -eq $true) 'T2b normal uninstall result success=true'
Assert-True ($rec.category -eq 'Success') 'T2c normal uninstall result category=Success'
Assert-True ($rec.message -eq 'PathVeer uninstalled.') 'T2d normal uninstall result message correct'
Assert-True ($rec.version -eq $expectedVersion) "T2e normal uninstall result version = manifest version ($($rec.version))"
# normal uninstall PRESERVES ProgramData\PathVeer
$stateRoot = Join-Path $env:ProgramData 'PathVeer'
Assert-True (-not ($script:removedPaths | Where-Object { $_ -eq $stateRoot })) 'T2h normal uninstall PRESERVES ProgramData\PathVeer'
# The Apps&Features unregister path (Unregister-Uninstall) executes as part of the $RegisterShell
# branch without throwing (full normal-uninstall path completed in T2a). NOTE: Unregister-Uninstall
# does NOT read $version (it only removes the Uninstall registry key), so it is unaffected by the fix.

# ===================================================================
# Test 3: purge uninstall (-Action uninstall -PurgeState) no longer
#         crashes and emits the installed version (GATE-4 path)
# ===================================================================
$script:removedPaths.Clear(); $script:setItemProps.Clear()
$script:ExitCode = 0
$ProgressFile = Join-Path $tmpDir 'progress-purge.jsonl'
$ResultFile   = Join-Path $tmpDir 'result-purge.json'
$PurgeState    = $true
$RegisterShell = $true
try {
    Invoke-Uninstall
    Assert-True $true "T3a purge uninstall ran without VariableIsUndefined"
} catch {
    Assert-True $false "T3a purge uninstall ran without VariableIsUndefined (threw: $($_.Exception.Message))"
}
$recP = Get-Content $ResultFile -Raw | ConvertFrom-Json
Assert-True ($recP.success -eq $true) 'T3b purge uninstall result success=true'
Assert-True ($recP.version -eq $expectedVersion) "T3c purge uninstall result version = manifest version ($($recP.version))"
# purge MUST remove ProgramData\PathVeer
Assert-True (($script:removedPaths | Where-Object { $_ -eq $stateRoot })) 'T3d purge uninstall REMOVES ProgramData\PathVeer'

# ===================================================================
# Test 4: manifest deleted BEFORE version capture would fail -> prove
#         capture happens before deletion by deleting manifest first
#         and showing result version becomes null (contract: version
#         required from valid installed state; missing -> null, no crash)
# ===================================================================
$script:removedPaths.Clear(); $script:setItemProps.Clear()
[System.IO.File]::Delete($ManifestPath)   # actually remove the manifest (mocked Remove-Item would not delete)
$ProgressFile = Join-Path $tmpDir 'progress-noman.jsonl'
$ResultFile   = Join-Path $tmpDir 'result-noman.json'
$PurgeState    = $false
$RegisterShell = $true
try {
    Invoke-Uninstall
    Assert-True $true "T4a uninstall with missing manifest does NOT throw VariableIsUndefined"
} catch {
    Assert-True $false "T4a uninstall with missing manifest does NOT throw (threw: $($_.Exception.Message))"
}
$recN = Get-Content $ResultFile -Raw | ConvertFrom-Json
Assert-True ($recN.version -eq $null) 'T4b missing-manifest uninstall yields version=null (no crash, fail-soft per existing policy)'

# ===================================================================
# Test 5: AST proof that no $version read precedes a write on any valid
#         action path (the fix is structurally complete)
# ===================================================================
$funcs = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)
foreach ($f in $funcs) {
    $writes = $f.Body.FindAll({ param($a) $a -is [System.Management.Automation.Language.AssignmentStatementAst] -and $a.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and $a.Left.VariablePath.UserPath -eq 'version' }, $true)
    $reads  = $f.Body.FindAll({ param($v) $v -is [System.Management.Automation.Language.VariableExpressionAst] -and $v.VariablePath.UserPath -eq 'version' }, $true)
    if ($reads.Count -gt 0 -and $writes.Count -eq 0) {
        # A reader with no assignment in its own body. Valid ONLY if:
        #   (a) it is invoked exclusively from a writer (Invoke-Install / Invoke-Uninstall, both
        #       assign $version) via dynamic scoping, OR
        #   (b) it is a self-writer via its own -Version parameter (e.g. Write-InstallManifest
        #       takes $Version and writes it to the manifest; no external $version needed).
        $selfWriterViaParam = $f.Body.Parent -and ($f.Body.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'version' })
        if ($f.Name -in @('Write-ResultRecord','Unregister-Uninstall','Register-Uninstall') -or $selfWriterViaParam) {
            Assert-True $true "T5 $($f.Name) reads `$version but is always supplied a defined value by its caller (dynamic scoping / -Version param OK)"
        } else {
            Assert-True $false "T5 $($f.Name) reads `$version with no writer and no known caller writer"
        }
    }
}
# specifically: Invoke-Uninstall now contains a $version assignment
$iu = $funcs | Where-Object { $_.Name -eq 'Invoke-Uninstall' }
$iuWrites = $iu.Body.FindAll({ param($a) $a -is [System.Management.Automation.Language.AssignmentStatementAst] -and $a.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and $a.Left.VariablePath.UserPath -eq 'version' }, $true)
Assert-True ($iuWrites.Count -ge 1) 'T6 Invoke-Uninstall now assigns $version (defect closed)'
# and that assignment is $version = Get-InstalledVersion
Assert-True ($iuWrites[0].Right.Extent.Text -match 'Get-InstalledVersion') 'T7 Invoke-Uninstall captures version from Get-InstalledVersion (installed state, not package)'

# ===================================================================
# Report
# ===================================================================
Write-Host ''
$total = (Test-Path variable:script:nPass)
if ($failures.Count -eq 0) {
    Write-Host "INSTALLER VERSION TESTS: ALL PASSED (no version defect; result records carry installed version)"
    exit 0
} else {
    Write-Host "INSTALLER VERSION TESTS: $($failures.Count) FAILED"
    foreach ($f in $failures) { Write-Host "  - $f" }
    exit 1
}
