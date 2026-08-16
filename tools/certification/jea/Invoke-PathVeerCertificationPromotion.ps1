<#
.SYNOPSIS
    Operator-authorized trust promotion (one-way, elevated) of the protected certification
    installer + payload into C:\ProgramData\PathVeerCertificationJea\{Trusted,Payloads}.

    This is the genuine elevated TRUST TRANSITION. It is invoked by the operator-elevated
    Enable-PathVeerCertificationJea.ps1 (which dot-sources this file). It is ALSO importable by
    the harness regression test (Test-PathVeerCertBootstrap.ps1) to prove the fail-closed,
    transaction-like promotion contract on isolated temp directories WITHOUT mutating the host.

    CONTRACT (fail-closed, transaction-like):
      1. VALIDATE all required sources exist (installer file + payload directory, correct
         basenames) BEFORE any mutation of the protected tree.
      2. If any required source is absent -> TERMINATING ERROR; DO NOT remove existing protected
         installer/payload; DO NOT re-register a partially prepared baseline. Previously valid
         protected content is preserved.
      3. If all sources present -> copy into protected TEMP paths, verify the copies exist,
         normalize ACLs on the temp copies, then SWAP (remove old canonical, move temp ->
         canonical) only after success, so a mid-copy failure cannot leave the old candidate
         destroyed.

    No new pvcert-controlled privileged promotion mechanism is introduced; the elevated operator
    bootstrap remains the ONLY trust transition.
#>
[CmdletBinding()]
param()

# The expected protected basenames (trusted identity). These are the canonical names the JEA
# installer consumes; presence at the fixed protected path IS the trusted identity.
$script:ExpectedInstallerBasename = 'Install-PathVeer.ps1'
$script:ExpectedPayloadBasename   = 'PathVeer-1.0.0-beta.1'

# ACL helpers (moved here so the promotion function and the regression test share one definition).
$sysSid  = [System.Security.Principal.SecurityIdentifier]'S-1-5-18'      # SYSTEM
$admSid  = [System.Security.Principal.SecurityIdentifier]'S-1-5-32-544'  # BUILTIN\Administrators
$adminGroup = [System.Security.Principal.NTAccount]'BUILTIN\Administrators'
$pvcertSid = $null
try { $pvcertSid = ([System.Security.Principal.NTAccount]'PV-CERT\pvcert').Translate([System.Security.Principal.SecurityIdentifier]) } catch {}

$full = [System.Security.AccessControl.FileSystemRights]::FullControl
$noWrite = [System.Security.AccessControl.FileSystemRights]::Write -bor [System.Security.AccessControl.FileSystemRights]::Modify -bor [System.Security.AccessControl.FileSystemRights]::CreateFiles -bor [System.Security.AccessControl.FileSystemRights]::CreateSubdirectories -bor [System.Security.AccessControl.FileSystemRights]::Delete -bor [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [System.Security.AccessControl.FileSystemRights]::WriteAttributes -bor [System.Security.AccessControl.FileSystemRights]::WriteExtendedAttributes
$inherit = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
$propagate = [System.Security.AccessControl.PropagationFlags]::None

function Set-PathVeerCertificationFileAcl {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$LiteralPath)
    $acl = Get-Acl -LiteralPath $LiteralPath
    $acl.SetAccessRuleProtection($true, $false)
    $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sysSid, $full, [System.Security.AccessControl.InheritanceFlags]::None, [System.Security.AccessControl.PropagationFlags]::None, 'Allow')))
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($adminGroup, $full, [System.Security.AccessControl.InheritanceFlags]::None, [System.Security.AccessControl.PropagationFlags]::None, 'Allow')))
    if ($pvcertSid) { $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($pvcertSid, $noWrite, [System.Security.AccessControl.InheritanceFlags]::None, [System.Security.AccessControl.PropagationFlags]::None, 'Deny'))) }
    Set-Acl -LiteralPath $LiteralPath -AclObject $acl
}

function Set-PathVeerCertificationDirectoryAcl {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$LiteralPath)
    $acl = Get-Acl -LiteralPath $LiteralPath
    $acl.SetAccessRuleProtection($true, $false)
    $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sysSid, $full, $inherit, $propagate, 'Allow')))
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($adminGroup, $full, $inherit, $propagate, 'Allow')))
    if ($pvcertSid) { $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($pvcertSid, $noWrite, $inherit, $propagate, 'Deny'))) }
    Set-Acl -LiteralPath $LiteralPath -AclObject $acl
}

<#
.SYNOPSIS
    Resolve the promotion source (primary from $SourceDir, secondary from C:\pv-cert\incoming).
#>
function Get-PathVeerCertificationPromotionSource {
    param([string]$Primary, [string]$Secondary)
    if (Test-Path -LiteralPath $Primary -PathType Leaf) { return $Primary }
    if (Test-Path -LiteralPath $Secondary -PathType Leaf) { return $Secondary }
    return $null
}

<#
.SYNOPSIS
    Perform the fail-closed, transaction-like promotion of the protected installer + payload.

    Parameters let the regression test redirect the protected tree to isolated temp directories
    (the operator bootstrap passes the real C:\ProgramData\PathVeerCertificationJea\{Trusted,Payloads}).
#>
function Invoke-PathVeerCertificationPromotion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDir,
        [Parameter(Mandatory = $true)]
        [string]$TrustedDir,
        [Parameter(Mandatory = $true)]
        [string]$PayloadDir,
        [string]$IncomingDir = 'C:\pv-cert\incoming'
    )

    $promotedInstaller = Join-Path $TrustedDir $script:ExpectedInstallerBasename
    $promotedPayload   = Join-Path $PayloadDir  $script:ExpectedPayloadBasename
    $incomingInstaller = Join-Path $IncomingDir $script:ExpectedInstallerBasename
    $incomingPayload   = Join-Path $IncomingDir $script:ExpectedPayloadBasename

    $srcInstaller = Join-Path $SourceDir $script:ExpectedInstallerBasename
    $srcPayload   = Join-Path $SourceDir $script:ExpectedPayloadBasename

    $resolvedInstaller = Get-PathVeerCertificationPromotionSource -Primary $srcInstaller -Secondary $incomingInstaller
    $resolvedPayload   = $null
    if (Test-Path -LiteralPath $srcPayload -PathType Container) { $resolvedPayload = $srcPayload }
    elseif (Test-Path -LiteralPath $incomingPayload -PathType Container) { $resolvedPayload = $incomingPayload }

    # Validation gate: BOTH required sources must be present BEFORE touching any existing protected
    # content. Fail-open -> fail-closed: a missing source is a terminating error; the existing
    # protected installer/payload are intentionally NOT removed.
    $missing = @()
    if (-not $resolvedInstaller) {
        $missing += "installer source not found at '$srcInstaller' or '$incomingInstaller'"
    }
    if (-not $resolvedPayload) {
        $missing += "payload source (directory '$script:ExpectedPayloadBasename') not found at '$srcPayload' or '$incomingPayload'"
    }
    if ($missing.Count -gt 0) {
        throw ("PROMOTION ABORTED (preserving existing protected candidate): `n  " + ($missing -join "`n  ") +
               "`nThe previously valid protected installer/payload were NOT removed. Stage the required " +
               "sources and re-run the bootstrap to promote a new candidate.")
    }

    # Transaction-like promotion: copy sources to protected TEMP paths, verify, then swap into canonical.
    $promotedInstallerTmp = Join-Path $TrustedDir ($script:ExpectedInstallerBasename + '.tmp-' + [guid]::NewGuid().ToString('N'))
    $promotedPayloadTmp   = Join-Path $PayloadDir   ($script:ExpectedPayloadBasename + '.tmp-' + [guid]::NewGuid().ToString('N'))

    try {
        Copy-Item -Path $resolvedInstaller -Destination $promotedInstallerTmp -Force
        if (-not (Test-Path -LiteralPath $promotedInstallerTmp -PathType Leaf)) {
            throw "Promoted installer temp copy failed to materialize at '$promotedInstallerTmp'."
        }
        Copy-Item -Path $resolvedPayload -Destination $promotedPayloadTmp -Recurse -Force
        if (-not (Test-Path -LiteralPath $promotedPayloadTmp -PathType Container)) {
            throw "Promoted payload temp copy failed to materialize at '$promotedPayloadTmp'."
        }

        # Apply ACL normalization on the temp copies first (so a failure here leaves the canonical intact).
        Set-PathVeerCertificationFileAcl -LiteralPath $promotedInstallerTmp
        Set-PathVeerCertificationDirectoryAcl -LiteralPath $promotedPayloadTmp
        foreach ($sub in @(Get-ChildItem -LiteralPath $promotedPayloadTmp -Recurse -Directory -ErrorAction SilentlyContinue)) {
            Set-PathVeerCertificationDirectoryAcl -LiteralPath $sub.FullName
        }
        foreach ($file in @(Get-ChildItem -LiteralPath $promotedPayloadTmp -Recurse -File -ErrorAction SilentlyContinue)) {
            Set-PathVeerCertificationFileAcl -LiteralPath $file.FullName
        }

        # SWAP: remove old canonical (only now, after a verified temp copy) then move temp -> canonical.
        if (Test-Path -LiteralPath $promotedInstaller) { Remove-Item -LiteralPath $promotedInstaller -Force }
        Move-Item -LiteralPath $promotedInstallerTmp -Destination $promotedInstaller -Force
        if (Test-Path -LiteralPath $promotedPayload) { Remove-Item -LiteralPath $promotedPayload -Recurse -Force }
        Move-Item -LiteralPath $promotedPayloadTmp -Destination $promotedPayload -Force
    } catch {
        if (Test-Path -LiteralPath $promotedInstallerTmp) { Remove-Item -LiteralPath $promotedInstallerTmp -Force -ErrorAction SilentlyContinue }
        if (Test-Path -LiteralPath $promotedPayloadTmp)   { Remove-Item -LiteralPath $promotedPayloadTmp -Recurse -Force -ErrorAction SilentlyContinue }
        throw ("PROMOTION FAILED (existing protected candidate preserved): " + $_.Exception.Message)
    }
}
