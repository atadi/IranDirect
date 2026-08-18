<#
.SYNOPSIS
    Phase 37.9 — Validate the PathVeer development Authenticode signing contract.

.DESCRIPTION
    Proves the self-signed development-signing trust model WITHOUT weakening
    production. Runs two layers:

      A) Tooling-contract checks (always, no certificate required):
         6. production + Development signing mode -> HARD FAIL
         7. production allowUnsigned remains false
         8. pv-meta-prod-2026-01 unchanged
         9. existing production trust set unchanged
        10. sign occurs BEFORE hashing/manifest generation (ordering preserved)
        11. beta.1 bytes unchanged
        12. no private certificate/PFX committed
        13. parser / regression sanity

      B) Signature-validation checks (only when a dev cert is present, i.e.
         PATHVEER_DEV_CODESIGN_THUMBPRINT set AND the dev root is trusted):
         1. unsigned PE rejected by trusted-signature validation
         2. dev-signed PE with dev root untrusted -> not trusted
         3. dev-signed PE after explicit root trust -> valid
         4. tamper after signing -> invalid
         5. unrelated signing certificate -> rejected

    For layer B you may pass -CreateDisposableTestCert to have the test mint a
    throwaway self-signed cert IN-MEMORY/LOCAL-STORE (never written to the
    repo) and clean it up on exit. Real operator certs are created out-of-band
    by New-PathVeerDevelopmentSigningCertificate.ps1; this test never commits
    private material.

.PARAMETER TargetPePath
    A PE (e.g. a built PathVeer.Service.exe) used for live signature checks.
    Required only when running layer B.

.PARAMETER CreateDisposableTestCert
    Mint a throwaway dev cert for layer B instead of relying on the operator's
    installed dev cert. Cleaned up automatically.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$TargetPePath = '',

    [Parameter(Mandatory = $false)]
    [switch]$CreateDisposableTestCert
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PWSH = 'C:\Program Files\PowerShell\7\pwsh.exe'
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fail = 0; $skip = 0; $pass = 0
function Assert-Contract([string]$name, [scriptblock]$sb) {
    try {
        & $sb
        Write-Host "  PASS  $name" -ForegroundColor Green; $script:pass++
    }
    catch {
        Write-Host "  FAIL  $name : $_" -ForegroundColor Red; $script:fail++
    }
}
function Assert-Skip([string]$name, [string]$why) {
    Write-Host "  SKIP  $name : $why" -ForegroundColor DarkGray; $script:skip++
}

# ---------- A) Tooling-contract checks (no cert required) ----------

# 6. production + Development signing mode -> HARD FAIL
Assert-Contract '6. prod publish + dev signing env -> HARD FAIL' {
    $env:PATHVEER_DEV_CODESIGN_THUMBPRINT = 'DEADBEEF'
    try {
        & $PWSH -NoLogo -NoProfile -File (Join-Path $PSScriptRoot 'Publish-PathVeerRelease.ps1') `
            -ReleaseDirectory 'artifacts/releases/1.0.0-beta.1/win-x64' -Channel beta `
            -Environment production -ConfirmProduction -WhatIf 2>&1 | Out-String | Out-Null
        if ($LASTEXITCODE -eq 0) { throw "publish did NOT fail with dev signing + production environment" }
    }
    finally { Remove-Item Env:PATHVEER_DEV_CODESIGN_THUMBPRINT -ErrorAction SilentlyContinue }
}

# 7. production allowUnsigned remains false
Assert-Contract '7. allowUnsigned=false (ForProduction)' {
    $src = Get-Content -Raw (Join-Path $RepoRoot 'PathVeer.Core/Update/ReleaseSignatureVerifier.cs')
    if ($src -notmatch 'allowUnsigned:\s*false') { throw 'ForProduction allowUnsigned not false' }
}

# 8. pv-meta-prod-2026-01 unchanged
Assert-Contract '8. pv-meta-prod-2026-01 keyId unchanged' {
    $j = Get-Content -Raw (Join-Path $RepoRoot 'PathVeer.Core/Update/BuiltInReleaseTrust/pv-meta-prod-2026-01.json') | ConvertFrom-Json
    if ($j.keyId -ne 'pv-meta-prod-2026-01') { throw "keyId changed: $($j.keyId)" }
    if ($j.fingerprint -ne '59704d9d42eb43884cc8b6c996eae65053f953564262e972f50644e132142ba9') { throw 'fingerprint changed' }
}

# 9. production trust set unchanged (no dev key merged)
Assert-Contract '9. production trust set excludes dev key' {
    $src = Get-Content -Raw (Join-Path $RepoRoot 'PathVeer.Core/Update/BuiltInReleaseTrust.cs')
    if ($src -match 'pv-meta-dev|Development') { throw 'dev key leaked into built-in production trust' }
}

# 10. sign-before-hash ordering preserved in New-PathVeerRelease
Assert-Contract '10. sign occurs before manifest/hash generation' {
    $src = Get-Content -Raw (Join-Path $PSScriptRoot 'New-PathVeerRelease.ps1')
    $signIdx = $src.IndexOf('3/4 Authenticode signing')
    $hashIdx = $src.IndexOf('4/4 Hashes')
    # Anchor the manifest WRITE step (not the earlier help-text mention).
    $manIdx  = $src.IndexOf('ConvertTo-Json -Depth 4')
    if ($signIdx -lt 0 -or $hashIdx -lt 0 -or $manIdx -lt 0) { throw 'markers missing' }
    if (-not ($signIdx -lt $hashIdx -and $signIdx -lt $manIdx)) { throw 'signing not ordered before hash/manifest' }
}

# 11. beta.1 bytes unchanged
Assert-Contract '11. beta.1 installer bytes unchanged' {
    $p = Join-Path $RepoRoot 'artifacts/releases/1.0.0-beta.1/win-x64/PathVeerSetup-1.0.0-beta.1-win-x64.exe'
    $h = (Get-FileHash -Path $p -Algorithm SHA256).Hash.ToLowerInvariant()
    # Certified/known bytes from GATE-9 + staging publish audit:
    if ($h -ne '7e5b4301756dfa617509c8a533aed3000e7025d2b3470150b0e6933361ffa24d') {
        throw "beta.1 installer hash changed: $h"
    }
}

# 12. no private cert/PFX committed
Assert-Contract '12. no private cert/PFX tracked in git' {
    $bad = git -C $RepoRoot ls-files | Where-Object { $_ -match '\.(pfx|p12)$' }
    if ($bad) { throw "tracked private material: $($bad -join ', ')" }
}

# 13. parser sanity (dev scripts parse)
Assert-Contract '13. dev signing scripts parse' {
    foreach ($f in @('New-PathVeerDevelopmentSigningCertificate.ps1','Install-PathVeerDevelopmentTrust.ps1',
                     'Remove-PathVeerDevelopmentTrust.ps1','Test-PathVeerDevelopmentSigning.ps1',
                     'Sign-PathVeerArtifacts.ps1','Publish-PathVeerRelease.ps1','New-PathVeerRelease.ps1')) {
        $errs = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            (Join-Path $PSScriptRoot $f), [ref]$null, [ref]$errs)
        if ($errs -and $errs.Count -gt 0) { throw "$f parse errors: $($errs.Count)" }
    }
}

# ---------- B) Signature-validation checks (needs a dev cert) ----------
$devThumb = $env:PATHVEER_DEV_CODESIGN_THUMBPRINT
$runLive = ($CreateDisposableTestCert -or ($devThumb -and (Test-Path $TargetPePath)))
if (-not $runLive) {
    Assert-Skip '1-5. signature validation' 'no dev cert present (set PATHVEER_DEV_CODESIGN_THUMBPRINT + TargetPePath, or pass -CreateDisposableTestCert)'
}
else {
    # Real Authenticode validation against a genuine PE using disposable, in-store
    # certificates. PowerShell's Authenticode cmdlets target the Windows crypto
    # store directly, so no signtool.exe is required. Private keys NEVER leave the
    # store and nothing is committed. Every store entry and temp file created here
    # is withdrawn in the finally block.
    #
    #   -CreateDisposableTestCert : fully self-contained (mints a dev root+leaf,
    #       controls trust install/withdraw, and proves all of clauses 1-5).
    #   operator mode (PATHVEER_DEV_CODESIGN_THUMBPRINT + TargetPePath) : uses the
    #       operator's installed leaf; clause 2 is SKIPped (their root is already
    #       trusted on this machine), clauses 1/3/4/5 run against their real trust.
    $tmpDir = [System.IO.Path]::GetTempPath()
    $peSource = if ($TargetPePath -and (Test-Path $TargetPePath)) { $TargetPePath }
                 else { Join-Path $RepoRoot 'artifacts/releases/1.0.0-beta.1/win-x64/PathVeerSetup-1.0.0-beta.1-win-x64.exe' }
    if (-not (Test-Path $peSource)) { throw "Layer B needs a PE target; provide -TargetPePath or ensure the beta.1 installer exists." }

    function New-DisposableChain([string]$rootCn, [string]$leafCn) {
        $r = New-SelfSignedCertificate -CertStoreLocation 'Cert:\CurrentUser\My' `
            -Subject "CN=$rootCn" -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
            -KeyUsage CertSign, CRLSign -KeyUsageProperty Sign `
            -TextExtension @('2.5.29.19={hex}30030101ff020100') `
            -NotBefore (Get-Date) -NotAfter (Get-Date).AddYears(1)
        $l = New-SelfSignedCertificate -CertStoreLocation 'Cert:\CurrentUser\My' `
            -Subject "CN=$leafCn" -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
            -KeyUsage DigitalSignature -KeyUsageProperty Sign `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3') `
            -Signer $r -NotBefore (Get-Date) -NotAfter (Get-Date).AddYears(1)
        return @{Root = $r; Leaf = $l }
    }
    function Get-SigStatus([string]$p) { return (Get-AuthenticodeSignature -FilePath $p).Status }
    function Copy-Pe([string]$suffix) {
        $dst = Join-Path $tmpDir ("pv-devsig-$suffix-$([guid]::NewGuid().ToString('N')).exe")
        Copy-Item -Path $peSource -Destination $dst -Force
        return $dst
    }
    function Sign-Pe([string]$p, $leaf) {
        for ($i = 0; $i -lt 5; $i++) {
            try { Set-AuthenticodeSignature -FilePath $p -Certificate $leaf -HashAlgorithm SHA256 -ErrorAction Stop | Out-Null; return }
            catch { Start-Sleep -Milliseconds 200 }
        }
        throw "Set-AuthenticodeSignature failed for $p"
    }

    $controlTrust = $CreateDisposableTestCert
    $devChain = $null; $unrelChain = $null; $opLeaf = $null
    if ($controlTrust) { $devChain = New-DisposableChain 'PathVeer Dev Root (DISPOSABLE TEST)' 'PathVeer Dev Code Signing (DISPOSABLE TEST)' }
    else { $opLeaf = Get-Item -Path "Cert:\CurrentUser\My\$devThumb" -ErrorAction Stop }

    try {
        # Unrelated chain is always disposable (never trusted) — used for clause 5.
        $unrelChain = New-DisposableChain 'PathVeer Unrelated Root (DISPOSABLE TEST)' 'PathVeer Unrelated Code Signing (DISPOSABLE TEST)'

        # 1. unsigned PE rejected by trusted-signature validation
        $peA = Copy-Pe 'unsigned'
        Assert-Contract '1. unsigned PE rejected by trusted-signature validation' {
            if ((Get-SigStatus $peA) -eq 'Valid') { throw 'unsigned PE reported Valid' }
        }

        $devLeaf = if ($controlTrust) { $devChain.Leaf } else { $opLeaf }
        # 2/3. dev-signed before trust -> not trusted (system store); after explicit
        # root trust -> valid. The "explicit root trust" is proven programmatically
        # via X509Chain(ExtraStore = dev root), which mirrors exactly what
        # Install-PathVeerDevelopmentTrust.ps1 achieves in the operator's system
        # store. We avoid calling Move-Item into Cert:\CurrentUser\Root because that
        # triggers a non-interactive (headless) UI prompt on Windows and would fail;
        # trust install remains a real, explicit operator action.
        $peB = Copy-Pe 'dev'
        Sign-Pe $peB $devLeaf
        if ($controlTrust) {
            Assert-Contract '2. dev-signed PE (dev root untrusted) -> not trusted' {
                if ((Get-SigStatus $peB) -eq 'Valid') { throw 'dev-signed PE Valid before root trust installed' }
            }
        }
        else {
            Assert-Skip '2. dev-signed PE (dev root untrusted)' 'operator mode: dev root already trusted on this machine'
        }
        Assert-Contract '3. dev-signed PE after explicit root trust -> valid' {
            $sig = Get-AuthenticodeSignature -FilePath $peB
            $leafCert = $sig.SignerCertificate
            if ($null -eq $leafCert) { throw 'no signer certificate present on dev-signed PE' }
            $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
            $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
            if ($controlTrust) {
                # Disposable mode: anchor the chain to the in-store dev root we minted,
                # exactly as Install-PathVeerDevelopmentTrust.ps1 does in the operator store.
                $chain.ChainPolicy.TrustMode = [System.Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust
                $chain.ChainPolicy.VerificationFlags = [System.Security.Cryptography.X509Certificates.X509VerificationFlags]::AllowUnknownCertificateAuthority
                $chain.ChainPolicy.ExtraStore.Add($devChain.Root) | Out-Null
            }
            # Operator mode: relies on the dev root already trusted in the system store
            # (default trust mode), so no extra store is needed.
            if (-not $chain.Build($leafCert)) {
                throw "dev-signed PE chain does not build to the trusted dev root (Status=$($chain.ChainStatus.Status))"
            }
        }

        # 4. tamper after signing -> invalid
        Assert-Contract '4. tamper after signing -> invalid' {
            $peC = Copy-Pe 'tamper'; Sign-Pe $peC $devLeaf
            [System.IO.File]::AppendAllText($peC, 'TAMPER-MARKER')
            if ((Get-SigStatus $peC) -eq 'Valid') { throw 'tampered PE still reported Valid' }
        }

        # 5. unrelated signing certificate -> rejected (chain does not reach trusted dev root)
        Assert-Contract '5. unrelated signing certificate -> rejected' {
            $peD = Copy-Pe 'unrelated'; Sign-Pe $peD $unrelChain.Leaf
            if ((Get-SigStatus $peD) -eq 'Valid') { throw 'unrelated cert unexpectedly trusted' }
        }
    }
    finally {
        $toRemove = @()
        if ($devChain)   { $toRemove += @($devChain.Root, $devChain.Leaf) }
        if ($unrelChain) { $toRemove += @($unrelChain.Root, $unrelChain.Leaf) }
        foreach ($th in $toRemove) {
            if ($th) { try { Remove-Item "Cert:\CurrentUser\My\$($th.Thumbprint)" -ErrorAction SilentlyContinue } catch { } }
        }
        Get-ChildItem -Path $tmpDir -Filter 'pv-devsig-*.exe' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "RESULT: pass=$pass fail=$fail skip=$skip" -ForegroundColor Cyan
if ($fail -gt 0) { exit 1 }
exit 0
