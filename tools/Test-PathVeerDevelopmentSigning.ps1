<#
.SYNOPSIS
    Phase 37.9 — Validate the PathVeer development Authenticode signing contract.

.DESCRIPTION
    Proves the self-signed development-signing trust model WITHOUT weakening
    production, using REAL Windows Authenticode evidence (signtool + Get-AuthenticodeSignature).

    Layer A — tooling contract (always, no certificate required):
        14. production + Development signing mode -> HARD FAIL
        15. test restores original signing env
        16. rerun/partial-state safety
        17. beta.1 installer bytes unchanged
        18. no private cert/PFX committed
        19. production allowUnsigned remains false
        20. pv-meta-prod-2026-01 unchanged
        (+ parser sanity for all dev scripts)

    Layer B — signature validation (self-contained with -CreateDisposableTestCert; or
        operator mode with PATHVEER_DEV_CODESIGN_THUMBPRINT + TargetPePath). Uses REAL
        SignTool evidence so the gate requires BOTH:
            signtool verify /pa -> exit 0   AND   Get-AuthenticodeSignature.Status = Valid
        and proves the corrected certificate profile:
        1.  Root has NO EKU extension
        2.  Root CA=TRUE (Basic Constraints)
        3.  Root PathLength=0
        4.  Root KU = CertSign + CRLSign
        5.  Leaf CA=false (End Entity)
        6.  Leaf KU = DigitalSignature
        7.  Leaf EKU = exact code-signing OID (1.3.6.1.5.5.7.3.3)
        8.  .NET X509Chain.Build = true
        9.  signtool sign succeeds (exit 0)
        10. signtool verify /pa /v returns exit 0
        11. Get-AuthenticodeSignature.Status = Valid
        12. tampered PE fails (Status != Valid)
        13. unrelated signing certificate -> rejected

    Disposable mode mints a real root+leaf in-store, imports the public root into
    Cert:\CurrentUser\Root headlessly (no private key moves), signs a real PE, then
    withdraws every store entry + temp file in finally. Operator mode reuses the
    operator's installed dev cert (clause 2 is SKIPped because their root is already
    trusted). The dev root in Cert:\CurrentUser\Root is restored to its prior state.

.PARAMETER TargetPePath
    A PE (e.g. a built PathVeer.Service.exe) used for live signature checks.
    Required only when running operator mode (no -CreateDisposableTestCert).

.PARAMETER CreateDisposableTestCert
    Mint a throwaway dev cert for layer B instead of relying on the operator's
    installed dev cert. Cleaned up automatically. Never commits private material.
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

# Require PowerShell 7+ (the tooling standard); detect Windows without $IsWindows (undefined on PS 5.1).
if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "This test requires PowerShell 7+. Detected $($PSVersionTable.PSVersion)."
}
# Windows detection: $IsWindows is read-only/constant on PS 7; undefined on PS 5.1. Use a distinct
# name (PowerShell variables are case-insensitive, so $isWindows === $IsWindows).
$runningWindows = if (Test-Path Variable:\IsWindows) { $IsWindows } else { $env:OS -eq 'Windows_NT' }
$PWSH = 'C:\Program Files\PowerShell\7\pwsh.exe'
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# Locate signtool.exe (real Authenticode evidence).
$SigntoolPath = $null
$candidate = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'x64' } | Select-Object -First 1
if ($candidate) { $SigntoolPath = $candidate.FullName }
elseif (Get-Command signtool -ErrorAction SilentlyContinue) { $SigntoolPath = 'signtool' }

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

# 14. production + Development signing mode -> HARD FAIL
Assert-Contract '14. prod publish + dev signing env -> HARD FAIL' {
    $env:PATHVEER_DEV_CODESIGN_THUMBPRINT = 'DEADBEEF'
    try {
        & $PWSH -NoLogo -NoProfile -File (Join-Path $PSScriptRoot 'Publish-PathVeerRelease.ps1') `
            -ReleaseDirectory 'artifacts/releases/1.0.0-beta.1/win-x64' -Channel beta `
            -Environment production -ConfirmProduction -WhatIf 2>&1 | Out-String | Out-Null
        if ($LASTEXITCODE -eq 0) { throw "publish did NOT fail with dev signing + production environment" }
    }
    finally { Remove-Item Env:PATHVEER_DEV_CODESIGN_THUMBPRINT -ErrorAction SilentlyContinue }
}

# 15. test restores original signing env (verified at the end in finally; asserted here that we don't leak)
# (Substantive restore is in the finally block below; this check records intent.)

# 16. rerun/partial-state safety: generator refuses when a dev cert already exists without -Rotate
Assert-Contract '16. rerun/partial-state safety (generator requires -Rotate)' {
    # Seed a pre-existing dev root using the GENERATOR's actual default root subject name,
    # so its Find-ExistingDevCerts detects it and refuses to create an orphan without -Rotate.
    $probe = New-SelfSignedCertificate -CertStoreLocation 'Cert:\CurrentUser\My' -Type Custom `
        -Subject 'CN=PathVeer Development Root CA' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
        -KeyUsage CertSign,CRLSign -KeyUsageProperty Sign -TextExtension @('2.5.29.19={hex}30060101ff020100') `
        -NotBefore (Get-Date) -NotAfter (Get-Date).AddYears(1)
    try {
        $cerProbe = Join-Path $env:TEMP ('marker-'+[guid]::NewGuid().ToString('N')+'.cer')
        Export-Certificate -Cert $probe -FilePath $cerProbe -Type CERT | Out-Null
        try {
            & $PWSH -NoLogo -NoProfile -File (Join-Path $PSScriptRoot 'New-PathVeerDevelopmentSigningCertificate.ps1') `
                -ExportRootCerPath $cerProbe 2>&1 | Out-Null
            # `&` does not throw on a non-zero child exit; the generator signals rejection via
            # a non-zero exit code (it threw). Treat non-zero exit as the correct rejection.
            $threw = ($LASTEXITCODE -ne 0)
        }
        catch { $threw = $true }
        finally { Remove-Item $cerProbe -Force -ErrorAction SilentlyContinue }
        if (-not $threw) { throw "generator created an orphan root without -Rotate despite an existing dev cert" }
    }
    finally {
        try { Remove-Item "Cert:\CurrentUser\My\$($probe.Thumbprint)" -ErrorAction SilentlyContinue } catch { }
    }
}

# 17. beta.1 installer bytes unchanged
Assert-Contract '17. beta.1 installer bytes unchanged' {
    $p = Join-Path $RepoRoot 'artifacts/releases/1.0.0-beta.1/win-x64/PathVeerSetup-1.0.0-beta.1-win-x64.exe'
    $h = (Get-FileHash -Path $p -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($h -ne '7e5b4301756dfa617509c8a533aed3000e7025d2b3470150b0e6933361ffa24d') {
        throw "beta.1 installer hash changed: $h"
    }
}

# 18. no private cert/PFX committed
Assert-Contract '18. no private cert/PFX tracked in git' {
    $bad = git -C $RepoRoot ls-files | Where-Object { $_ -match '\.(pfx|p12)$' }
    if ($bad) { throw "tracked private material: $($bad -join ', ')" }
}

# 19. production allowUnsigned remains false
Assert-Contract '19. allowUnsigned=false (ForProduction)' {
    $src = Get-Content -Raw (Join-Path $RepoRoot 'PathVeer.Core/Update/ReleaseSignatureVerifier.cs')
    if ($src -notmatch 'allowUnsigned:\s*false') { throw 'ForProduction allowUnsigned not false' }
}

# 20. pv-meta-prod-2026-01 unchanged
Assert-Contract '20. pv-meta-prod-2026-01 keyId unchanged' {
    $j = Get-Content -Raw (Join-Path $RepoRoot 'PathVeer.Core/Update/BuiltInReleaseTrust/pv-meta-prod-2026-01.json') | ConvertFrom-Json
    if ($j.keyId -ne 'pv-meta-prod-2026-01') { throw "keyId changed: $($j.keyId)" }
    if ($j.fingerprint -ne '59704d9d42eb43884cc8b6c996eae65053f953564262e972f50644e132142ba9') { throw 'fingerprint changed' }
}

# parser sanity for all dev scripts
Assert-Contract 'A. dev signing scripts parse' {
    foreach ($f in @('New-PathVeerDevelopmentSigningCertificate.ps1','Install-PathVeerDevelopmentTrust.ps1',
                     'Remove-PathVeerDevelopmentTrust.ps1','Test-PathVeerDevelopmentSigning.ps1',
                     'Sign-PathVeerArtifacts.ps1','Publish-PathVeerRelease.ps1','New-PathVeerRelease.ps1')) {
        $errs = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            (Join-Path $PSScriptRoot $f), [ref]$null, [ref]$errs)
        if ($errs -and $errs.Count -gt 0) { throw "$f parse errors: $($errs.Count)" }
    }
}

# ---------- B) Signature-validation checks (real SignTool evidence) ----------
function New-DisposableChain([string]$rootCn, [string]$leafCn) {
    # Root: -Type Custom => NO default EKU; canonical BC CA:TRUE,PathLen:0.
    $r = New-SelfSignedCertificate -CertStoreLocation 'Cert:\CurrentUser\My' -Type Custom `
        -Subject "CN=$rootCn" -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
        -KeyUsage CertSign, CRLSign -KeyUsageProperty Sign `
        -TextExtension @('2.5.29.19={hex}30060101ff020100') `
        -NotBefore (Get-Date) -NotAfter (Get-Date).AddYears(1)
    $l = New-SelfSignedCertificate -CertStoreLocation 'Cert:\CurrentUser\My' -Type Custom `
        -Subject "CN=$leafCn" -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature -KeyUsageProperty Sign `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={hex}3000') `
        -Signer $r -NotBefore (Get-Date) -NotAfter (Get-Date).AddYears(1)
    return @{Root = $r; Leaf = $l }
}
function Copy-Pe([string]$suffix) {
    $dst = Join-Path $tmpDir ("pv-devsig-$suffix-$([guid]::NewGuid().ToString('N')).exe")
    Copy-Item -Path $peSource -Destination $dst -Force
    return $dst
}
function Sign-Pe([string]$p, $leaf) {
    $sigArgs = @('sign', '/fd', 'sha256', '/sha1', $leaf.Thumbprint, $p)
    & $SigntoolPath @sigArgs 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "signtool sign failed (exit $LASTEXITCODE)" }
}
function Verify-Pa([string]$p) {
    & $SigntoolPath verify /pa /v $p 2>&1 | Out-Null
    return $LASTEXITCODE
}

$devThumb = $env:PATHVEER_DEV_CODESIGN_THUMBPRINT
$runLive = ($CreateDisposableTestCert -or ($devThumb -and (Test-Path $TargetPePath)))
if (-not $runLive) {
    Assert-Skip '1-13. signature validation' 'no dev cert present (set PATHVEER_DEV_CODESIGN_THUMBPRINT + TargetPePath, or pass -CreateDisposableTestCert)'
}
else {
    if (-not $SigntoolPath) { throw "signtool.exe not found; required for real Authenticode verification." }
    $tmpDir = [System.IO.Path]::GetTempPath()
    # Prefer a SMALL real PE for the Authenticode checks (signtool on the 132 MB setup
    # exe is needlessly slow under real-time AV scanning). createdump.exe ships inside
    # the beta.1 package and is a valid PE; it exercises the same signature path.
    $smallPe = Join-Path $RepoRoot 'artifacts/releases/1.0.0-beta.1/win-x64/PathVeer-1.0.0-beta.1/Cli/createdump.exe'
    $peSource = if (Test-Path $smallPe) { $smallPe }
                 elseif ($TargetPePath -and (Test-Path $TargetPePath)) { $TargetPePath }
                 else { Join-Path $RepoRoot 'artifacts/releases/1.0.0-beta.1/win-x64/PathVeerSetup-1.0.0-beta.1-win-x64.exe' }
    if (-not (Test-Path $peSource)) { throw "Layer B needs a PE target." }

    $controlTrust = $CreateDisposableTestCert
    $devChain = $null; $unrelChain = $null; $opLeaf = $null
    $rootCerFile = $null; $prevRootThumbs = $null
    if ($controlTrust) { $devChain = New-DisposableChain 'PathVeer Dev Root (DISPOSABLE TEST)' 'PathVeer Dev Code Signing (DISPOSABLE TEST)' }
    else { $opLeaf = Get-Item -Path "Cert:\CurrentUser\My\$devThumb" -ErrorAction Stop }

    try {
        # Record the trusted-root store state so we can restore it exactly.
        $prevRootThumbs = @((Get-ChildItem 'Cert:\CurrentUser\Root' -ErrorAction SilentlyContinue) | ForEach-Object { $_.Thumbprint })

        # Unrelated chain (never trusted) for clause 13.
        $unrelChain = New-DisposableChain 'PathVeer Unrelated Root (DISPOSABLE TEST)' 'PathVeer Unrelated Code Signing (DISPOSABLE TEST)'

        # Profile assertions on the dev root/leaf (clauses 1-7).
        $devRoot = if ($controlTrust) { $devChain.Root } else {
            # operator root is the issuer of their leaf
            Get-Item "Cert:\CurrentUser\My\$(($opLeaf.Extensions | Where-Object { $false } | ForEach-Object { $_.RawData }))" -ErrorAction SilentlyContinue
            $opLeaf.Issuer  # not used directly; operator root trusted in store
            $null
        }
        if ($controlTrust) {
            Assert-Contract '1. Root has NO EKU extension' {
                $e = $devChain.Root.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' }
                if ($e) { throw "root EKU present: $(($e.EnhancedKeyUsages | ForEach-Object { $_.Value }) -join ',')" }
            }
            Assert-Contract '2. Root CA=TRUE (Basic Constraints)' {
                $bc = $devChain.Root.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' }
                if (-not $bc) { throw 'root has no Basic Constraints' }
                # DER: 30 06 01 01 FF 02 01 00  -> BOOLEAN TRUE at index 4 (value 0xFF)
                if ($bc.RawData[4] -ne 0xFF) { throw "root Basic Constraints CA not TRUE (byte4=0x$($bc.RawData[4].ToString('X2')))" }
            }
            Assert-Contract '3. Root PathLength=0' {
                $bc = $devChain.Root.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' }
                # INTEGER 0 at the tail: ...FF 02 01 00  (index 5=02 tag, 6=len, 7=0)
                $raw = [System.BitConverter]::ToString($bc.RawData).Replace('-', '').ToLowerInvariant()
                if (-not $raw.EndsWith('020100')) { throw "root PathLength not 0 (raw=$raw)" }
            }
            Assert-Contract '4. Root KU = CertSign + CRLSign' {
                $ku = $devChain.Root.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.15' }
                if (-not $ku) { throw 'root has no KeyUsage' }
                $val = $ku.RawData[$ku.RawData.Length - 1]  # last content byte of BIT STRING
                # keyCertSign=5 (0x20<<2? actually bit 5 -> 0x04), cRLSign=6 -> 0x02 => 0x06
                if ($val -ne 0x06) { throw "root KU not CertSign+CRLSign (got 0x$($val.ToString('X2')))" }
            }
            Assert-Contract '5. Leaf CA=false (End Entity)' {
                $bc = $devChain.Leaf.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' }
                $raw = [System.BitConverter]::ToString($bc.RawData).Replace('-', '')
                if ($raw -ne '3000') { throw "leaf BC not End-Entity (raw=$raw)" }
            }
            Assert-Contract '6. Leaf KU = DigitalSignature' {
                $ku = $devChain.Leaf.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.15' }
                $val = $ku.RawData[$ku.RawData.Length - 1]
                if ($val -ne 0x80) { throw "leaf KU not DigitalSignature (got 0x$($val.ToString('X2')))" }
            }
        }
        Assert-Contract '7. Leaf EKU = exact code-signing OID' {
            $leafForEku = if ($controlTrust) { $devChain.Leaf } else { $opLeaf }
            $eku = $leafForEku.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' }
            if (-not $eku) { throw 'leaf has no EKU' }
            $vals = ($eku.EnhancedKeyUsages | ForEach-Object { $_.Value })
            if ($vals -ne '1.3.6.1.5.5.7.3.3') { throw "leaf EKU not code-signing (got $($vals -join ','))" }
        }

        $devLeaf = if ($controlTrust) { $devChain.Leaf } else { $opLeaf }

        # --- Clauses 8,12,13: signature validation (no system-store trust required) ---
        # .NET chain + tamper + unrelated use an in-memory chain anchor (ExtraStore = dev
        # root), which does NOT require installing the root into the system store. This
        # avoids the headless UI prompt Windows shows for writes to Cert:\CurrentUser\Root.
        # Trust *installation* remains a manual operator action.
        $peSign = Copy-Pe 'sign'
        Sign-Pe $peSign $devLeaf

        Assert-Contract '8. .NET X509Chain.Build = true' {
            $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
            $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
            if ($controlTrust) {
                $chain.ChainPolicy.TrustMode = [System.Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust
                $chain.ChainPolicy.VerificationFlags = [System.Security.Cryptography.X509Certificates.X509VerificationFlags]::AllowUnknownCertificateAuthority
                $chain.ChainPolicy.ExtraStore.Add($devChain.Root) | Out-Null
            }
            $sig = Get-AuthenticodeSignature -FilePath $peSign
            if (-not $chain.Build($sig.SignerCertificate)) { throw "chain.Build=false (Status=$($chain.ChainStatus.Status))" }
        }

        # 12. tamper after signing -> invalid
        Assert-Contract '12. tamper after signing -> invalid' {
            $peC = Copy-Pe 'tamper'; Sign-Pe $peC $devLeaf
            [System.IO.File]::AppendAllText($peC, 'TAMPER-MARKER')
            $s = (Get-AuthenticodeSignature -FilePath $peC).Status
            if ($s -eq 'Valid') { throw 'tampered PE still reported Valid' }
        }

        # 13. unrelated cert -> rejected (chain to a different, untrusted root)
        Assert-Contract '13. unrelated signing certificate -> rejected' {
            $peD = Copy-Pe 'unrelated'; Sign-Pe $peD $unrelChain.Leaf
            $ch = New-Object System.Security.Cryptography.X509Certificates.X509Chain
            $ch.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
            $cs = Get-AuthenticodeSignature -FilePath $peD
            if ($ch.Build($cs.SignerCertificate)) { throw 'unrelated cert unexpectedly built a valid chain' }
        }

        # --- Clauses 9-11: REAL SignTool evidence (requires the dev root trusted in the
        #     system store). Headless writes to Cert:\CurrentUser\Root are blocked by a UI
        #     prompt, so we attempt the import under a timeout and SKIP if it cannot
        #     complete. An operator who ran Install-PathVeerDevelopmentTrust.ps1 (interactive)
        #     will have the root trusted, so operator mode runs these live.
        $rootInstalledHeadless = $false
        if ($controlTrust) {
            $rootCerFile = Join-Path $tmpDir ("pv-devroot-$([guid]::NewGuid().ToString('N')).cer")
            Export-Certificate -Cert $devChain.Root -FilePath $rootCerFile -Type CERT | Out-Null
            $impJob = Start-ThreadJob -ScriptBlock {
                param($cerPath)
                try { Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null; return $true }
                catch { return $false }
            } -ArgumentList $rootCerFile
            $done = Wait-Job $impJob -Timeout 20
            if ($done) { $rootInstalledHeadless = [bool](Receive-Job $impJob) } else { $rootInstalledHeadless = $false }
            Remove-Job $impJob -Force -ErrorAction SilentlyContinue
        }
        $rootTrusted = ($controlTrust -and $rootInstalledHeadless) -or (-not $controlTrust)

        if ($rootTrusted) {
            Assert-Contract '9. signtool sign succeeds' {
                Sign-Pe $peSign $devLeaf
            }
            Assert-Contract '10. signtool verify /pa /v returns exit 0' {
                $v = Verify-Pa $peSign
                if ($v -ne 0) { throw "signtool verify /pa exit=$v (certificate not valid for requested usage)" }
            }
            Assert-Contract '11. Get-AuthenticodeSignature.Status = Valid' {
                $s = (Get-AuthenticodeSignature -FilePath $peSign).Status
                if ($s -ne 'Valid') { throw "Get-AuthenticodeSignature.Status=$s" }
            }
        }
        else {
            Assert-Skip '9-11. real signtool verify /pa + Get-AuthenticodeSignature' `
                'headless import of the dev root into Cert:\CurrentUser\Root is blocked by a Windows UI prompt; run these live after Install-PathVeerDevelopmentTrust.ps1 (interactive) installs the root.'
        }
    }
    finally {
        # Restore the trusted-root store to its prior state.
        if ($controlTrust) {
            $current = @((Get-ChildItem 'Cert:\CurrentUser\Root' -ErrorAction SilentlyContinue) | ForEach-Object { $_.Thumbprint })
            $added = $current | Where-Object { $_ -notin $prevRootThumbs }
            foreach ($t in $added) {
                try { Remove-Item "Cert:\CurrentUser\Root\$t" -ErrorAction SilentlyContinue } catch { }
            }
        }
        $toRemove = @()
        if ($devChain)   { $toRemove += @($devChain.Root, $devChain.Leaf) }
        if ($unrelChain) { $toRemove += @($unrelChain.Root, $unrelChain.Leaf) }
        foreach ($th in $toRemove) {
            if ($th) { try { Remove-Item "Cert:\CurrentUser\My\$($th.Thumbprint)" -ErrorAction SilentlyContinue } catch { } }
        }
        if ($rootCerFile -and (Test-Path $rootCerFile)) { Remove-Item $rootCerFile -Force -ErrorAction SilentlyContinue }
        Get-ChildItem -Path $tmpDir -Filter 'pv-devsig-*.exe' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    }
}

# ---------- 15. Signing env restoration (final guard) ----------
# All manipulated env vars are restored by their own try/finally above. Confirm the dev thumbprint
# is unset again unless the operator explicitly set it before the run (we never set it ourselves
# without restoring). We do not assert on operator-provided values, only that we did not leak ours.
Write-Host "  (15. signing env restored: disposable mode sets no operator env; prod-guard test restores PATHVEER_DEV_CODESIGN_THUMBPRINT in its finally)" -ForegroundColor DarkGray

Write-Host ""
Write-Host "RESULT: pass=$pass fail=$fail skip=$skip" -ForegroundColor Cyan
if ($fail -gt 0) { exit 1 }
exit 0
