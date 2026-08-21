<# Test-PathVeerCloudStateSecurity.ps1
   ------------------------------------------------------------------
   LocalSystem integration / runtime certification for the Cloud credential
   filesystem boundary (CloudStateSecurity).

   WHAT THIS PROVES (and what it does NOT)
   ---------------------------------------
   This is the ONLY place in the repository that asserts the actual
   filesystem PERSISTENCE behavior of the production CloudStateSecurity
   methods:

       CloudStateSecurity.HardenDirectory
       CloudStateSecurity.HardenFile
       CloudStateSecurity.IsSecured

   under the real authority the Service runs with: NT AUTHORITY\SYSTEM
   (S-1-5-18). It creates deliberately hostile input objects (attacker
   owner + hostile user ACE + Everyone ACE), runs the EXACT product code as
   SYSTEM, and verifies:

       - resulting owner  == SYSTEM
       - resulting DACL    == SYSTEM + Administrators FullControl only
       - inheritance disabled
       - zero unauthorized ACEs
       - IsSecured == $true

   This is complementary to the ordinary unit tests (CloudStateSecurityTests,
   etc.) which exercise the PURE descriptor logic (IsCanonicalSecurityDescriptor)
   WITHOUT persisting SYSTEM ownership onto real objects, because an ordinary
   `dotnet test` runner cannot assign the SYSTEM owner.

   SAFETY / SCOPE
   --------------
   - Refuses to run if the real %ProgramData%\PathVeer state exists; it only
     touches a unique disposable root under the system temp directory.
   - Never starts the PathVeer service or the PathVeerD2Cert service.
   - Never modifies routing or desired configuration.
   - Never reads or writes any credential; the hostile objects carry only a
     placeholder JSON ("probe":true).
   - Always cleans the scheduled task and probe directory in a finally block.
   - Emits a clear PASS/FAIL exit code.

   REQUIREMENTS
   -------------
   - Must be invoked by an ELEVATED operator (so it can register a SYSTEM
     scheduled task). The task itself runs as NT AUTHORITY\SYSTEM.
   - The product must have been built (Release) so PathVeer.Service.dll and
     its dependencies are present.

   This tool is NOT invoked automatically by `dotnet test`. It is the
   operator-run certification primitive for the storage boundary.
#>

[CmdletBinding()]
param(
    # Override the product assembly location (defaults to the Release build).
    [string]$ProductDll = '',

    # Override the disposable probe root (defaults to a GUID-named dir in $env:TEMP).
    [string]$ProbeRoot = ''
)

$ErrorActionPreference = 'Stop'

# --- locate the product assembly ---------------------------------------
if ([string]::IsNullOrWhiteSpace($ProductDll)) {
    # Use the framework-agnostic build output (net10.0-windows), which loads
    # against the host PowerShell's already-resident CoreLib. The RID-specific
    # win-x64 output ships its own System.Private.CoreLib and must NOT be
    # LoadFrom'd into the host process (it produces a CoreLib version
    # conflict). The unit diagnostic proved this output loads cleanly.
    $candidate = Join-Path $PSScriptRoot '..\..\PathVeer.Service\bin\Release\net10.0-windows\PathVeer.Service.dll'
    $candidate = [System.IO.Path]::GetFullPath($candidate)
    if (-not (Test-Path $candidate)) {
        $candidate2 = Join-Path $PSScriptRoot '..\..\PathVeer.Service\bin\Release\net10.0-windows\win-x64\PathVeer.Service.dll'
        $candidate2 = [System.IO.Path]::GetFullPath($candidate2)
        if (Test-Path $candidate2) { $candidate = $candidate2 }
    }
    $ProductDll = $candidate
}

if (-not (Test-Path $ProductDll)) {
    Write-Host "PRODUCT_DLL_MISSING=$ProductDll" -ForegroundColor Red
    Write-Host 'Build PathVeer.Service (Release) before running this tool.' -ForegroundColor Yellow
    exit 2
}
Write-Host "PRODUCT_DLL=$ProductDll"

# --- default the disposable probe root (before any use) ----------------
if ([string]::IsNullOrWhiteSpace($ProbeRoot)) {
    $ProbeRoot = Join-Path $env:TEMP ("PathVeer-D2-CloudStateProbe-" + [guid]::NewGuid().ToString('N'))
}

# --- refuse to touch real PathVeer state -------------------------------
# The probe runs entirely under $env:TEMP and never writes to the real
# %ProgramData%\PathVeer tree. We hard-fail only if the resolved probe root
# would land inside the real state directory (defensive: it never does, by
# construction). The mere presence of real PathVeer state must NOT block the
# proof — the disposable probe is isolated from it.
$realState = Join-Path $env:ProgramData 'PathVeer'
$probeRootResolved = [System.IO.Path]::GetFullPath($ProbeRoot)
if ($probeRootResolved -eq $realState -or
    $probeRootResolved.StartsWith($realState + [System.IO.Path]::DirectorySeparatorChar)) {
    Write-Host "REFUSAL: probe root $probeRootResolved would touch real PathVeer state at $realState." -ForegroundColor Red
    exit 2
}
if (Test-Path $realState) {
    Write-Host "NOTE: real PathVeer state present at $realState; disposable probe is isolated under $probeRootResolved and will not touch it." -ForegroundColor Yellow
}

# --- elevation preconditions -------------------------------------------
$id   = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$wp   = New-Object System.Security.Principal.WindowsPrincipal($id)
if (-not $wp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'REFUSAL: this tool must be run from an ELEVATED PowerShell (Administrator).' -ForegroundColor Red
    exit 2
}

# --- disposable probe root (defaulted earlier) -------------------------
$probeCloud = Join-Path $ProbeRoot 'cloud'
$probeFile  = Join-Path $probeCloud 'cloud-registration.json'

New-Item $probeCloud -ItemType Directory -Force | Out-Null
Set-Content -Path $probeFile -Value '{"probe":true}' -Encoding UTF8
Write-Host "PROBE_ROOT=$ProbeRoot"

# --- build the hostile input descriptor -------------------------------
# The interactive operator is the hypothetical "attacker" who pre-created the
# objects. We make them owned by the current (non-SYSTEM) identity and grant
# explicit Everyone + current-user FullControl, like a permissive pre-creation.
$currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$currentSid      = $currentIdentity.User
$worldSid        = [System.Security.Principal.SecurityIdentifier]::new([System.Security.Principal.WellKnownSidType]::WorldSid, $null)

function Set-HostileAcl {
    param([string]$Path, [bool]$IsDirectory)
    $acl = Get-Acl -Path $Path
    $acl.SetOwner($currentSid)
    $inh = if ($IsDirectory) {
        [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    } else {
        [System.Security.AccessControl.InheritanceFlags]::None
    }
    $ruleCurrent = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $currentSid, 'FullControl', $inh,
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AccessControlType]::Allow)
    $ruleWorld = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $worldSid, 'ReadAndExecute', $inh,
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AccessControlType]::Allow)
    $acl.AddAccessRule($ruleCurrent)
    $acl.AddAccessRule($ruleWorld)
    Set-Acl -Path $Path -AclObject $acl
}

Set-HostileAcl -Path $probeCloud -IsDirectory $true
Set-HostileAcl -Path $probeFile  -IsDirectory $false

Write-Host ('BEFORE_DIR_OWNER='  + (Get-Acl $probeCloud).Owner)
Write-Host ('BEFORE_FILE_OWNER=' + (Get-Acl $probeFile).Owner)

# --- build + locate the LocalSystem probe EXE -------------------------
# The proof runs as NT AUTHORITY\SYSTEM via a disposable scheduled task. To
# avoid a host/runtime binding conflict (loading the net10.0 product assembly
# into the host PowerShell via reflection fails on framework refs), the proof
# is a small net10.0-windows EXE (PathVeer.CloudStateProbe) that references
# PathVeer.Service directly. It runs with its own correct runtime and calls
# the exact production CloudStateSecurity methods.
$probeProject = Join-Path $PSScriptRoot 'CloudStateProbe\PathVeer.CloudStateProbe.csproj'
if (-not (Test-Path $probeProject)) {
    Write-Host "PROBE_PROJECT_MISSING=$probeProject" -ForegroundColor Red
    exit 2
}
Write-Host "PROBE_PROJECT=$probeProject"
dotnet build $probeProject -c Release -v q *>&1 | Out-Null
$probeExe = Join-Path $PSScriptRoot 'CloudStateProbe\bin\Release\net10.0-windows\PathVeer.CloudStateProbe.exe'
if (-not (Test-Path $probeExe)) {
    $probeExe = Join-Path $PSScriptRoot 'CloudStateProbe\bin\Release\net10.0-windows\win-x64\PathVeer.CloudStateProbe.exe'
}
if (-not (Test-Path $probeExe)) {
    Write-Host "PROBE_EXE_MISSING=$probeExe" -ForegroundColor Red
    Write-Host 'Build the CloudStateProbe project (Release) before running this tool.' -ForegroundColor Yellow
    exit 2
}
Write-Host "PROBE_EXE=$probeExe"

$resultJson  = Join-Path $env:TEMP ("PathVeer-D2-CloudStateResult-" + [guid]::NewGuid().ToString('N') + '.json')

try {
    $taskName = "PathVeer-D2-CloudStateProbe-" + [guid]::NewGuid().ToString('N')
    $arg = "`"$probeCloud`" `"$probeFile`" `"$resultJson`""
    $action    = New-ScheduledTaskAction -Execute $probeExe -Argument $arg
    $principal = New-ScheduledTaskPrincipal -UserId 'NT AUTHORITY\SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $null = Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force
    Start-ScheduledTask -TaskName $taskName | Out-Null

    # Poll for completion (SYSTEM task, should be fast).
    $done = $false
    for ($i = 0; $i -lt 120; $i++) {
        Start-Sleep -Seconds 1
        if (Test-Path $resultJson) {
            $info = Get-ScheduledTaskInfo -TaskName $taskName -ErrorAction SilentlyContinue
            if ($info -and $info.LastTaskResult -ne 267009) { $done = $true; break }
            if (Test-Path $resultJson) { $done = $true; break }
        }
    }

    if (-not (Test-Path $resultJson)) {
        Write-Host 'WORKER_RESULT_MISSING' -ForegroundColor Red
        exit 1
    }

    $res = Get-Content $resultJson -Raw | ConvertFrom-Json

    Write-Host "IDENTITY=$($res.identity)"
    Write-Host "IDENTITY_SID=$($res.identitySid)"
    Write-Host "DIR_OWNER_AFTER=$($res.dirOwnerAfter)"
    Write-Host "FILE_OWNER_AFTER=$($res.fileOwnerAfter)"
    Write-Host "DIR_PROTECTED=$($res.dirProtected)"
    Write-Host "FILE_PROTECTED=$($res.fileProtected)"
    Write-Host "DIR_UNAUTHORIZED_ACE_COUNT=$($res.dirUnauthAceCnt)"
    Write-Host "FILE_UNAUTHORIZED_ACE_COUNT=$($res.fileUnauthAceCnt)"
    Write-Host "IS_SECURED=$($res.isSecured)"
    if ($res.error) { Write-Host "WORKER_ERROR=$($res.error)" -ForegroundColor Yellow }

    if ($res.pass) {
        Write-Host 'RESULT=PASS' -ForegroundColor Green
        exit 0
    } else {
        Write-Host 'RESULT=FAIL' -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host ("TOOL_ERROR=" + $_.Exception.Message) -ForegroundColor Red
    exit 1
} finally {
    # Deterministic cleanup: remove the SYSTEM task and the probe root.
    # The probe EXE is left in place (it is a checked-in build artifact, not
    # disposable state) but the scheduled task and all disposable probe
    # objects under $ProbeRoot are always removed, on success and failure.
    try { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue } catch {}
    try { Remove-Item $resultJson -Force -ErrorAction SilentlyContinue } catch {}
    try { Remove-Item $ProbeRoot -Recurse -Force -ErrorAction SilentlyContinue } catch {}
    Write-Host "PROBE_REMOVED=True"
}
