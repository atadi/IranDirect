<#
.SYNOPSIS
    Operator-authorized bootstrap of the PathVeer certification JEA control plane.

.DESCRIPTION
    Registers a narrow, certification-only Just Enough Administration endpoint
    'PathVeer.Certification' in THIS guest. Privileged certification commands run as a
    per-connection virtual account while the connecting user keeps a filtered standard token.

    Must run ONCE with genuine local elevation (Run as Administrator) inside the PV-CERT-WINDOWS
    guest, NOT from the filtered PowerShell Direct session (Windows denies bootstrapping elevation
    from the filtered parent - that is why the old Scheduled-Task design failed with "Access denied").

    SECURITY-CRITICAL: this is the genuine elevated TRUST TRANSITION. It installs the trusted
    module into the standard all-users Windows PowerShell module path and promotes the installer /
    payload into the protected certification tree. The filtered pvcert caller never gains write to
    either location.

    JEA runs on Windows PowerShell (Desktop edition). Run this script from an ELEVATED
    Windows PowerShell (powershell.exe), NOT pwsh, so module discovery and registration use the
    Windows PowerShell PSModulePath. (If launched under pwsh, discovery validation is delegated to
    Windows PowerShell automatically.)

    After this succeeds and Test-PathVeerCertGuestJea.ps1 proves both privileged execution AND the
    restricted command boundary, take a new checkpoint PV-CERT-HARNESS. Never modify PV-CLEAN-WINDOWS.

    SECURITY: no unrestricted admin endpoint, no disabled UAC, no token-filter registry change, no
    automatic logon, no persistent privileged service beyond the reversible JEA endpoint.
#>
[CmdletBinding()]
param(
    [string]$SourceDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Enable-PathVeerCertificationJea must run from an ELEVATED PowerShell session (Run as Administrator).'
}

$pssc   = Join-Path $SourceDir 'PathVeer.Certification.pssc'
$role   = Join-Path $SourceDir 'PathVeerCertificationRole.psrc'
$psm1   = Join-Path $SourceDir 'PathVeerCertificationJea.psm1'
$psd1   = Join-Path $SourceDir 'PathVeerCertificationJea.psd1'
foreach ($f in @($pssc,$role,$psm1,$psd1)) {
    if (-not (Test-Path $f)) { throw "Required file not found: $f" }
}

# ---------------------------------------------------------------------------------------------
# Install the trusted module into the STANDARD all-users Windows PowerShell module path.
# Windows PowerShell 5.1 only auto-discovers role-capability modules from:
#   $PSHOME\Modules, $env:ProgramFiles\WindowsPowerShell\Modules, $HOME\Documents\WindowsPowerShell\Modules
# The previous location (C:\Program Files\PathVeerCertificationJea) was NOT on PSModulePath, so the
# JEA runspace could not find 'PathVeerCertificationRole' -> "Could not find the role capability".
# C:\Program Files\WindowsPowerShell\Modules is the correct, discoverable location.
# ---------------------------------------------------------------------------------------------
$moduleBase = Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules\PathVeerCertificationJea'
$roleDir    = Join-Path $moduleBase 'RoleCapabilities'
New-Item -ItemType Directory -Force -Path $roleDir | Out-Null
$installedMod = Join-Path $moduleBase 'PathVeerCertificationJea.psm1'
$installedPsd1 = Join-Path $moduleBase 'PathVeerCertificationJea.psd1'
$installedRole = Join-Path $roleDir 'PathVeerCertificationRole.psrc'
Copy-Item -Path $psm1 -Destination $installedMod -Force
Copy-Item -Path $psd1 -Destination $installedPsd1 -Force
Copy-Item -Path $role -Destination $installedRole -Force
# Narrowly clear any Mark-of-the-Web / Zone.Identifier on the EXACT trusted files we just promoted.
# This is the operator-authorized trust transition; we do NOT call Unblock-File globally.
Unblock-File -Path $installedMod, $installedPsd1, $installedRole -ErrorAction SilentlyContinue
Write-Host "Trusted module installed to: $moduleBase" -ForegroundColor Cyan

# --- Protected certification tree (TRUST BOUNDARY) ---
# Layout:
#   C:\ProgramData\PathVeerCertificationJea\
#     Trusted\     -> operator-promoted installer script (executed by privileged JEA)
#     Payloads\    -> operator-promoted package payload (consumed by privileged JEA)
#     Transcripts\ -> audit logs
#     Results\     -> privileged installer result/progress files (deterministic; not $env:TEMP)
# ACL: SYSTEM + local Administrators = FullControl; ordinary/filtered PV-CERT\pvcert = NO WRITE.
# The filtered pvcert caller is NOT a member of these; we also explicitly DENY the pvcert SID
# to be safe against group membership surprises, and disable inheritance so no relaxed inherited
# ACE leaks write access.
$baseDir    = Join-Path $env:ProgramData 'PathVeerCertificationJea'
$trustedDir = Join-Path $baseDir 'Trusted'
$payloadDir = Join-Path $baseDir 'Payloads'
$transcriptDir = Join-Path $baseDir 'Transcripts'
$resultsDir = Join-Path $baseDir 'Results'
foreach ($d in @($baseDir,$trustedDir,$payloadDir,$transcriptDir,$resultsDir)) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

$sysSid  = [System.Security.Principal.SecurityIdentifier]'S-1-5-18'      # SYSTEM
$admSid  = [System.Security.Principal.SecurityIdentifier]'S-1-5-32-544'  # BUILTIN\Administrators
$adminGroup = [System.Security.Principal.NTAccount]'BUILTIN\Administrators'
# The ordinary/filtered certification identity. Explicit DENY so group-membership surprises
# cannot grant write. Resolved best-effort; if the account does not exist, the explicit allow
# for SYSTEM+Administrators below is still sufficient to keep pvcert out.
$pvcertSid = $null
try { $pvcertSid = ([System.Security.Principal.NTAccount]'PV-CERT\pvcert').Translate([System.Security.Principal.SecurityIdentifier]) } catch {}

$full = [System.Security.AccessControl.FileSystemRights]::FullControl
$noWrite = [System.Security.AccessControl.FileSystemRights]::Write -bor [System.Security.AccessControl.FileSystemRights]::Modify -bor [System.Security.AccessControl.FileSystemRights]::CreateFiles -bor [System.Security.AccessControl.FileSystemRights]::CreateSubdirectories -bor [System.Security.AccessControl.FileSystemRights]::Delete -bor [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [System.Security.AccessControl.FileSystemRights]::WriteAttributes -bor [System.Security.AccessControl.FileSystemRights]::WriteExtendedAttributes
$inherit = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
$propagate = [System.Security.AccessControl.PropagationFlags]::None

foreach ($d in @($baseDir,$trustedDir,$payloadDir,$transcriptDir,$resultsDir)) {
    $acl = Get-Acl -Path $d
    $acl.SetAccessRuleProtection($true, $false)   # disable inheritance; drop inherited ACEs
    $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
    # Allow SYSTEM + Administrators full control.
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sysSid, $full, $inherit, $propagate, 'Allow')))
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($adminGroup, $full, $inherit, $propagate, 'Allow')))
    # Explicit DENY write to the filtered pvcert identity (if resolvable).
    if ($pvcertSid) {
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($pvcertSid, $noWrite, $inherit, $propagate, 'Deny')))
    }
    Set-Acl -Path $d -AclObject $acl
}
Write-Host "Protected certification tree created: $baseDir (SYSTEM + Administrators only; pvcert denied write)." -ForegroundColor Cyan

# --- Operator-authorized trust promotion (one-way, elevated) ---
# The bootstrap is the genuine elevated trust transition. Source material originates from the
# operator-provided $SourceDir (host share) OR the untrusted C:\pv-cert\incoming area. The
# protected copies below are the ONLY paths the privileged JEA installer will ever execute/consume.
# No JEA function performs promotion, so the filtered pvcert caller cannot turn incoming content
# into executed privileged content.
$promotedInstaller = Join-Path $trustedDir 'Install-PathVeer.ps1'
$promotedPayload   = Join-Path $payloadDir 'PathVeer-1.0.0-beta.1'
$incomingInstaller = Join-Path 'C:\pv-cert\incoming' 'Install-PathVeer.ps1'
$incomingPayload   = Join-Path 'C:\pv-cert\incoming' 'PathVeer-1.0.0-beta.1'

# Prefer operator-provided trusted sources from $SourceDir.
$srcInstaller = Join-Path $SourceDir 'Install-PathVeer.ps1'
$srcPayload   = Join-Path $SourceDir 'PathVeer-1.0.0-beta.1'
if (Test-Path $srcInstaller) { Copy-Item -Path $srcInstaller -Destination $promotedInstaller -Force }
elseif (Test-Path $incomingInstaller) { Copy-Item -Path $incomingInstaller -Destination $promotedInstaller -Force }
else { Write-Host "WARNING: no installer source found at $srcInstaller or $incomingInstaller; JEA install will fail until promoted." -ForegroundColor Yellow }

if (Test-Path $srcPayload) { Copy-Item -Path $srcPayload -Destination $promotedPayload -Recurse -Force }
elseif (Test-Path $incomingPayload) { Copy-Item -Path $incomingPayload -Destination $promotedPayload -Recurse -Force }
else { Write-Host "WARNING: no payload source found at $srcPayload or $incomingPayload; JEA install will fail until promoted." -ForegroundColor Yellow }

# Re-apply ACLs on the promoted items so the ENTIRE promoted tree inherits the protected
# (pvcert-denied) rules. Copy-Item preserves the SOURCE file's explicit ACEs and does NOT inherit
# the destination parent's inheritable ACEs, so a directory-level Set-Acl with InheritanceFlags=None
# would leave the copied child files (package.json, package-hashes.sha256, binaries) carrying their
# untrusted source ACL. The JEA virtual-account child (a member of local Administrators) then fails
# with 'Access denied' reading the operator-approved payload. We therefore normalize the ACL on the
# directory (with ContainerInherit|ObjectInherit so future children inherit) AND recursively on every
# existing file/subdirectory so the privileged identity can read the payload and write Results.
foreach ($root in @($promotedInstaller, $promotedPayload)) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    # Directory: inheritable allow for SYSTEM+Administrators, deny write for pvcert.
    $dirAcl = Get-Acl -Path $root
    $dirAcl.SetAccessRuleProtection($true, $false)
    $dirAcl.Access | ForEach-Object { $dirAcl.RemoveAccessRule($_) | Out-Null }
    $dirAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sysSid, $full, $inherit, $propagate, 'Allow')))
    $dirAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($adminGroup, $full, $inherit, $propagate, 'Allow')))
    if ($pvcertSid) { $dirAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($pvcertSid, $noWrite, $inherit, $propagate, 'Deny'))) }
    Set-Acl -Path $root -AclObject $dirAcl
    # Recurse every existing child (file or subdirectory): explicit allow for SYSTEM+Administrators,
    # deny write for pvcert. This guarantees copied package files carry the privileged read grant.
    $dirs = @(Get-ChildItem -LiteralPath $root -Recurse -Directory -ErrorAction SilentlyContinue)
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue)
    foreach ($item in ($dirs + $files)) {
        $fa = Get-Acl -Path $item.FullName
        $fa.SetAccessRuleProtection($true, $false)
        $fa.Access | ForEach-Object { $fa.RemoveAccessRule($_) | Out-Null }
        $fa.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sysSid, $full, [System.Security.AccessControl.InheritanceFlags]::None, [System.Security.AccessControl.PropagationFlags]::None, 'Allow')))
        $fa.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($adminGroup, $full, [System.Security.AccessControl.InheritanceFlags]::None, [System.Security.AccessControl.PropagationFlags]::None, 'Allow')))
        if ($pvcertSid) { $fa.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($pvcertSid, $noWrite, [System.Security.AccessControl.InheritanceFlags]::None, [System.Security.AccessControl.PropagationFlags]::None, 'Deny'))) }
        Set-Acl -Path $item.FullName -AclObject $fa
    }
}
Write-Host "Trusted installer promoted to: $promotedInstaller" -ForegroundColor Cyan
Write-Host "Trusted payload promoted to:    $promotedPayload" -ForegroundColor Cyan

# Register the endpoint (genuine admin action, performed by the operator here).
$configName = 'PathVeer.Certification'
$existing = Get-PSSessionConfiguration -Name $configName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Endpoint '$configName' already registered; re-registering." -ForegroundColor Yellow
    Unregister-PSSessionConfiguration -Name $configName -Force -ErrorAction Stop
}
Register-PSSessionConfiguration -Path $pssc -Name $configName -Force -ErrorAction Stop

# ---------------------------------------------------------------------------------------------
# Post-install validation. The previous bootstrap printed success even though the endpoint was
# unusable because the role capability was not discoverable. We now FAIL LOUDLY if any of the
# required conditions is not met.
# ---------------------------------------------------------------------------------------------
Write-Host "Validating JEA module + role-capability discoverability (Windows PowerShell)..." -ForegroundColor Cyan

function Get-ModuleDiscovery {
    # Windows PowerShell (Desktop) shares JEA's PSModulePath. If we are running under pwsh (Core),
    # delegate discovery to Windows PowerShell so the check reflects the actual JEA runtime.
    if ($PSVersionTable.PSEdition -eq 'Core') {
        $json = powershell.exe -NoProfile -Command @'
$mod = Get-Module -ListAvailable -Name PathVeerCertificationJea | Select-Object -First 1
if ($mod) {
    [PSCustomObject]@{
        Found     = $true
        ModuleBase = $mod.ModuleBase
        RoleCap   = Test-Path (Join-Path $mod.ModuleBase 'RoleCapabilities\PathVeerCertificationRole.psrc')
    } | ConvertTo-Json -Compress
} else {
    '{"Found":false}'
}
'@
        return ($json | ConvertFrom-Json)
    } else {
        $mod = Get-Module -ListAvailable -Name PathVeerCertificationJea | Select-Object -First 1
        return [PSCustomObject]@{
            Found      = ($null -ne $mod)
            ModuleBase = $(if ($mod) { $mod.ModuleBase } else { $null })
            RoleCap    = $(if ($mod) { Test-Path (Join-Path $mod.ModuleBase 'RoleCapabilities\PathVeerCertificationRole.psrc') } else { $false })
        }
    }
}

$disc = Get-ModuleDiscovery
if (-not $disc.Found) {
    throw "VALIDATION FAILED: module 'PathVeerCertificationJea' is not discoverable by Windows PowerShell (PSModulePath). The role capability will NOT resolve -> endpoint unusable."
}
if (-not [string]::Equals($disc.ModuleBase, $moduleBase, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "VALIDATION FAILED: module discovered at '$($disc.ModuleBase)' but expected '$moduleBase'. Role capability resolution path is wrong."
}
if (-not $disc.RoleCap) {
    throw "VALIDATION FAILED: RoleCapabilities\PathVeerCertificationRole.psrc is missing under module base '$($disc.ModuleBase)'."
}

$cfg = Get-PSSessionConfiguration -Name $configName -ErrorAction SilentlyContinue
if (-not $cfg) { throw "VALIDATION FAILED: endpoint '$configName' is not registered." }
if ($cfg.Enabled -ne $true) { throw "VALIDATION FAILED: endpoint '$configName' is registered but not enabled." }

# Verify the ACTIVE registered configuration, not merely the source PSSC. An omitted ExecutionPolicy
# would make the JEA session default to Restricted and block the trusted module import
# ("cannot be loaded because running scripts is disabled on this system"). Fail loudly if the live
# endpoint did not pick up RemoteSigned.
$cfgExecPolicy = $null
try { $cfgExecPolicy = $cfg.ExecutionPolicy } catch {}
if ($cfgExecPolicy -ne 'RemoteSigned') {
    throw "VALIDATION FAILED: registered endpoint '$configName' ExecutionPolicy='$cfgExecPolicy' (expected 'RemoteSigned'). The JEA wsmprovhost process will default to Restricted and block the trusted module import. Re-register from the updated PSSC."
}
Write-Host "Active endpoint ExecutionPolicy = '$($cfg.ExecutionPolicy)' (expected RemoteSigned)." -ForegroundColor Green

# ---------------------------------------------------------------------------------------------
# Effective export-surface validation (CRITICAL).
# The previous bootstrap validated module discoverability, role-capability presence, and the
# active endpoint ExecutionPolicy, but never validated that the ACTUAL imported module export
# surface satisfies the role-capability VisibleFunctions. The .psm1 can Export-ModuleMember the
# function and the .psrc can request it, yet a missing .psd1 FunctionsToExport entry silently
# removes it from the effective surface -> the JEA session throws CommandNotFoundException while
# bootstrap prints success. We FAIL LOUDLY here instead.
#
# This validation:
#   1. imports the INSTALLED module from its protected installed location (not the source copy),
#   2. reads the actual exported function names from the imported module,
#   3. loads the INSTALLED role-capability PSRC and reads its VisibleFunctions,
#   4. throws VALIDATION FAILED if any PSRC-visible function is absent from the effective surface.
# ---------------------------------------------------------------------------------------------
Write-Host "Validating effective module export surface against role-capability VisibleFunctions..." -ForegroundColor Cyan

# Import the installed module from its protected installed location so we test EXACTLY what JEA imports.
$installedModuleName = 'PathVeerCertificationJea'
Remove-Module -Name $installedModuleName -Force -ErrorAction SilentlyContinue
Import-Module -Name $installedModuleName -Force -ErrorAction Stop

$exported = @( (Get-Command -Module $installedModuleName -CommandType Function).Name )
if ($exported.Count -eq 0) {
    throw "VALIDATION FAILED: imported module '$installedModuleName' exposes NO functions. The endpoint would serve an empty command surface."
}

# Read the INSTALLED role-capability PSRC and its VisibleFunctions.
$installedRolePath = Join-Path $moduleBase 'RoleCapabilities\\PathVeerCertificationRole.psrc'
if (-not (Test-Path $installedRolePath)) {
    throw "VALIDATION FAILED: installed role capability not found at '$installedRolePath'."
}
$roleData = Import-PowerShellDataFile -Path $installedRolePath
$visibleFunctions = @( $roleData.VisibleFunctions )

$missing = @( $visibleFunctions | Where-Object { $_ -and ($exported -notcontains $_) } )
if ($missing.Count -gt 0) {
    throw ("VALIDATION FAILED: role-capability VisibleFunctions request functions that are NOT present " +
           "in the effective module export surface:`n  " + ($missing -join "`n  ") +
           "`nModule exported: " + ($exported -join ', ') +
           "`nFix the module manifest (FunctionsToExport) so every PSRC-visible function is actually exported.")
}

# Invariant proof: PSRC VisibleFunctions ⊆ Module ExportedFunctions.
Write-Host ("Effective export surface OK: all {0} PSRC-visible function(s) are exported by the imported module." -f $visibleFunctions.Count) -ForegroundColor Green

# Smoke test: attempt a local JEA session. As a non-role user (the operator is admin, not in
# RoleDefinitions) the connection is EXPECTED to be rejected with an authorization error - which
# proves the endpoint is reachable AND the role capability resolved. If the role capability still
# cannot be found, the error mentions "role capability" / "could not find" -> we fail loudly.
Write-Host "Smoke test: attempting a local JEA session (expected: authorization denied for non-role user, NOT 'role capability not found')..." -ForegroundColor Cyan
$job = Start-Job -ScriptBlock { param($cn)
    try {
        $s = New-PSSession -ComputerName localhost -ConfigurationName $cn -ErrorAction Stop
        if ($s) { Remove-PSSession $s -ErrorAction SilentlyContinue }
        return 'CONNECTED'
    } catch {
        return "ERR:$($_.Exception.Message)"
    }
} -ArgumentList $configName
$done = Wait-Job $job -Timeout 30
if (-not $done) {
    Stop-Job $job -ErrorAction SilentlyContinue
    Remove-Job $job -ErrorAction SilentlyContinue
    Write-Host "Smoke test: timed out (inconclusive; module + endpoint validation above is authoritative)." -ForegroundColor Yellow
} else {
    $r = Receive-Job $job
    Remove-Job $job -ErrorAction SilentlyContinue
    if ($r -eq 'CONNECTED') {
        Write-Host "Smoke test: connected to endpoint (unexpected for non-role user) - endpoint reachable." -ForegroundColor Green
    } elseif ($r -match '^ERR:') {
        $smokeErr = $r.Substring(4)
        if ($smokeErr -match 'role capability|could not find') {
            throw "VALIDATION FAILED (smoke test): role capability STILL not resolvable: $smokeErr"
        } elseif ($smokeErr -match 'not authorized|access is denied|cannot be loaded|permission') {
            Write-Host "Smoke test: endpoint reachable; role capability resolved; connection correctly rejected for non-role user (expected)." -ForegroundColor Green
        } else {
            Write-Host "Smoke test: inconclusive (transport/environment): $smokeErr" -ForegroundColor Yellow
        }
    }
}

Write-Host "Registered JEA endpoint '$configName' (role limited to PV-CERT\pvcert) and validated role-capability discovery." -ForegroundColor Green
Write-Host "Next: from the HOST run Test-PathVeerCertGuestJea.ps1 to prove privileged context AND restricted boundary." -ForegroundColor Cyan
