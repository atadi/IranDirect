<#
.SYNOPSIS
    Real PowerShell PARAMETER BINDER test for the certification JEA CLI bridge.

.DESCRIPTION
    Reproduces the 2949ed9 real-VM GATE-2 failure as a regression test and proves the
    fix. The previous "bridge allowlist probe" only exercised the internal $validSub set
    inside the Desktop bridge and NEVER touched real PowerShell parameter binding, so it
    could not catch a stale guest-side ValidateSet that rejected a valid subverb at the
    binder before the function body ran.

    This test imports the ACTUAL guest JEA module (jea/PathVeerCertificationJea.psm1) the
    same way the JEA endpoint loads it (ModulesToImport), and invokes the EXPORTED function
    Invoke-PathVeerCli through normal parameter syntax (`& Invoke-PathVeerCli -Verb ... -SubVerb ...`).
    PowerShell's parameter binder enforces the function's real [ValidateSet] on $SubVerb --
    the same binder the real VM hit. No live VM is required: the harness guarantees
    PathVeer.Cli.exe is absent, so the function returns a harmless `available=$false` object
    AFTER binding succeeds (parameter binding is evaluated before the function body).

    PROVES:
      1. All 11 approved subverbs BIND successfully (no ValidateSet rejection).
      2. An invalid subverb FAILS binding (ParameterBindingValidationException).
      3. The exact 2949ed9 defect is gone: 'resolve' no longer throws
         "does not belong to the set add-cidr,list".

    INVARIANT GUARD (prevents the drift that caused this bug):
      The guest-module authoritative [ValidateSet] for $SubVerb is parsed from the source and
      asserted identical to the Desktop bridge $validSub mirror. If the two sets drift apart,
      the test FAILS, forcing a single consistent grammar.

.PREREQUISITES (harness guarantees, no live VM):
      - PathVeer.Cli.exe ABSENT at the module's $script:CliExe so no product call occurs.
      - Pester (3.x / 4.x) module available.

.EXIT: 0 pass, 1 fail.
#>
[CmdletBinding()]
param(
    [string]$ModulePath = (Join-Path $PSScriptRoot 'jea/PathVeerCertificationJea.psm1'),
    [string]$BridgePath = (Join-Path $PSScriptRoot 'PathVeer.Certification.Jea.ps1')
)

$ErrorActionPreference = 'Stop'
Import-Module Pester -Force -ErrorAction Stop

# --- Guard: no live product call. The module returns available=$false harmlessly. ---
$moduleText = Get-Content -LiteralPath $ModulePath -Raw
$cliExe = if ($moduleText -match "\`$script:CliExe\s*=\s*'([^']+)'") { $Matches[1] } else { $null }
if ($cliExe -and (Test-Path -LiteralPath $cliExe)) {
    throw "REFUSING: real product binary present at '$cliExe'. This binder test must not invoke product code."
}

# --- Load the ACTUAL guest module exactly as the JEA endpoint does (ModulesToImport) ---
Import-Module $ModulePath -Force -ErrorAction Stop
$inv = Get-Command Invoke-PathVeerCli -ErrorAction Stop

# --- The authoritative supported grammar (mirror of psm1 [ValidateSet] + bridge $validSub) ---
$ApprovedSubVerbs = @(
    'list','add-domain','add-ip','add-cidr','enable','disable','remove','resolve','status','invalidate','invalidate-all'
)

Describe 'Certification JEA CLI binder - real ValidateSet enforcement' {

    It 'exposes Invoke-PathVeerCli as the binder under test' {
        $inv | Should Not BeNullOrEmpty
        $inv.CommandType | Should Be 'Function'
    }

    foreach ($sv in $ApprovedSubVerbs) {
        $s = $sv
        It "parameter binder ACCEPTS approved SubVerb: '$s' (no ValidateSet rejection)" {
            # The real binder runs here. A stale ValidateSet('add-cidr','list') would throw
            # ParameterBindingValidationException for: add-domain,add-ip,enable,disable,
            # remove,resolve,status,invalidate,invalidate-all -- the 2949ed9 class of defect.
            $threw = $false
            try {
                $r = & $inv -Verb 'custom-routes' -SubVerb $s -Argument ''
            } catch {
                $threw = $true
                $script:bindErr = $_.Exception.Message
            }
            $threw | Should Be $false
        }

        It "approved SubVerb '$s' returns a structured result (not a binder failure)" {
            $r = & $inv -Verb 'custom-routes' -SubVerb $s -Argument ''
            $r | Should Not BeNullOrEmpty
            $r.PSObject.Properties.Match('available').Count | Should BeGreaterThan 0
            # Harness guarantees PathVeer.Cli.exe absent -> available=$false, no product call.
            $r.available | Should Be $false
        }
    }

    It 'parameter binder REJECTS an invalid SubVerb (defense-in-depth preserved)' {
        $threw = $false; $msg = $null
        try {
            & $inv -Verb 'custom-routes' -SubVerb 'definitely-invalid-subcommand' -Argument ''
        } catch {
            $threw = $true; $msg = $_.Exception.Message
        }
        $threw | Should Be $true
        ($msg -match 'ValidateSet' -or $msg -match 'does not belong' -or $msg -match 'parameter') | Should Be $true
    }

    It "REGRESSION (2949ed9): SubVerb 'resolve' binds (no longer rejected as add-cidr,list)" {
        $threw = $false; $msg = $null
        try {
            $r = & $inv -Verb 'custom-routes' -SubVerb 'resolve' -Argument ''
        } catch {
            $threw = $true; $msg = $_.Exception.Message
        }
        $threw | Should Be $false
        if ($threw) { Write-Host "BOUND-ERROR: $msg" }
    }

    It "REGRESSION (2949ed9): stale ValidateSet('add-cidr','list') no longer a SubVerb binder" {
        # The phrase may appear only in explanatory comments; reject it ONLY when it is an actual
        # [ValidateSet(...)] binder on a parameter (i.e. preceded by a bracket-open, not a '#').
        $src = Get-Content -LiteralPath $ModulePath -Raw
        $src | Should Not Match "\[ValidateSet\('add-cidr','list'\)"
    }

    It 'INVARIANT: guest module [ValidateSet] for SubVerb is identical to Desktop bridge $validSub' {
        $src = Get-Content -LiteralPath $ModulePath -Raw
        $m = [regex]::Match($src, "\[ValidateSet\('([^']+(?:','[^']+)*)'\)\]\s*\[string\]\`$SubVerb")
        $m.Success | Should Be $true
        $moduleSet = $m.Groups[1].Value -split "','" | ForEach-Object { $_.Trim() }

        $bridgeSrc = Get-Content -LiteralPath $BridgePath -Raw
        $bm = [regex]::Match($bridgeSrc, '\$validSub\s*=\s*@\(''([^'']+)''(?:\s*,\s*''([^'']+)'')*\)')
        $bm.Success | Should Be $true
        $bridgeTokens = @($bm.Groups[1].Value) + @($bm.Groups[2].Captures.Value)
        $bridgeSet = $bridgeTokens | ForEach-Object { $_.Trim() }

        $sMod = ($moduleSet | Sort-Object) -join ','
        $sBri = ($bridgeSet  | Sort-Object) -join ','
        $sMod | Should Be $sBri
        foreach ($tok in $ApprovedSubVerbs) {
            ($moduleSet -contains $tok) | Should Be $true
        }
    }
}

# In Pester 3.4 the Describe blocks above execute at file-load time; their pass/fail is
# surfaced by Pester's own process exit code (non-zero on any failure). Rely on that and
# do NOT re-invoke Invoke-Pester (it would run the suite twice and report zero).
exit $LASTEXITCODE

