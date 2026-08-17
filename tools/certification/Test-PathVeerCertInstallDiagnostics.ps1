<#
.SYNOPSIS
    Behavioral test for the shared install-precondition diagnostics hardening (HARNESS DIAGNOSTICS
    DEFECT fix). Exercises the REAL Install-PathVeerProtectedCandidate function extracted from
    Invoke-PathVeerCertification.ps1 with mocked externals, proving that a product installer failure
    (exit 107 / ReadinessFailed) surfaces installerCategory + installerMessage and persists
    stage-specific evidence WITHOUT writing the gate's normal reboot-persistence file.

    No VM, no product install, no credentials.
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$orch = Resolve-Path "tools/certification/Invoke-PathVeerCertification.ps1"

# --- Static source assertions (cheap, high-signal) -------------------------
function Assert-Static($cond, $msg) {
    if ($cond) { Write-Host "  PASS(static): $msg" } else { Write-Host "  FAIL(static): $msg"; $script:fail++ }
}

$src = [System.IO.File]::ReadAllText($orch)
$script:fail = 0; $script:pass = 0

# 1. Exit 107 maps ONLY from ReadinessFailed in the product installer.
$installer = [System.IO.File]::ReadAllText((Resolve-Path "tools/Install-PathVeer.ps1"))
$107count = ([regex]::Matches($installer, '\b107\b')).Count
Assert-Static ($installer -match "ReadinessFailed.*\{\s*return 107") "product installer maps ReadinessFailed -> 107"
Assert-Static ($107count -eq 1) "107 appears exactly once in product installer (no 'already installed'/'version conflict' path to 107)"
# The only 'already installed' string lives in the DowngradeBlocked branch (->103), NOT 107.
Assert-Static ($installer -match "is already installed\. Setup cannot install") "installer mentions 'already installed' only in a downgrade message"
Assert-Static ($installer -match "Map-CategoryToExitCode 'DowngradeBlocked'") "DowngradeBlocked category exists (the 'already installed' branch)"

# 2. GATE-2 and GATE-3 invoke the SAME shared precondition with no per-gate argument divergence.
$gate2call = [regex]::Match($src, '(?s)function Run-GATE2\b.*?Install-PathVeerProtectedCandidate \$Session(-Stage GATE2)?').Success
$gate3call = [regex]::Match($src, '(?s)function Run-GATE3\b.*?Install-PathVeerProtectedCandidate \$Session(-Stage GATE3)?').Success
Assert-Static ($gate2call) "Run-GATE2 calls Install-PathVeerProtectedCandidate (GATE2)"
Assert-Static ($gate3call) "Run-GATE3 calls Install-PathVeerProtectedCandidate (GATE3)"
Assert-Static ($src -match "-Action Install -Feature @\('RegisterShell','InstallTray'\)") "shared precondition uses fixed Action=Install, Feature=RegisterShell,InstallTray"
$g2 = [regex]::Matches($src, ([regex]::Escape('Install-PathVeerProtectedCandidate $Session -Stage GATE2') + '(?!\d)')).Count
$g3 = [regex]::Matches($src, [regex]::Escape('Install-PathVeerProtectedCandidate $Session -Stage GATE3')).Count
$g28 = [regex]::Matches($src, [regex]::Escape('Install-PathVeerProtectedCandidate $Session -Stage GATE28')).Count
Assert-Static ($g2 -eq 1) "exactly one GATE2 stage call (count=$g2)"
Assert-Static ($g3 -eq 1) "exactly one GATE3 stage call (count=$g3)"
Assert-Static ($g28 -eq 1) "exactly one GATE28 stage call (count=$g28)"

# 3. GATE-3 precondition precedes Restart-VM (static order check inside Run-GATE3).
$m = [regex]::Match($src, '(?sm)function Run-GATE3\b.*?\n\}')
$gate3body = if ($m.Success) { $m.Groups[0].Value } else { '' }
$preCondIdx = $gate3body.IndexOf('Install-PathVeerProtectedCandidate $Session -Stage GATE3')
$rebootIdx  = $gate3body.IndexOf('Restart-VM -Name $VmName')
Assert-Static ($preCondIdx -ge 0 -and $rebootIdx -ge 0 -and $preCondIdx -lt $rebootIdx) "Run-GATE3: precondition call precedes Restart-VM (no reboot on precondition failure)"

# --- Dynamic extraction of the REAL function ------------------------------
$m2 = [regex]::Match($src, '(?s)function Install-PathVeerProtectedCandidate\(.*?\n\}')
if (-not $m2.Success) { Write-Error "Could not extract Install-PathVeerProtectedCandidate source." }
Invoke-Expression $m2.Value

# --- Mocked externals ------------------------------------------------------
# The real orchestrator always initializes $script:Cred before calling the precondition; replicate it
# so Set-StrictMode (enabled below) does not abort on the function's Get-GuestJeaSession $script:Cred call.
$script:Cred = $null
$script:EvidenceCalls = @()
function script:Get-GuestJeaSession { param($Cred) return [PSCustomObject]@{ note='mock-jea' } }
function script:Invoke-Command { param($Session,$ScriptBlock,$ArgumentList)
    # Mock the post-install verification probe (reads service state + manifest version).
    return [PSCustomObject]@{ serviceState = 'Running'; version = '1.0.0-beta.1'; manifestError = $null }
}
function script:Save-Json { param([string]$name, $obj) $script:EvidenceCalls += [PSCustomObject]@{ name=$name; obj=$obj } }
function script:Write-Warning { param($m) }   # swallow

function New-MockInstall($exitCode, $category, $message, $progress, $version) {
    [PSCustomObject]@{
        result = [PSCustomObject]@{
            exitCode = $exitCode
            installerError = $null
            installerResult = [PSCustomObject]@{ success=($exitCode -eq 0); category=$category; message=$message; version=$version; timestamp=(Get-Date -AsUTC).ToString('o') }
            installerProgress = $progress
            installerPathValidated = $true
            payloadPathValidated = $true
            action = 'Install'
            feature = @('RegisterShell','InstallTray')
        }
        error = $null
        completed = $true
        elevationAvailable = $true
    }
}

# Override Invoke-GuestJeaInstall with a script-scoped mock we can parameterize per test.
$script:MockInstall = $null
function script:Invoke-GuestJeaInstall { param($Session,$JeaSession,$Action,$Feature) return $script:MockInstall }

$fail = 0; $pass = 0
function Assert($cond, $msg) {
    if ($cond) { $script:pass++; Write-Host "  PASS: $msg" } else { $script:fail++; Write-Host "  FAIL: $msg" }
}

# --- Scenario A: exit 107 ReadinessFailed surfaces category+message + stage evidence ---
Write-Host "Scenario A: exit 107 (ReadinessFailed) -> GATE3 precondition"
$script:MockInstall = New-MockInstall 107 'ReadinessFailed' 'primary control pipe PathVeer.Control.v1 is not available' '{"stage":"ReadinessFailed","message":"primary control pipe PathVeer.Control.v1 is not available"}' $null
$script:EvidenceCalls = @()
$threw = $false; $errMsg = $null; $thrownResult = $null
try { Install-PathVeerProtectedCandidate $null -Stage GATE3 | Out-Null } catch {
    $threw = $true; $errMsg = $_.Exception.Message; $thrownResult = $_.TargetObject
}
Assert ($threw) "precondition throws on exit 107"
Assert ($errMsg -match "installerCategory='ReadinessFailed'") "thrown error exposes installerCategory='ReadinessFailed'"
Assert ($errMsg -match "installerMessage='primary control pipe PathVeer.Control.v1 is not available'") "thrown error exposes installerMessage (readiness Reason)"
Assert ($null -ne $thrownResult -and $thrownResult.installerCategory -eq 'ReadinessFailed') "thrown ErrorRecord.TargetObject carries installerCategory"
Assert ($null -ne $thrownResult -and $thrownResult.installerMessage -eq 'primary control pipe PathVeer.Control.v1 is not available') "ErrorRecord.TargetObject carries installerMessage"
Assert ($null -ne $thrownResult -and $thrownResult.installerResult -ne $null -and $thrownResult.installerResult.category -eq 'ReadinessFailed') "structured installerResult preserved"
Assert ($null -ne $thrownResult -and $thrownResult.installerProgress -like '*ReadinessFailed*') "raw installerProgress preserved"
$ev = $script:EvidenceCalls | Where-Object { $_.name -eq '03-gate3-install-precondition.json' } | Select-Object -First 1
Assert ($null -ne $ev) "stage-specific evidence 03-gate3-install-precondition.json written"
Assert ($ev.obj.installerCategory -eq 'ReadinessFailed') "evidence records installerCategory"
Assert ($ev.obj.installerMessage -eq 'primary control pipe PathVeer.Control.v1 is not available') "evidence records installerMessage"
Assert ($ev.obj.gate -eq 'GATE3') "evidence records gate=GATE3"
$rebootEv = $script:EvidenceCalls | Where-Object { $_.name -eq '03-gate3-reboot-persistence.json' }
Assert ($null -eq $rebootEv) "NO 03-gate3-reboot-persistence.json written on precondition failure"

# --- Scenario B: GATE2 stage naming ---
Write-Host "Scenario B: exit 107 -> GATE2 precondition naming"
$script:MockInstall = New-MockInstall 107 'ReadinessFailed' 'CLI status command failed (exit 1)' '{"stage":"ReadinessFailed","message":"CLI status command failed (exit 1)"}' $null
$script:EvidenceCalls = @()
try { Install-PathVeerProtectedCandidate $null -Stage GATE2 | Out-Null } catch { $threw=$true }
$ev2 = $script:EvidenceCalls | Where-Object { $_.name -eq '02-gate2-install-precondition.json' } | Select-Object -First 1
Assert ($threw) "GATE2 precondition throws on exit 107"
Assert ($null -ne $ev2) "stage-specific evidence 02-gate2-install-precondition.json written"
Assert ($ev2.obj.gate -eq 'GATE2') "evidence records gate=GATE2"

# --- Scenario C: default stage (defensive) ---
Write-Host "Scenario C: default stage falls back to install-precondition.json"
$script:MockInstall = New-MockInstall 107 'ReadinessFailed' 'service status is Stopped' '{"stage":"ReadinessFailed","message":"service status is Stopped"}' $null
$script:EvidenceCalls = @()
try { Install-PathVeerProtectedCandidate $null | Out-Null } catch { $threw=$true }
$evd = $script:EvidenceCalls | Where-Object { $_.name -eq 'install-precondition.json' } | Select-Object -First 1
Assert ($null -ne $evd) "default stage writes install-precondition.json"

# --- Scenario D: exit 0 success path unchanged ---
Write-Host "Scenario D: exit 0 + matching version -> verified, no failure evidence"
$script:MockInstall = New-MockInstall 0 $null $null $null '1.0.0-beta.1'
$script:EvidenceCalls = @()
$out = $null
try { $out = Install-PathVeerProtectedCandidate $null -Stage GATE3 } catch { Write-Host "  unexpected throw: $_" }
Assert ($null -ne $out -and $out.verified -eq $true) "exit 0 + expected version => verified=true (no throw)"
Assert ($out.installExitCode -eq 0) "result.installExitCode=0"
Assert ($script:EvidenceCalls.Count -eq 0) "no failure evidence written on success"

# --- Scenario E: null result (missing protected candidate) still reported, not 107 misread ---
Write-Host "Scenario E: null wrapper result => missing-candidate detail, no false 107 framing"
$script:MockInstall = [PSCustomObject]@{ result=$null; error='Certification payload not staged at ...\Payloads\PathVeer-1.0.0-beta.1'; completed=$false; elevationAvailable=$null }
$script:EvidenceCalls = @()
$threwE=$false; $msgE=$null
try { Install-PathVeerProtectedCandidate $null -Stage GATE3 | Out-Null } catch { $threwE=$true; $msgE=$_.Exception.Message }
Assert ($threwE) "null result throws"
Assert ($msgE -match "no result object|protected candidate") "null-result message mentions missing candidate, not 'exit 107'"

Write-Host ""
Write-Host "Install-diagnostics test: $pass passed, $fail failed."
exit $(if ($fail -eq 0) { 0 } else { 1 })
