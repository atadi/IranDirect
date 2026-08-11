<#
.SYNOPSIS
    Reports PathVeer Authenticode production signing readiness.

.DESCRIPTION
    Answers the release-signing readiness question without ever printing private
    key material or certificate secrets. It inspects the configured signing
    providers (AzureSignTool / PFX / certificate-store thumbprint), detects
    signtool.exe, and reports the current production publisher policy.

    When no production Authenticode certificate is configured, the result is:

        Production ready: NO
        Reason: no production Authenticode certificate configured

    rather than crashing. The command is safe to run on any developer machine.

.EXAMPLE
    .\tools\Test-PathVeerAuthenticodeReadiness.ps1
#>
[CmdletBinding()]
param(
    # Expected production publisher identity (legal name). Defaults to UNPROVISIONED
    # until a real certificate exists. Set when a production cert is provisioned.
    [Parameter(Mandatory = $false)]
    [string]$ExpectedPublisher = 'UNPROVISIONED'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Field([string]$k, [string]$v) {
    Write-Host ("  {0,-22}: {1}" -f $k, $v)
}

# --- Resolve provider availability (no secrets printed) --------------------
$Azure = ($env:AZURE_KEY_VAULT_URI -and $env:AZURE_CLIENT_ID -and
          $env:AZURE_TENANT_ID -and $env:AZURE_CLIENT_SECRET)
$Pfx   = ($env:PATHVEER_SIGN_PFX -and $env:PATHVEER_SIGN_PASSWORD)
$Thumb = ($env:PATHVEER_SIGN_THUMBPRINT)

$Provider = if ($Azure) { 'AzureSignTool (cloud/HSM)' }
            elseif ($Pfx) { 'Local PFX' }
            elseif ($Thumb) { 'Certificate store thumbprint' }
            else { 'NONE' }

# --- Certificate availability (metadata only, never the key) ----------------
$CertAvail = $Provider -ne 'NONE'
$CertSubject = 'n/a'
$CertThumbprint = 'n/a'
$CodeSigningEku = 'n/a'
$Validity = 'n/a'
$PrivateKeyAccessible = 'n/a'

if ($Thumb) {
    try {
        $c = Get-Item -Path "Cert:\CurrentUser\My\$env:PATHVEER_SIGN_THUMBPRINT" -ErrorAction Stop
        $CertSubject = $c.Subject
        $CertThumbprint = $c.Thumbprint
        $eku = ($c.Extensions | Where-Object { $_.Oid.FriendlyName -eq 'Enhanced Key Usage' } |
                Select-Object -First 1)
        $CodeSigningEku = if ($eku -and ($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' })) { 'YES (id-kp-codeSigning)' } else { 'NO' }
        $now = Get-Date
        $Validity = if ($c.NotBefore -le $now -and $c.NotAfter -ge $now) { 'VALID' } else { 'EXPIRED/NOT-YET-VALID' }
        $PrivateKeyAccessible = if ($c.HasPrivateKey) { 'YES (non-exportable expected)' } else { 'NO' }
    }
    catch {
        $CertSubject = "thumbprint not found: $($env:PATHVEER_SIGN_THUMBPRINT)"
        $CertAvail = $false
    }
}
elseif ($Pfx) {
    $CertSubject = 'stored in PFX (not loaded here)'
    $CertThumbprint = 'not exposed'
    $CodeSigningEku = 'verified at sign time'
    $Validity = 'verified at sign time'
    $PrivateKeyAccessible = 'loaded in-process at sign time'
}

# --- signtool detection -----------------------------------------------------
$SigntoolPath = $null
$st = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($st) { $SigntoolPath = $st.FullName }
elseif (Get-Command signtool -ErrorAction SilentlyContinue) { $SigntoolPath = 'on PATH' }

# --- Timestamp service ------------------------------------------------------
$TimestampUrl = 'http://timestamp.digicert.com'   # RFC3161, SHA-256
$TimestampService = if ($Provider -ne 'NONE') { "required ($TimestampUrl)" } else { 'n/a' }

# --- Smoke test -------------------------------------------------------------
$Smoke = 'SKIPPED (no certificate configured)'
if ($CertAvail -and $SigntoolPath -and $Provider -ne 'NONE') {
    $Smoke = 'CONFIGURED (run New-PathVeerRelease -Mode Release/Signed to execute)'
}

# --- Production readiness decision -----------------------------------------
$ProductionReady = $false
$Reason = ''
if (-not $CertAvail) {
    $Reason = 'no production Authenticode certificate configured'
}
elseif ($ExpectedPublisher -eq 'UNPROVISIONED') {
    $Reason = 'certificate present but production publisher policy is UNPROVISIONED'
}
elseif ($CodeSigningEku -eq 'NO') {
    $Reason = 'certificate lacks code-signing EKU'
}
elseif ($Validity -ne 'VALID') {
    $Reason = "certificate validity: $Validity"
}
elseif ($PrivateKeyAccessible -eq 'NO') {
    $Reason = 'private key not accessible'
}
elseif (-not $SigntoolPath) {
    $Reason = 'signtool.exe not available'
}
else {
    $ProductionReady = $true
}

Write-Host ""
Write-Host "AUTHENTICODE READINESS" -ForegroundColor Cyan
Write-Field "Signing provider" $Provider
Write-Field "Certificate available" $(if ($CertAvail) { 'YES' } else { 'NO' })
Write-Field "Certificate subject" $CertSubject
Write-Field "Certificate thumbprint" $CertThumbprint
Write-Field "Code-signing EKU" $CodeSigningEku
Write-Field "Validity" $Validity
Write-Field "Private key accessible" $PrivateKeyAccessible
Write-Field "Timestamp service" $TimestampService
Write-Field "signtool.exe" $(if ($SigntoolPath) { $SigntoolPath } else { 'NOT FOUND' })
Write-Field "Sign/verify smoke test" $Smoke
Write-Field "Expected publisher" $ExpectedPublisher
Write-Host ""
if ($ProductionReady) {
    Write-Host "Production ready: YES" -ForegroundColor Green
}
else {
    Write-Host "Production ready: NO" -ForegroundColor Yellow
    Write-Host "Reason: $Reason" -ForegroundColor Yellow
}
Write-Host ""

# Exit code reflects readiness (0 = ready, 1 = not ready) for CI gating.
exit $(if ($ProductionReady) { 0 } else { 1 })
