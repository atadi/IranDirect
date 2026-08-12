<#
.SYNOPSIS
    Minimal, NON-destructive proof of the PathVeer certification JEA control plane.

.DESCRIPTION
    Exercises the EXACT shared bridge (New-GuestJeaSession from PathVeer.Certification.Jea.ps1)
    used by every privileged gate (GATE-5/2/4/6/8/22/28). Performs NO install, NO route mutation,
    NO service mutation, NO checkpoint restore.

    The operator authenticates as PV-CERT\pvcert via the native local credential prompt
    (PowerShell Direct). The script then:
      * captures the PARENT (filtered PowerShell Direct session) identity + admin role;
      * connects to the JEA endpoint 'PathVeer.Certification' with the SAME credential;
      * runs a harmless identity + admin-role check inside the JEA session;
      * PROVES the restricted boundary: commands that MUST NOT be available in the JEA
        session fail Get-Command / capability inspection (powershell.exe, Start-Process,
        Invoke-Expression, Invoke-Command, New-ScheduledTask, Set-Content, etc.);
      * PROVES the trust boundary: the ordinary/filtered parent (pvcert) cannot write into
        the protected certification tree (C:\ProgramData\PathVeerCertificationJea\{Trusted,Payloads,Transcripts}).

    Required outcome for the control plane to be accepted (ALL conditions):
        parentIsAdministrator           = False
        jeaIsAdministrator              = True
        restrictedBoundaryOk            = True   (forbidden arbitrary-execution surface absent)
        trustedFilesNotWritableByParent = True

    CRITICAL: Save-Probe is defined FIRST (before any code path that can call it) so that the
    evidence-saver is always available. A probe/cleanup failure must NEVER mask the original
    JEA exception — the real error is preserved in the result and rethrown.

    Prerequisites (operator-performed in the guest, once):
      - Enable-PathVeerCertificationJea.ps1 (elevated) has registered the endpoint.
#>
[CmdletBinding()]
param(
    [string]$VmName = 'PathVeer-Certification',
    [string]$GuestUser = 'PV-CERT\pvcert',
    [string]$ConfigurationName = 'PathVeer.Certification',
    [string]$ResultDir = 'C:\pv-cert'
)

$ErrorActionPreference = 'Stop'

# ------------------------------------------------------------------------------------------
# Save-Probe: defined BEFORE any failure path. Writes structured evidence + prints it.
# If writing evidence itself fails, it throws a wrapper that carries BOTH the original
# error and the evidence-save error so neither is lost.
# ------------------------------------------------------------------------------------------
function Save-Probe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [PSObject]$Result
    )
    $outPath = Join-Path $PWD 'artifacts/certification/jea-probe.json'
    try {
        $dir = Split-Path $outPath
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        $Result | ConvertTo-Json -Depth 8 -Compress:$false | Set-Content -Path $outPath -Encoding utf8
        Write-Host ''
        Write-Host '=== JEA PROBE RESULT ===' -ForegroundColor White
        $Result | Format-List | Out-String | Write-Host
        Write-Host "Saved: $outPath" -ForegroundColor DarkGray
    } catch {
        # Evidence save failed. Preserve the original probe error if present, plus this save error.
        $orig = if ($Result.PSObject.Properties['error']) { $Result.error } else { $null }
        $msg = "EVIDENCE SAVE FAILED. originalError=[$orig] evidenceSaveError=[$($_.Exception.Message)]"
        throw [System.InvalidOperationException]::new($msg, $_)
    }
}

# --- operator credential via native local prompt (never printed/stored) ---
$cred = Get-Credential -UserName $GuestUser -Message "Enter the certification guest ($GuestUser) password for PowerShell Direct"

# --- load the EXACT shared bridge used by the gates ---
$jeaModule = Join-Path $PSScriptRoot 'PathVeer.Certification.Jea.ps1'
if (-not (Test-Path $jeaModule)) { throw "Shared JEA module not found: $jeaModule" }
. $jeaModule

# Structured evidence object, initialized with stage flags so every run records HOW FAR it got.
$result = [ordered]@{
    parentSessionConnected   = $false
    parentIdentityCaptured   = $false
    jeaSessionConnected       = $false
    jeaIdentityCaptured       = $false
    aclChecksCompleted        = $false
    restrictionChecksCompleted = $false
    resultSavingAttempted     = $false
    resultSaved               = $false
    parentUser                = $null
    parentIsAdministrator     = $null
    jeaUser                   = $null
    jeaIsAdministrator        = $null
    configurationName         = $ConfigurationName
    completed                 = $false
    elevationAvailable        = $false
    elevationSucceeded        = $false
    restrictedBoundaryOk       = $false
    trustedFilesNotWritableByParent = $false
    parentWriteAttempts       = $null
    forbiddenAvailable        = $null
    error                     = $null
    errorType                 = $null
    errorFullyQualifiedId     = $null
    jeaConnectionError        = $null
}

$session = $null; $jeaSession = $null
try {
    # --- normal (filtered) PowerShell Direct parent session ---
    Write-Host 'Connecting to guest via PowerShell Direct (filtered parent)...' -ForegroundColor Cyan
    $session = New-PSSession -VMName $VmName -Credential $cred -ErrorAction Stop
    $result.parentSessionConnected = $true

    $parent = Invoke-Command -Session $session -ScriptBlock {
        $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $wp = New-Object System.Security.Principal.WindowsPrincipal($id)
        [PSCustomObject]@{
            user = $id.Name
            isAdministrator = $wp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
        }
    }
    $result.parentIdentityCaptured = $true
    $result.parentUser = $parent.user
    $result.parentIsAdministrator = $parent.isAdministrator

    # --- JEA endpoint connection, wrapped separately so its failure is distinguishable ---
    Write-Host "Connecting to JEA endpoint '$ConfigurationName'..." -ForegroundColor Cyan
    try {
        $jeaSession = New-GuestJeaSession -Cred $cred -VMName $VmName -ConfigurationName $ConfigurationName
    } catch {
        $result.jeaSessionConnected = $false
        $result.jeaConnectionError = $_.Exception.Message
        throw
    }
    if (-not $jeaSession) {
        $result.jeaSessionConnected = $false
        $result.jeaConnectionError = "New-GuestJeaSession returned null (endpoint '$ConfigurationName' unavailable)."
        # Build a complete unavailable report and exit cleanly (distinct from a probe crash).
        $result.completed = $false
        $result.elevationAvailable = $false
        $result.elevationSucceeded = $false
        $result.restrictedBoundaryOk = $false
        $result.trustedFilesNotWritableByParent = $false
        $result.forbiddenAvailable = @()
        $result.error = "JEA endpoint '$ConfigurationName' unavailable. Register it in the guest via Enable-PathVeerCertificationJea.ps1 (run elevated)."
        $result.resultSavingAttempted = $true
        Save-Probe -Result $result
        $result.resultSaved = $true
        Write-Host 'JEA CONTROL PLANE FAIL — endpoint unavailable (not a probe crash).' -ForegroundColor Red
        exit 1
    }
    $result.jeaSessionConnected = $true

    # --- identity/admin-role proof via the TRUSTED JEA function (NoLanguage-safe: bare call) ---
    # No arbitrary scripting is sent into the JEA session; the trusted module performs the check.
    $jea = Invoke-Command -Session $jeaSession -ScriptBlock { Test-PathVeerCertificationAdmin }
    $result.jeaIdentityCaptured = $true
    $result.jeaUser = $jea.user
    $result.jeaIsAdministrator = $jea.isAdministrator

    # --- TRUST BOUNDARY: ordinary/filtered parent (pvcert) must NOT write the protected tree ---
    # This runs in the NORMAL PowerShell Direct parent session (FullLanguage), not the JEA session.
    # The protected tree is deliberately DENIED to PV-CERT\pvcert (explicit DENY ACE + inheritance).
    # We do NOT pre-test the path with Test-Path (enumeration itself is denied and would abort the
    # probe). Instead we attempt a harmless unique canary WRITE directly into the fixed protected
    # tree and interpret an authorization failure as PASS. A successful write is a security violation.
    $protectedRoot = Join-Path $env:ProgramData 'PathVeerCertificationJea'
    $canaryTargets = @(
        $protectedRoot
        (Join-Path $protectedRoot 'Trusted')
        (Join-Path $protectedRoot 'Payloads')
        (Join-Path $protectedRoot 'Transcripts')
    )
    $parentWriteAttempts = Invoke-Command -Session $session -ScriptBlock {
        param($targets)
        $results = @()
        foreach ($root in $targets) {
            $sentinel = Join-Path $root ('probe-write-{0}.tmp' -f [guid]::NewGuid().ToString('N'))
            try {
                # Attempt the write. If it succeeds, that is a TRUST-BOUNDARY VIOLATION.
                [System.IO.File]::WriteAllText($sentinel, 'probe')
                try { Remove-Item -LiteralPath $sentinel -Force -ErrorAction SilentlyContinue } catch { }
                $results += [PSCustomObject]@{ target = $root; writeDenied = $false; classification = 'WRITE_SUCCEEDED'; note = 'SECURITY VIOLATION: parent wrote into protected tree' }
            } catch {
                $ex   = $_.Exception
                $msg  = $ex.Message
                $fqid = $_.FullyQualifiedErrorId
                $cat  = $_.CategoryInfo.Category
                # Robustly recognize an EXPECTED authorization denial.
                $isDenied = ($ex -is [System.UnauthorizedAccessException]) -or
                            ($fqid -match 'UnauthorizedAccess') -or
                            ($cat -eq 'PermissionDenied') -or
                            ($msg -match 'Access is denied' -or $msg -match 'Unauthorized')
                # Parent cannot even create the path (e.g. subdir absent) -> still not writable.
                $isAbsent = ($ex -is [System.IO.DirectoryNotFoundException]) -or ($msg -match 'Could not find|does not exist|not exist')
                if ($isDenied) {
                    $results += [PSCustomObject]@{ target = $root; writeDenied = $true; classification = 'UNAUTHORIZED'; note = 'UnauthorizedAccess (expected denial)' }
                } elseif ($isAbsent) {
                    $results += [PSCustomObject]@{ target = $root; writeDenied = $true; classification = 'CANNOT_CREATE'; note = 'parent cannot create path in protected tree (denied/absent)' }
                } else {
                    # Genuinely unexpected error: do NOT swallow it; let the probe fail loudly.
                    throw
                }
            }
        }
        return $results
    } -ArgumentList $canaryTargets
    $result.parentWriteAttempts = $parentWriteAttempts
    $parentCannotWriteProtected = $true
    foreach ($r in $parentWriteAttempts) {
        if (-not $r.writeDenied) { $parentCannotWriteProtected = $false }
    }
    $result.trustedFilesNotWritableByParent = $parentCannotWriteProtected
    $result.aclChecksCompleted = $true
    if (-not $parentCannotWriteProtected) {
        throw "SECURITY VIOLATION: ordinary parent (PV-CERT\pvcert) was able to WRITE into the protected certification tree ($protectedRoot). Trust boundary broken."
    }

    # --- RESTRICTED BOUNDARY: forbidden commands must be ABSENT from the JEA session ---
    # Delegated to the trusted Get-PathVeerCertificationBoundary function (NoLanguage-safe bare call).
    # The returned list is the certification evidence; no scripting is sent into the JEA session.
    $boundary = Invoke-Command -Session $jeaSession -ScriptBlock { Get-PathVeerCertificationBoundary }
    $result.forbiddenAvailable = $boundary.available
    $result.restrictedBoundaryOk = $boundary.restrictedBoundaryOk
    $result.restrictionChecksCompleted = $true

    $result.elevationAvailable = $result.jeaIsAdministrator
    $result.elevationSucceeded = $result.jeaIsAdministrator
    $result.completed = $true

    $result.resultSavingAttempted = $true
    $result.resultSaved = $true
    Save-Probe -Result $result

    $pass = ($result.parentIsAdministrator -eq $false) -and
            ($result.jeaIsAdministrator -eq $true) -and
            $result.restrictedBoundaryOk -and
            $result.trustedFilesNotWritableByParent
    if ($pass) {
        Write-Host 'JEA CONTROL PLANE PASS (privileged context + restricted boundary + trust boundary)' -ForegroundColor Green
        exit 0
    } else {
        Write-Host 'JEA CONTROL PLANE FAIL' -ForegroundColor Red
        if ($result.parentIsAdministrator) { Write-Host '  - parent was unexpectedly administrator' -ForegroundColor Red }
        if (-not $result.jeaIsAdministrator) { Write-Host '  - JEA session not genuinely elevated' -ForegroundColor Red }
        if (-not $result.restrictedBoundaryOk) { Write-Host "  - forbidden commands available: $($forbiddenAvailable -join ', ')" -ForegroundColor Red }
        if (-not $result.trustedFilesNotWritableByParent) { Write-Host '  - parent CAN write into protected certification tree' -ForegroundColor Red }
        exit 1
    }
} catch {
    # Preserve the ORIGINAL exception. Record it in the result, attempt to save evidence, then rethrow.
    $result.error = $_.Exception.Message
    $result.errorType = $_.Exception.GetType().FullName
    $result.errorFullyQualifiedId = $_.FullyQualifiedErrorId
    $result.resultSavingAttempted = $true
    try {
        $result.resultSaved = $true
        Save-Probe -Result $result
    } catch [System.InvalidOperationException] {
        # Save-Probe wrapped an evidence-save failure; the original error is in the message.
        $result.resultSaved = $false
        $result.error = $_.Exception.Message
    } catch {
        $result.resultSaved = $false
        $result.error = "originalError=[$($result.error)] evidenceSaveError=[$($_.Exception.Message)]"
    }
    # Rethrow the ORIGINAL error so the operator (and any CI) sees the true failure, not a cleanup artifact.
    throw
} finally {
    # Cleanup must NEVER mask the root cause. Guarded, best-effort only.
    if ($jeaSession) {
        try { Remove-PSSession -Session $jeaSession -ErrorAction SilentlyContinue } catch { }
    }
    if ($session) {
        try { Remove-PSSession -Session $session -ErrorAction SilentlyContinue } catch { }
    }
}
