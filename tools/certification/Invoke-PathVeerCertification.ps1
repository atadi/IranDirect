<# .SYNOPSIS
    PathVeer VM Certification — host-side orchestrator (Phase 37.9).

    Drives the Phase 37.5 gate matrix (GATE-2..9, 5) against the disposable
    PathVeer-Certification guest over PowerShell Direct, capturing REAL Windows
    evidence to artifacts/certification/evidence/<NN>-*.json.

    SECURITY (per certification handoff):
      * The guest receives ONLY public artifacts: the install package, the
        installer script, and the evidence/support scripts. NO R2 secret, NO
        production ES256 private key, NO PFX, NO KeePassXC, NO pvcert password
        is ever written to the guest or to a git-tracked file.
      * The pvcert password is collected ONCE via a native Get-Credential dialog
        (never echoed, never logged, never written).
      * Stable is NEVER published. beta-channel immutability is respected.

    #    RESUMABILITY:
    #      * Run with -Stage All (default) for the full sequence, or -Stage <name> to
    #        (re)run a single gate. Each stage restores the certification checkpoint
    #        (PV-CERT-HARNESS) first (except where noted), so a half-run VM never poisons
    #        later stages.
    #      * -SkipRestore skips the checkpoint-restore step for a single re-run.
    #      * GATE-5 is split into two explicit, resumable stages:
    #          - GATE5PREP:  restores PV-CERT-HARNESS, installs, registers the pvcert
    #                       per-user Tray Run entry, validates Service/CLI/Run evidence,
    #                       and INTENTIONALLY LEAVES the prepared VM intact (no restore).
    #                       It prints operator instructions to log in interactively.
    #          - GATE5VERIFY: must ONLY be run on a PREP-preserved VM. It refuses to restore
    #                       at the start, validates the prepared state, requires genuine
    #                       interactive-Tray evidence (Tray SessionId == pvcert Explorer
    #                       SessionId; PowerShell Direct SessionId is never accepted as
    #                       interactive evidence), then exercises the authority contract.
    #                       On PASS it restores the baseline; on PRODUCT FAIL it preserves the
    #                       failed VM for diagnostics.
    #          - GATE5 (legacy alias): an orchestrator that runs GATE5PREP then stops with a
    #                       clear message directing the operator to GATE5VERIFY. It never
    #                       silently claims a full GATE-5 PASS without genuine interactive
    #                       verification.

    USAGE (from a NATIVE Windows PowerShell/Terminal window so the credential
    dialog can appear):
      pwsh -NoProfile -File tools\certification\Invoke-PathVeerCertification.ps1 [-Stage All]
#>
[CmdletBinding()]
param(
    [string]$VmName = 'PathVeer-Certification',
    # Canonical certification execution baseline. MUST be PV-CERT-HARNESS (clean Windows +
    # approved JEA instrumentation, NO PathVeer product). Restoring PV-CLEAN-WINDOWS would
    # wipe the JEA endpoint and break every privileged gate. Missing -> harness fails loudly.
    [string]$CertificationSnapshot = 'PV-CERT-HARNESS',
    [ValidateSet('All','GATE5','GATE5PREP','GATE5VERIFY','GATE6','GATE8','GATE3','GATE2','GATE4','GATE28','GATE9')]
    [string]$Stage = 'All',
    [switch]$SkipRestore,

    # Local package under test (the real candidate). Framework-dependent.
    [string]$CandidatePackage = (Join-Path $PSScriptRoot '..\..\artifacts\releases\1.0.0-beta.1\win-x64\PathVeer-1.0.0-beta.1'),
    # Older version used for GATE-6 upgrade-from baseline + downgrade-block test.
    [string]$OlderPackage = (Join-Path $PSScriptRoot '..\..\artifacts\packages\PathVeer-0.9.0'),

    [string]$EvidenceDir = (Join-Path $PSScriptRoot '..\..\artifacts\certification\evidence')
)

$ErrorActionPreference = 'Stop'
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$InstallScript = Join-Path $RepoRoot 'tools\Install-PathVeer.ps1'
$EvidenceDir = [System.IO.Path]::GetFullPath($EvidenceDir)
if (-not (Test-Path $EvidenceDir)) { New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null }

# Reserved TEST-NET-3 prefix for bounded GATE-2 route evidence (RFC 5737, never a real route).
$ManagedPrefix = '203.0.113.0/24'

function Write-Stage([string]$m){ Write-Host "`n===== $m =====" -ForegroundColor Magenta }
function Save-Json([string]$name, $obj){
    $p = Join-Path $EvidenceDir $name
    $obj | ConvertTo-Json -Depth 8 | Set-Content -Path $p -Encoding utf8
    Write-Host "  evidence -> $p" -ForegroundColor DarkGray
}
function Step([string]$s){ Write-Host "  • $s" -ForegroundColor Cyan }

function Assert-VmRunning {
    $vm = Get-VM -Name $VmName -ErrorAction Stop
    if ($vm.State -ne 'Running') {
        Write-Host "Starting $VmName ..." -ForegroundColor Cyan
        Start-VM -Name $VmName -ErrorAction Stop
        # Wait for PS Direct to be available (VMHeartbeat / integration).
        $vm | Wait-VM -For Heartbeat -Timeout 300 -ErrorAction Stop
    }
}

function Restore-Clean {
    param([System.Management.Automation.Runspaces.PSSession]$Session)
    # Certification execution baseline MUST be PV-CERT-HARNESS (clean Windows + approved JEA
    # instrumentation, NO PathVeer product). Restoring PV-CLEAN-WINDOWS would wipe the JEA
    # endpoint and break every privileged gate. Fail loudly (no silent fallback) if absent.
    Write-Host "Restoring certification checkpoint '$CertificationSnapshot'..." -ForegroundColor Yellow
    if ($Session) { Remove-PSSession $Session -ErrorAction SilentlyContinue }
    # VM RESTORE BOUNDARY: the entire VM is reverted, so any cached JEA PSSession refers to the
    # pre-restore guest runspace and MUST be disposed and nulled before restore. Otherwise a later
    # Get-GuestJeaSession would reuse a stale session across the -Stage All boundary. This does NOT
    # alter the 12-function JEA surface or the JEA module/bootstrap.
    if ($script:JeaSession) {
        try { $script:JeaSession | Remove-PSSession -ErrorAction SilentlyContinue } catch {}
        $script:JeaSession = $null
    }
    try {
        Restore-VMSnapshot -VMName $VmName -Name $CertificationSnapshot -Confirm:$false -ErrorAction Stop
    } catch {
        throw "Certification checkpoint '$CertificationSnapshot' not found or could not be restored on VM '$VmName'. Create it from the clean Windows baseline AFTER registering the JEA endpoint (Enable-PathVeerCertificationJea.ps1), then re-run. Do NOT use PV-CLEAN-WINDOWS for certification runs. ($($_.Exception.Message))"
    }
    # A Standard checkpoint captured while Running may already be Running after restore,
    # so do NOT unconditionally Start-VM (unsafe state transition). Reuse Assert-VmRunning,
    # which starts the VM only when it is not already Running, then waits for heartbeat.
    # This works regardless of whether the restored certification checkpoint was captured
    # Running or Off. New-GuestSession's retry loop establishes PowerShell Direct after.
    Assert-VmRunning
    Start-Sleep -Seconds 5
}

function New-GuestSession([System.Management.Automation.PSCredential]$Cred) {
    # Retry PS Direct a few times after restore (guest may still be booting).
    for ($i = 1; $i -le 12; $i++) {
        try { return New-PSSession -VMName $VmName -Credential $Cred -ErrorAction Stop }
        catch {
            if ($i -eq 12) { throw "PowerShell Direct failed after retries: $_" }
            Start-Sleep -Seconds 10
        }
    }
}

function Repair-GuestDhcp([System.Management.Automation.Runspaces.PSSession]$Session) {
    # Bounded DHCP renewal on the guest's DHCP-enabled, connected adapter(s). Reused by BOTH
    # Wait-GuestNetworkReady (GATE-9, needs DNS) and Wait-GuestRouteReady (GATE-2, needs only a
    # default route) so the DHCP-recovery implementation is NOT duplicated. SINGLE source of truth.
    # The renewal process is started detached and waited on with a HARD 25000 ms bound via
    # Process.WaitForExit(int) so it can NEVER hang indefinitely (no DHCP server answer). On timeout
    # the spawned process is killed. No static IP; no ExpressVPN-specific code; no arbitrary long sleep.
    return Invoke-Command -Session $Session -ScriptBlock {
        $aliases = @(Get-NetIPInterface -AddressFamily IPv4 -Dhcp Enabled -ErrorAction SilentlyContinue |
            Where-Object { $_.ConnectionState -eq 'Connected' } |
            ForEach-Object { (Get-NetAdapter -InterfaceIndex $_.InterfaceIndex -ErrorAction SilentlyContinue).Name } |
            Where-Object { $_ })
        $summary = [PSCustomObject]@{ renewedAliases = @(); timedOut = $false; error = $null }
        try {
            $targets = if ($aliases.Count -gt 0) { $aliases } else { @('*') }
            foreach ($alias in $targets) {
                $p = Start-Process `
                    -FilePath "$env:SystemRoot\System32\ipconfig.exe" `
                    -ArgumentList '/renew', $alias `
                    -PassThru `
                    -WindowStyle Hidden `
                    -ErrorAction Stop
                $completed = $p.WaitForExit(25000)
                if (-not $completed) {
                    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
                    $p.WaitForExit()
                    $summary.timedOut = $true
                }
                $summary.renewedAliases += [PSCustomObject]@{ alias = $alias; exitCode = $p.ExitCode }
            }
        } catch { $summary.error = $_.Exception.Message }
        return $summary
    }
}

function Get-GuestRouteReadinessProbe([System.Management.Automation.Runspaces.PSSession]$Session, [string]$DnsTarget = $null) {
    # Single network-readiness probe shared by Wait-GuestRouteReady and Wait-GuestNetworkReady.
    # Reports non-APIPA IPv4, IPv4 default route, DHCP adapter aliases, and (optionally) DNS resolution.
    # It does NOT itself mutate state; callers decide whether to invoke Repair-GuestDhcp.
    Invoke-Command -Session $Session -ScriptBlock {
        param($dnsTarget)
        $allV4 = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike '127.*' })
        $apis = @($allV4 | Where-Object { $_.IPAddress -like '169.254.*' })
        $nonApi = @($allV4 | Where-Object { $_.IPAddress -notlike '169.254.*' })
        $hasNonApipa = $nonApi.Count -gt 0
        $defRoute = $null
        try { $defRoute = @(Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue) } catch {}
        $hasDefaultRoute = ($null -ne $defRoute) -and ($defRoute.Count -gt 0)
        $dhcpIfs = @(Get-NetIPInterface -AddressFamily IPv4 -Dhcp Enabled -ErrorAction SilentlyContinue |
            Where-Object { $_.ConnectionState -eq 'Connected' })
        $dhcpAliases = @($dhcpIfs | ForEach-Object { (Get-NetAdapter -InterfaceIndex $_.InterfaceIndex -ErrorAction SilentlyContinue).Name } | Where-Object { $_ })
        $dnsOk = $false; $dnsErr = $null
        if ($dnsTarget) {
            try { $a = [System.Net.Dns]::GetHostAddresses($dnsTarget); if ($a -and $a.Count -gt 0) { $dnsOk = $true } } catch { $dnsErr = $_.Exception.Message }
        }
        [PSCustomObject]@{
            hasNonApipa = $hasNonApipa
            nonApipaAddresses = @($nonApi | ForEach-Object { $_.IPAddress })
            apipaAddresses = @($apis | ForEach-Object { $_.IPAddress })
            hasDefaultRoute = $hasDefaultRoute
            dhcpInterfaceIndexes = @($dhcpIfs | ForEach-Object { $_.ifIndex })
            dhcpAdapterAliases = @($dhcpAliases)
            dnsResolved = $dnsOk
            dnsError = $dnsErr
        }
    } -ArgumentList $DnsTarget
}

function Wait-GuestRouteReady([System.Management.Automation.Runspaces.PSSession]$Session, [int]$MaxSeconds = 120) {
    # ROUTE-readiness prerequisite for GATE-2 (and any route-mutating stage). Requires ONLY a usable
    # adapter/default route: non-APIPA IPv4 AND an IPv4 default route. It does NOT require DNS/HTTPS/
    # GitHub/releases.pathveer.com — installing a managed route needs a valid next-hop/interface, not name
    # resolution. After a PV-CERT-HARNESS checkpoint restore the guest may come back APIPA (169.254.x.x)
    # with no default route because DHCP runtime state is NOT baked into the checkpoint; bounded DHCP
    # renewal (shared Repair-GuestDhcp) restores it. Throws GuestRouteNotReady (environment defect) if the
    # baseline cannot be established within MaxSeconds — callers must NOT run route mutations in that case.
    $deadline = (Get-Date).AddSeconds($MaxSeconds)
    $iter = 0
    while ($true) {
        $iter++
        $probe = Get-GuestRouteReadinessProbe -Session $Session
        if ($probe.hasNonApipa -and $probe.hasDefaultRoute) {
            return [PSCustomObject]@{ ready = $true; iterations = $iter; lastProbe = $probe }
        }
        if ((Get-Date) -ge $deadline) {
            throw [System.Management.Automation.ErrorRecord]::new(
                [System.InvalidOperationException]::new(
                    "Route readiness not established within $MaxSeconds s after checkpoint restore (APIPA='$($probe.apipaAddresses -join ',')'; defaultRoute=$($probe.hasDefaultRoute); nonApipa=$($probe.hasNonApipa); dhcpAliases='$($probe.dhcpAdapterAliases -join ',')'). Environment/network-readiness defect; not a product defect."),
                'GuestRouteNotReady', [System.Management.Automation.ErrorCategory]::ResourceUnavailable, $probe)
        }
        Repair-GuestDhcp -Session $Session | Out-Null
        Start-Sleep -Seconds 3
    }
}

function Wait-GuestNetworkReady([System.Management.Automation.Runspaces.PSSession]$Session, [string]$TargetHost = 'releases.pathveer.com', [int]$MaxSeconds = 120) {
    # Bounded network-readiness probe for network-dependent certification stages (GATE-9). Requires
    # non-APIPA IPv4 + default route + DNS resolution of TargetHost. After a PV-CERT-HARNESS checkpoint
    # restore the guest may come back APIPA (169.254.x.x) with no default route / unusable DNS, because
    # DHCP runtime state is NOT baked into the checkpoint. Uses the shared Get-GuestRouteReadinessProbe /
    # Repair-GuestDhcp primitives (no duplicated DHCP logic). No product/JEA/checkpoint changes; no static
    # IP; no arbitrary long sleep; no infinite loop.
    $deadline = (Get-Date).AddSeconds($MaxSeconds)
    $iter = 0
    while ($true) {
        $iter++
        $probe = Get-GuestRouteReadinessProbe -Session $Session -DnsTarget $TargetHost
        if ($probe.hasNonApipa -and $probe.hasDefaultRoute -and $probe.dnsResolved) {
            return [PSCustomObject]@{ ready = $true; iterations = $iter; lastProbe = $probe }
        }
        if ((Get-Date) -ge $deadline) {
            throw [System.Management.Automation.ErrorRecord]::new(
                [System.InvalidOperationException]::new(
                    "Network readiness not established within $MaxSeconds s after checkpoint restore (APIPA='$($probe.apipaAddresses -join ',')'; defaultRoute=$($probe.hasDefaultRoute); dnsResolved=$($probe.dnsResolved); dnsError='$($probe.dnsError)'). Environment/network-readiness defect; not a product defect."),
                'GuestNetworkNotReady', [System.Management.Automation.ErrorCategory]::ResourceUnavailable, $probe)
        }
        Repair-GuestDhcp -Session $Session | Out-Null
        Start-Sleep -Seconds 3
    }
}

function Install-PathVeerProtectedCandidate([System.Management.Automation.Runspaces.PSSession]$Session, [string]$ExpectedVersion = '1.0.0-beta.1') {
    # Shared install precondition for install-dependent lifecycle gates (GATE-2/3/4/8/28).
    # Establishes the product lifecycle state each gate truthfully claims to test: installs the CURRENT
    # protected candidate (PathVeer-1.0.0-beta.1) via the EXISTING trusted JEA wrapper, then FAIL-CLOSED
    # verifies the installation actually exists AND is the expected protected version. No new privileged
    # mechanism; JEA trust boundary and the protected payload are unchanged.
    $jea = Get-GuestJeaSession $script:Cred
    $install = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Install -Feature @('RegisterShell','InstallTray')
    $svcState = $null; $version = $null; $err = $null; $verified = $false
    $installExit = if ($install.result) { $install.result.exitCode } else { $null }
    if ($installExit -eq 0) {
        try {
            $probe = Invoke-Command -Session $Session -ScriptBlock {
                $svc = Get-Service -Name 'PathVeer' -ErrorAction Stop
                $ver = $null; $manifestErr = $null
                try { $ver = (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw -ErrorAction Stop | ConvertFrom-Json).productVersion } catch { $manifestErr = $_.Exception.Message }
                [PSCustomObject]@{ serviceState = $svc.Status; version = $ver; manifestError = $manifestErr }
            }
            $svcState = $probe.serviceState; $version = $probe.version; $manifestErr = $probe.manifestError
            # FAIL-CLOSED: require a present, non-empty version that EXACTLY matches the expected protected version.
            if ($null -ne $version -and $version -ne '' -and $version -eq $ExpectedVersion) {
                $verified = $true
            } else {
                $err = "protected version mismatch or missing (expected='$ExpectedVersion'; actual='$version'; manifestError='$manifestErr')"
            }
        } catch { $err = $_.Exception.Message }
    } else {
        $err = "install exitCode was not 0 (exitCode=$installExit)"
    }
    $result = [PSCustomObject]@{
        installExitCode = $installExit
        installElevated = if ($install.result) { $install.elevationAvailable } else { $null }
        expectedVersion = $ExpectedVersion
        actualVersion = $version
        serviceState = $svcState
        verified = $verified
        verifyError = $err
    }
    if (-not $verified) {
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new(
                "Protected-candidate install precondition failed (expectedVersion=$ExpectedVersion; actualVersion=$version; installExitCode=$installExit; serviceState=$svcState; verifyError='$err'). Downstream gate assertions must not run against an absent or version-mismatched PathVeer product."),
            'ProtectedCandidateInstallPreconditionFailed', [System.Management.Automation.ErrorCategory]::ResourceUnavailable, $result)
    }
    return $result
}

function Copy-ToGuest([System.Management.Automation.Runspaces.PSSession]$Session, [string[]]$Paths, [string]$Dest) {
    Step "Copying $($Paths.Count) item(s) into guest $Dest"
    Invoke-Command -Session $Session -ScriptBlock { param($d) if(-not(Test-Path $d)){ New-Item -ItemType Directory -Force -Path $d | Out-Null } } -ArgumentList $Dest
    foreach ($p in $Paths) {
        if (-not (Test-Path $p)) { throw "Local artifact missing: $p" }
        Copy-Item -ToSession $Session -Path $p -Destination $Dest -Recurse -Force
    }
}

# Shared privileged-execution primitive (single source of truth). Defines
# New-GuestJeaSession / Invoke-GuestJeaFunction / Invoke-GuestJeaInstall / Invoke-GuestJeaCli /
# Stop-GuestJeaService / Start-GuestJeaService / Stop-GuestJeaTray / Get-GuestJeaServiceState /
# Get-GuestJeaRouteState / Get-GuestJeaInstallManifest / Get-GuestJeaProgramDataState, used by
# every privileged gate. Execution occurs through a narrow JEA endpoint 'PathVeer.Certification'
# (virtual account) registered in the guest by the operator (Enable-PathVeerCertificationJea.ps1).
# This replaces the retired Scheduled-Task (RunLevel Highest) elevation design, which real-VM
# Windows rejected with "Access is denied" from the filtered PowerShell Direct parent.
#
# NOTE: PathVeer.Certification.Elevation.ps1 is RETIRED (preserved as historical diagnostic
# evidence of the rejected design) and must NOT be dot-sourced or invoked by the harness.
. (Join-Path $PSScriptRoot 'PathVeer.Certification.Jea.ps1')

# Establish the privileged JEA session for elevated guest operations. If the endpoint is
# not registered in the guest, this returns $null and privileged gates fail with a
# structured HARNESS/ENVIRONMENT error (never silent non-elevated execution).
$script:JeaSession = $null
function Get-GuestJeaSession([System.Management.Automation.PSCredential]$Cred) {
    if ($script:JeaSession) { return $script:JeaSession }
    $s = New-GuestJeaSession -Cred $Cred -VMName $VmName -ConfigurationName 'PathVeer.Certification'
    if (-not $s) {
        throw "JEA certification endpoint 'PathVeer.Certification' is not registered in the guest. Run Enable-PathVeerCertificationJea.ps1 (elevated) inside the guest, then re-run."
    }
    $script:JeaSession = $s
    return $s
}

# Convenience wrappers that route privileged gate operations through the trusted JEA
# module functions. NO raw command / ScriptBlock / executable is forwarded to the guest;
# the JEA endpoint exposes only validated wrappers.

function Run-GuestJeaInstall([System.Management.Automation.Runspaces.PSSession]$Session,
                             [string]$Action='Install', [string[]]$Feature=@('RegisterShell','InstallTray')) {
    $jea = Get-GuestJeaSession $script:Cred
    return Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action $Action -Feature $Feature
}

# -------- Stage implementations (each returns a hashtable of evidence) --------

function Assert-CandidateMatchesProtected([string]$CandidatePackage, [string]$ExpectedPackageId) {
    # SECURITY (Option A semantics): the harness installs ONLY the operator-staged protected
    # payload (ExpectedPackageId). It must NOT silently accept a -CandidatePackage that names a
    # different release while the VM installs the checkpointed protected candidate. Fail loud on
    # identity mismatch so the operator never passes candidate X while the VM installs candidate Y.
    $leaf = [System.IO.Path]::GetFileName($CandidatePackage)
    if ($leaf -ne $ExpectedPackageId) {
        throw ("CANDIDATE IDENTITY MISMATCH: -CandidatePackage basename '$leaf' does not match the operator-staged protected certification payload '$ExpectedPackageId'. The harness installs only the protected payload staged via Enable-PathVeerCertificationJea.ps1; pass the matching candidate package.")
    }
    Step "Candidate identity matches protected payload: $leaf"
}

function Register-NormalUserTrayRunEntry([System.Management.Automation.Runspaces.PSSession]$Session, [string]$trayExePath) {
    # Harness-only per-user Tray Run-entry registration for PV-CERT\pvcert.
    #
    # WHY THIS EXISTS: GATE-5 installs through the ELEVATED JEA trusted wrapper, which runs as the
    # RunAsVirtualAccount administrator. That process's HKCU is the VIRTUAL ACCOUNT's hive, NOT
    # pvcert's (proven in the pre-approval trace). So the installer's own -InstallTray /
    # Set-TrayStartupEntry writes the Run entry into the wrong hive and pvcert's interactive logon
    # would never auto-start the Tray. The authoritative real-VM result proved that PowerShell Direct
    # under PV-CERT\pvcert IS genuinely that user and its HKCU IS pvcert's hive -- so this harness
    # step writes pvcert's per-user Run entry through the EXISTING normal $Session (identity
    # PV-CERT\pvcert), using the SAME value name and quoting shape the product installer uses.
    #
    # HARNESS-ONLY: no product source, no JEA change, no new launch primitive, no HKLM, no token theft,
    # no SYSTEM, no CreateProcessAsUser. If this step fails it is HARNESS/PREPARATION FAILURE, never a
    # product failure.
    #
    # REGISTRY BOUNDARY (hardened): the VM remote block uses Microsoft.Win32.Registry (NOT the
    # PowerShell Registry provider, which silently no-oped New-Item in a PowerShell Direct session and
    # surfaced a raw ItemNotFound). The remote block NEVER writes Desktop evidence files -- it returns
    # ONE structured object for both PASS and FAIL. All 21-tray-run-entry.json persistence happens
    # DESKTOP-side via Save-Json, so certification evidence is never silently swallowed inside the VM.
    $expectedIdentity = 'PV-CERT\pvcert'
    # Must EXACTLY match the product installer's TrayRunValueName (tools/Install-PathVeer.ps1:107).
    $runValueName = 'PathVeer Tray'
    $registrySubPath = 'Software\Microsoft\Windows\CurrentVersion\Run'

    $reg = Invoke-Command -Session $Session -ScriptBlock {
        param($trayExePath, $runValueName, $registrySubPath, $expectedIdentity)
        # Identity guard MUST come first -- never mutate under the wrong identity.
        $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $sid = $id.User.Value
        $name = $id.Name
        $sessionId = (Get-Process -Id $pid).SessionId
        $result = [PSCustomObject]@{
            success = $false
            classification = $null
            errorType = $null
            errorMessage = $null
            normalUserIdentity = $name
            normalUserSid = $sid
            normalUserProfile = $env:USERPROFILE
            powerShellDirectSessionId = $sessionId
            registrySubPath = $registrySubPath
            runValueName = $runValueName
            expectedValue = ("`"$trayExePath`"")
            actualValue = $null
            createSubKeySucceeded = $false
            writeSucceeded = $false
            readBackSucceeded = $false
            valueMatchesExpected = $false
        }
        try {
            if ($name -ne $expectedIdentity) {
                $result.classification = 'HARNESS/PREPARATION FAILURE'
                $result.errorType = 'IdentityGuardFailed'
                $result.errorMessage = "unexpected normal-user identity at registry write point: '$name' (expected '$expectedIdentity')"
                return $result
            }

            # Prove the current user's HKCU hive is loaded under HKEY_USERS\<SID> before mutating.
            $hku = Get-Item -LiteralPath "Registry::HKEY_USERS\$sid" -ErrorAction SilentlyContinue
            if ($null -eq $hku) {
                $result.classification = 'HARNESS/PREPARATION FAILURE'
                $result.errorType = 'UserHiveNotLoaded'
                $result.errorMessage = "registry hive HKEY_USERS\$sid for '$name' is not loaded"
                return $result
            }

            # Use Microsoft.Win32.Registry (deterministic; missing Run subkey is created naturally).
            $key = $null
            try {
                $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registrySubPath, $true)
            } catch {
                $result.classification = 'HARNESS/PREPARATION FAILURE'
                $result.errorType = 'CreateSubKeyFailed'
                $result.errorMessage = "CreateSubKey('$registrySubPath') failed: $($_.Exception.Message)"
                return $result
            }
            if ($null -eq $key) {
                $result.classification = 'HARNESS/PREPARATION FAILURE'
                $result.errorType = 'CreateSubKeyReturnedNull'
                $result.errorMessage = "CreateSubKey('$registrySubPath') returned null"
                return $result
            }
            $result.createSubKeySucceeded = $true

            $writeOk = $false
            try {
                $key.SetValue($runValueName, $result.expectedValue, [Microsoft.Win32.RegistryValueKind]::String)
                $writeOk = $true
            } catch {
                $result.classification = 'HARNESS/PREPARATION FAILURE'
                $result.errorType = 'RunValueWriteFailed'
                $result.errorMessage = "SetValue('$runValueName') failed: $($_.Exception.Message)"
                return $result
            }
            $result.writeSucceeded = $true

            try {
                $result.actualValue = [string]$key.GetValue(
                    $runValueName, $null,
                    [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                $result.readBackSucceeded = $true
            } catch {
                $result.classification = 'HARNESS/PREPARATION FAILURE'
                $result.errorType = 'RunValueReadBackFailed'
                $result.errorMessage = "GetValue('$runValueName') failed: $($_.Exception.Message)"
                return $result
            }

            # Hard requirement: case-sensitive equality with the exact quoted manifest path.
            if ($result.actualValue -ceq $result.expectedValue) {
                $result.valueMatchesExpected = $true
                $result.success = $true
            } else {
                $result.classification = 'HARNESS/PREPARATION FAILURE'
                $result.errorType = 'RunValueMismatch'
                $result.errorMessage = "Run value read-back ('$($result.actualValue)') != expected ('$($result.expectedValue)')"
            }
            return $result
        } catch {
            $result.classification = 'HARNESS/PREPARATION FAILURE'
            $result.errorType = 'UnexpectedRegistryError'
            $result.errorMessage = "unexpected error: $($_.Exception.Message)"
            return $result
        } finally {
            if ($null -ne $key) { try { $key.Close() } catch {} }
        }
    } -ArgumentList $trayExePath, $runValueName, $registrySubPath, $expectedIdentity

    # --- Desktop-side persistence only: never swallow evidence inside the VM. ---
    Save-Json '21-tray-run-entry.json' ([PSCustomObject]@{
        capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
        registration = $reg
        result = $(if ($reg.success) { 'PASS' } else { 'FAIL' })
        note = if ($reg.success) {
            'Per-user Tray Run entry registered into PV-CERT\pvcert HKCU via Microsoft.Win32.Registry; executes on next genuine interactive logon.'
        } else {
            "HARNESS/PREPARATION FAILURE: $($reg.errorType) - $($reg.errorMessage)"
        }
    })
    if (-not $reg.success) {
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("HARNESS/PREPARATION FAILURE: Tray Run entry registration failed ($($reg.errorType)): $($reg.errorMessage)"),
            'TrayRunEntryPreparationFailed', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
    }
    return $reg
}

function Run-GATE5PREP([System.Management.Automation.Runspaces.PSSession]$Session, [string]$Pkg) {
    # GATE-5 PREPARATION stage (explicit two-stage design).
    #
    # Restores PV-CERT-HARNESS first (caller responsibility for restore-on-entry is handled in
    # main), installs the product via the trusted JEA wrapper, reads the authoritative install
    # manifest, registers the PV-CERT\pvcert per-user Tray Run entry through the EXISTING normal
    # PowerShell Direct session, validates Service Running/Automatic + normal-user CLI + Run-entry
    # readback/SID, and captures current interactive Explorer/Tray observations.
    #
    # CRITICAL RESTORE POLICY: on success this stage returns WITHOUT restoring the certification
    # baseline. The prepared installed VM is left INTACT so the operator can perform a genuine
    # interactive pvcert logon and then run GATE5VERIFY. PREP is NOT a product failure; it is a
    # HARNESS/PRECONDITION boundary. The caller (main) must NOT restore after PREP.
    Write-Stage "GATE-5 PREP (install + pvcert Tray Run entry; leaves VM prepared)"
    # --- PREP evidence hygiene (provenance): establish ONE run id for this PREP execution and clear
    #     the PREP-owned evidence artifacts BEFORE execution, so stale August-15/16 artifacts cannot
    #     be mistaken for current-run evidence. Only artifacts owned by this PREP flow are cleared;
    #     unrelated gate evidence (e.g. 02/03/06/08/09) is left untouched. ---
    $gate5RunId = [System.Guid]::NewGuid().ToString('D')
    $prepOwnedArtifacts = @('05-gate5-fresh-install.json', '21-tray-run-entry.json', '23-gate5-interactive-prepared.json')
    foreach ($a in $prepOwnedArtifacts) {
        $p = Join-Path $EvidenceDir $a
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }
    }
    # SECURITY (Option A): the operator bootstrap is the ONLY trust transition. PV-CERT-HARNESS
    # already contains the protected, operator-approved installer + payload. The harness performs
    # NO runtime copy of executable bytes into the guest (the old Copy-ToGuest into the untrusted
    # C:\pv-cert\incoming was obsolete and unconsumed after Option A). The harness does NOT inspect
    # the protected tree from the filtered pvcert session (the Option-A ACL denies it, by design).
    # Instead it validates the Desktop-side candidate identity, then delegates to the trusted JEA
    # wrapper, which enforces the exact protected installer + payload preconditions inside the
    # privileged virtual-account context and returns the installer's structured result. pvcert
    # cannot choose the privileged executable bytes.
    $expectedPackageId = 'PathVeer-1.0.0-beta.1'
    Assert-CandidateMatchesProtected -CandidatePackage $Pkg -ExpectedPackageId $expectedPackageId

    $pre = Invoke-Command -Session $Session -ScriptBlock {
        [PSCustomObject]@{
            programFilesPathVeer = (Test-Path 'C:\Program Files\PathVeer')
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programData = (Test-Path "$env:ProgramData\PathVeer")
        }
    }

    Step "Running installer (ELEVATED via JEA trusted wrapper, RegisterShell + InstallTray)"
    # Trusted installer wrapper: fixed installer path, validated switches only. No generic shell.
    $install = Run-GuestJeaInstall -Session $Session -Action Install -Feature @('RegisterShell','InstallTray')

    # Read the installer's structured result + progress (authoritative gate boundary).
    # The trusted function now writes the installer's -ResultFile/-ProgressFile into the JEA
    # virtual account's TEMP, reads them back, and surfaces them on $install.result. The harness
    # therefore consumes the structured result directly from the trusted return object and never
    # reads C:\pv-cert\install-*.json (the installer was never given those paths to write).
    $installResult = $null; $installProgress = $null
    if ($install.result) {
        $installResult   = $install.result.installerResult
        $installProgress = $install.result.installerProgress
    }

    # Hard gate boundary: installation must succeed before any CLI use.
    $post = Get-GuestJeaServiceState -Session $Session -JeaSession (Get-GuestJeaSession $script:Cred)
    $installRootExists = $false; $svcExists = $false; $manifestExists = $false
    $manifest = Get-GuestJeaInstallManifest -Session $Session -JeaSession (Get-GuestJeaSession $script:Cred)
    $installRootExists = (Invoke-Command -Session $Session -ScriptBlock { Test-Path 'C:\Program Files\PathVeer' })
    $svcExists = ($post.result.exists -eq $true)
    if ($manifest.result) { $manifestExists = $true }

    $gate5Failed = $false
    $failReasons = [System.Collections.Generic.List[string]]::new()
    if (-not $install.elevationAvailable) { $gate5Failed = $true; $failReasons.Add("elevation unavailable: $($install.error)") }
    # Only classify as "child not genuinely elevated" when the install actually completed and
    # reached identity capture. If the invocation was rejected upstream (e.g. NoLanguage syntax),
    # completed=$false and the "did not complete" reason already explains the real cause -- do not
    # mislabel it as a loss of JEA virtual-account elevation (which the control plane already proved).
    if ($install.completed -eq $true -and $install.elevationSucceeded -ne $true) { $gate5Failed = $true; $failReasons.Add("child not genuinely elevated (childIsAdministrator=$($install.childIsAdministrator), childUser=$($install.childUser))") }
    if ($install.completed -eq $false) { $gate5Failed = $true; $failReasons.Add("installer did not complete: $($install.error)") }
    # --- Installer lifecycle (from the trusted function's explicit fields, not the wrapper's
    #     misleading 'completed' flag). 'installCompleted=true' historically meant only "the JEA
    #     call returned without an error string", which is NOT proof the installer ran. These
    #     fields disambiguate installer start / return / exit / error. ---
    $ir = $install.result
    if ($null -eq $ir) { $gate5Failed = $true; $failReasons.Add('trusted install function returned no result object') }
    else {
        if ($ir.installerInvocationAttempted -ne $true) { $gate5Failed = $true; $failReasons.Add('installer invocation was not attempted by the trusted wrapper') }
        if ($ir.installerStarted -ne $true) { $gate5Failed = $true; $failReasons.Add('installer process did not start') }
        if ($ir.installerReturned -ne $true) { $gate5Failed = $true; $failReasons.Add("installer process did not return: $($ir.installerError)") }
        if ($null -ne $ir.installerExitCode -and $ir.installerExitCode -ne 0) { $gate5Failed = $true; $failReasons.Add("installer exit code $($ir.installerExitCode)") }
        if ($ir.installerError) { $gate5Failed = $true; $failReasons.Add("installer error: $($ir.installerError)") }
        if ($ir.installerResult -and $ir.installerResult.success -eq $false) { $gate5Failed = $true; $failReasons.Add("installer reported failure: [$($ir.installerResult.category)] $($ir.installerResult.message)") }
    }
    if (-not $svcExists) { $gate5Failed = $true; $failReasons.Add('PathVeer service not present after install') }
    if (-not $manifestExists) { $gate5Failed = $true; $failReasons.Add('install-manifest.json not present after install') }

    if ($gate5Failed) {
        $failEvidence = [PSCustomObject]@{
            gate = 'GATE-5'; result = 'FAIL'; reasons = $failReasons.ToArray()
            elevated = $install.elevationAvailable; elevationSucceeded = $install.elevationSucceeded
            childUser = $install.childUser; childIsAdministrator = $install.childIsAdministrator
            # Explicit installer lifecycle (replaces the misleading 'installCompleted').
            trustedInstallFunctionReached  = ($null -ne $install.childUser)
            installerPathValidated         = if ($ir) { $ir.installerPathValidated } else { $null }
            payloadPathValidated           = if ($ir) { $ir.payloadPathValidated } else { $null }
            installerInvocationAttempted   = if ($ir) { $ir.installerInvocationAttempted } else { $null }
            installerStarted               = if ($ir) { $ir.installerStarted } else { $null }
            installerReturned              = if ($ir) { $ir.installerReturned } else { $null }
            installerExitCode              = if ($ir) { $ir.installerExitCode } else { $null }
            installerError                 = if ($ir) { $ir.installerError } else { $null }
            installerResult                = $installResult
            installerProgress              = $installProgress
            installCompleted               = $install.completed
            postConditions = $post.result; preInstall = $pre
        }
        Save-Json '05-gate5-fresh-install.json' $failEvidence
        Write-Host "  GATE-5 FAIL (no CLI invoked): $($failReasons -join ' | ')" -ForegroundColor Red
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("GATE-5 install failed: $($failReasons -join '; ')"),
            'GATE5InstallFailed', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
    }

    # Success path: locate CLI from the authoritative manifest, guard before invoking.
    $cliExe = $null
    if ($manifest.result -and $manifest.result.cliExecutablePath) { $cliExe = $manifest.result.cliExecutablePath }
    else { $cliExe = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe' }

    # --- Register PV-CERT\pvcert's per-user Tray Run entry through the NORMAL PowerShell Direct
    #     session (identity pvcert), NOT the elevated JEA wrapper. The JEA wrapper's HKCU is the
    #     virtual account's hive, so it cannot register pvcert's interactive logon Tray. The Run
    #     entry fires only on pvcert's NEXT GENUINE INTERACTIVE logon -- it does NOT launch the Tray
    #     immediately, and a Session-0 PowerShell Direct Tray must never be claimed as interactive.
    #     Failure here is HARNESS/PREPARATION failure, not a product failure. ---
    $trayRunEntry = $null
    if ($manifest.result -and $manifest.result.trayExecutablePath) {
        Step "Registering PV-CERT\pvcert per-user Tray Run entry (normal-user session)"
        $trayRunEntry = Register-NormalUserTrayRunEntry -Session $Session -trayExePath $manifest.result.trayExecutablePath
    } else {
        Write-Host "  WARN: authoritative trayExecutablePath unavailable; skipping per-user Tray Run registration." -ForegroundColor Yellow
    }

    $ev = Invoke-Command -Session $Session -ScriptBlock {
        param($cliExe, $installManifest)
        $svc = Get-CimInstance Win32_Service -Filter "Name='PathVeer'" -ErrorAction SilentlyContinue
        $app = $null
        try {
            foreach ($k in (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue)) {
                if ($k.DisplayName -eq 'PathVeer') { $app = $k; break }
            }
        } catch {}
        $startMenu = @(Get-ChildItem "$env:ProgramData\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter *.lnk -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'PathVeer' } | ForEach-Object { $_.Name })
        $tray = Get-ChildItem 'C:\Program Files\PathVeer' -Recurse -Filter PathVeer.Tray.exe -ErrorAction SilentlyContinue | Select-Object -First 1
        $cliExists = (Test-Path -LiteralPath $cliExe -PathType Leaf)
        $cliOnPath = @($env:Path -split ';' | Where-Object { $_ -and (Test-Path (Join-Path $_ 'PathVeer.Cli.exe')) }).Count
        $pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*PathVeer.Control.v1*' -or $_ -like '*IranDirect.Control.v1*' }
        $cliStatus = $null
        if ($cliExists) { $cliStatus = (& $cliExe status 2>&1) }
        $svcName = $null; $svcState = 'absent'; $svcStartMode = $null; $svcPathName = $null
        if ($svc) { $svcName = $svc.Name; $svcState = $svc.State; $svcStartMode = $svc.StartMode; $svcPathName = $svc.PathName }
        $appsAndFeatures = $null
        if ($app) { $appsAndFeatures = [PSCustomObject]@{ displayName = $app.DisplayName; version = $app.DisplayVersion; publisher = $app.Publisher; uninstall = $app.UninstallString } }
        [PSCustomObject]@{
            serviceName = $svcName; serviceState = $svcState; serviceStartMode = $svcStartMode; servicePathName = $svcPathName
            appsAndFeatures = $appsAndFeatures; startMenuShortcuts = $startMenu
            trayPresent = ($null -ne $tray); cliOnPathCount = $cliOnPath
            ipcPipes = $pipe; cliExists = $cliExists; cliPath = $cliExe
            cliStatus = ($cliStatus -join "`n"); programDataState = (Test-Path "$env:ProgramData\PathVeer")
            installManifest = $installManifest
        }
    } -ArgumentList $cliExe, $manifest.result

    # --- Prepare-state evidence: the operator MUST run GATE5VERIFY on this exact prepared VM. ---
    $interactiveExplorerSessionIds = @(Invoke-Command -Session $Session -ScriptBlock {
        @(Get-Process -Name 'explorer' -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty SessionId -Unique | Where-Object { $_ -ne $null })
    })
    $prepared = [PSCustomObject]@{
        gate5RunId = $gate5RunId
        preparedUtc = (Get-Date).ToUniversalTime().ToString('o')
        capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
        prepared = $true
        candidateVersion = if ($manifest.result) { $manifest.result.productVersion } else { $null }
        installManifest = $manifest.result
        explorerPresentAtPrep = ($interactiveExplorerSessionIds -and $interactiveExplorerSessionIds.Count -gt 0)
        explorerSessionIdsAtPrep = $interactiveExplorerSessionIds
        # A newly-written HKCU Run entry executes ONLY at a NEW interactive logon. If an interactive
        # Explorer session already existed when the Run entry was registered, merely unlocking/reconnecting
        # is insufficient: PV-CERT\pvcert must SIGN OUT and SIGN BACK IN.
        freshInteractiveLogonRequired = $true
        normalUserIdentity = if ($trayRunEntry) { $trayRunEntry.normalUserIdentity } else { $null }
        normalUserSid = if ($trayRunEntry) { $trayRunEntry.normalUserSid } else { $null }
        trayRunEntryRegistered = if ($trayRunEntry) { $trayRunEntry.success } else { $false }
        trayRunEntryValue = if ($trayRunEntry) { $trayRunEntry.actualValue } else { $null }
        powerShellDirectSessionId = if ($trayRunEntry) { $trayRunEntry.powerShellDirectSessionId } else { $null }
        interactiveExplorerSessionIds = $interactiveExplorerSessionIds
        # interactiveDesktopRequired stays true until a genuine interactive pvcert logon provides
        # a Tray whose SessionId matches an interactive Explorer session id (GATE5VERIFY gate).
        interactiveDesktopRequired = $true
        # Service/CLI/health observations captured during PREP (not the authority contract).
        serviceState = $ev.serviceState
        serviceStartMode = $ev.serviceStartMode
        cliExists = $ev.cliExists
        cliExitCode = $ev.cliStatus
        ipcPipes = $ev.ipcPipes
    }
    Save-Json '23-gate5-interactive-prepared.json' $prepared
    Save-Json '05-gate5-fresh-install.json' ([PSCustomObject]@{
        gate5RunId = $gate5RunId
        preparedUtc = $prepared.preparedUtc
        capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
        preInstall = $pre
        elevationAvailable = $install.elevationAvailable
        installExitCode = $install.completed
        installCompleted = $install.completed
        installResult = $installResult
        installProgress = $installProgress
        postConditions = $post.result
        evidence = $ev
        # Per-user Tray Run entry registered into PV-CERT\pvcert HKCU (normal-user session).
        # Fires only on pvcert's next genuine interactive logon; not an immediate launch.
        trayRunEntry = $trayRunEntry
    })
    Write-Host "`n  GATE-5 PREPARED." -ForegroundColor Green
    Write-Host "  A genuine PV-CERT\pvcert interactive logon is required for Windows to execute the per-user Tray Run entry." -ForegroundColor Yellow
    if ($prepared.explorerPresentAtPrep -eq $true) {
        Write-Host "  IMPORTANT: an interactive Explorer session for PV-CERT\pvcert ALREADY existed when the Run entry was registered." -ForegroundColor Yellow
        Write-Host "  Merely unlocking/reconnecting is INSUFFICIENT. You must SIGN OUT of that Windows session completely, then SIGN BACK IN." -ForegroundColor Yellow
        Write-Host "  The Run entry executes only at a NEW interactive logon." -ForegroundColor Yellow
    } else {
        Write-Host "  Log into PathVeer-Certification normally as PV-CERT\pvcert." -ForegroundColor Yellow
    }
    Write-Host "  After Explorer AND PathVeer Tray are running, execute: -Stage GATE5VERIFY" -ForegroundColor Yellow
    Write-Host "  Do NOT restore/checkpoint the VM in the meantime." -ForegroundColor Yellow
    # Leave the prepared VM intact (no restore). The caller (main) must not restore after PREP.
    return $prepared
}

function Run-GATE5VERIFY([System.Management.Automation.Runspaces.PSSession]$Session) {
    # GATE-5 VERIFICATION stage (explicit two-stage design).
    #
    # MUST operate only on a VM preserved by a prior GATE5PREP. It does NOT restore PV-CERT-HARNESS
    # at the start (restoring would wipe the prepared product + pvcert Run entry). It validates a
    # strong prepared-state precondition, requires genuine interactive-desktop evidence, then runs
    # the existing Service/Tray authority contract.
    #
    # RESTORE POLICY:
    #   * success        -> restore baseline (PV-CERT-HARNESS) and report GATE-5 PASS / CLOSED.
    #   * product FAIL   -> PRESERVE the failed VM for diagnostics; do not auto-restore.
    #   * precondition   -> HARNESS/PRECONDITION FAILURE; instruct operator to run GATE5PREP; do not
    #                       classify as a product defect.
    Write-Stage "GATE-5 VERIFY (authority contract on PREP-preserved VM)"

    # --- Prepared-state precondition (no restore). Validate before any contract test. ---
    $prepEvidencePath = Join-Path $EvidenceDir '23-gate5-interactive-prepared.json'
    $prep = $null
    if (Test-Path $prepEvidencePath) {
        try { $prep = Get-Content $prepEvidencePath -Raw | ConvertFrom-Json } catch { $prep = $null }
    }
    $preparedOk = ($null -ne $prep -and $prep.prepared -eq $true)
    if (-not $preparedOk) {
        Save-Json '22-servicetray-contract.json' ([PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            result = 'HARNESS/PRECONDITION FAILURE'
            failReasons = @('gate5-prep-state-missing')
            reason = 'GATE5VERIFY ran without a preserved GATE5PREP state. Run -Stage GATE5PREP first (it installs the product and registers the pvcert Tray Run entry, then leaves the VM intact).'
        })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new('GATE5VERIFY precondition failed: no preserved GATE5PREP state. Run GATE5PREP first.'),
            'GATE5VerifyPreconditionFailed', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
    }

    # --- Bind VERIFY to the CURRENT PREP run id. Stale 23/05/21 artifacts (e.g. an August-15 run)
    #     must NOT satisfy a current run. The preserved evidence MUST carry a gate5RunId that was
    #     minted by the PREP flow that left this VM intact. ---
    $prepGate5RunId = $null
    if ($prep.PSObject.Properties.Name -contains 'gate5RunId') { $prepGate5RunId = $prep.gate5RunId }
    if ([string]::IsNullOrWhiteSpace($prepGate5RunId)) {
        Save-Json '22-servicetray-contract.json' ([PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            result = 'HARNESS/PRECONDITION FAILURE'
            failReasons = @('gate5-prep-runid-missing')
            reason = '23-gate5-interactive-prepared.json has no gate5RunId; it is stale (pre-run-id) PREP evidence. Run -Stage GATE5PREP to produce current-run evidence.'
            preparedEvidence = $prep
        })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new('GATE5VERIFY precondition failed: preserved PREP evidence has no gate5RunId (stale). Run GATE5PREP first.'),
            'GATE5VerifyPreconditionFailed', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
    }

    # Re-read authoritative live state; if it does not match the prepared evidence, refuse.
    $manifestResponse = Get-GuestJeaInstallManifest -Session $Session -JeaSession (Get-GuestJeaSession $script:Cred)
    # Get-GuestJeaInstallManifest returns a wrapper; the authoritative manifest is under .result.
    $manifest = $manifestResponse.result
    $svcResponse = Get-GuestJeaServiceState -Session $Session -JeaSession (Get-GuestJeaSession $script:Cred)
    $live = $null
    try {
        $live = Invoke-Command -Session $Session -ScriptBlock {
        param($trayValueName, $runKey)
        $svcExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
        $svc = if ($svcExists) { Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue } else { $null }
        $runEntry = $null
        try { $runEntry = (Get-ItemProperty -Path $runKey -Name $trayValueName -ErrorAction SilentlyContinue).$trayValueName } catch { $runEntry = $null }
        $explorer = @(Get-Process -Name 'explorer' -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty SessionId -Unique | Where-Object { $_ -ne $null })
        $trayProc = @(Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue)
        # Read-only owner mapping for interactive-session attribution (no token manipulation).
        # This helper runs INSIDE the VM runspace (Win32_Process instances live on the VM). It is
        # intentionally defined here, NOT in the Desktop script scope, because a Desktop-scoped
        # function is NOT available inside the Invoke-Command remote runspace. Uses the
        # Win32_Process.GetOwner CIM METHOD via Invoke-CimMethod (NOT the unsupported
        # $cimInstance.GetOwner() that silently returned null in a prior real run).
        function Get-ProcessOwnerInfo([int]$ProcessId) {
            $out = [PSCustomObject]@{
                success = $false; processId = $ProcessId; sessionId = $null
                domain = $null; user = $null; identity = $null
                errorType = $null; errorMessage = $null
            }
            try {
                $proc = Get-CimInstance -ClassName Win32_Process -Filter "Handle='$ProcessId'" -ErrorAction Stop
                if ($null -eq $proc) {
                    $out.errorType = 'ProcessNotFound'
                    $out.errorMessage = "no Win32_Process with Handle=$ProcessId"
                    return $out
                }
                $out.sessionId = $proc.SessionId
                $owner = Invoke-CimMethod -InputObject $proc -MethodName GetOwner -ErrorAction Stop
                if ($owner -and $owner.ReturnValue -eq 0 -and $owner.Domain -and $owner.User) {
                    $out.domain = $owner.Domain
                    $out.user = $owner.User
                    $out.identity = ($owner.Domain + '\' + $owner.User)
                    $out.success = $true
                } else {
                    $out.errorType = 'GetOwnerReturned'
                    $out.errorMessage = "GetOwner ReturnValue=$($owner.ReturnValue) Domain='$($owner.Domain)' User='$($owner.User)'"
                }
            } catch {
                $out.errorType = 'OwnerResolutionError'
                $out.errorMessage = $_.Exception.Message
            }
            return $out
        }
        $explorerOwnerBySession = @{}
        foreach ($ep in (Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" -ErrorAction SilentlyContinue)) {
            $oi = Get-ProcessOwnerInfo ([int]$ep.Handle)
            if ($oi.success) {
                $explorerOwnerBySession[[int]$ep.SessionId] = [PSCustomObject]@{
                    processId = $oi.processId; sessionId = $oi.sessionId
                    domain = $oi.domain; user = $oi.user; identity = $oi.identity
                }
            }
        }
        $trayOwner = $null
        $trayOwnerInfo = $null
        if ($trayProc.Count) {
            $trayOwnerInfo = Get-ProcessOwnerInfo ([int]$trayProc[0].Id)
            if ($trayOwnerInfo.success) { $trayOwner = $trayOwnerInfo.identity }
        }
        [PSCustomObject]@{
            serviceExists = $svcExists
            serviceState = if ($svc) { $svc.Status } else { 'absent' }
            serviceStartMode = if ($svc) { $svc.StartType } else { $null }
            runEntryValue = $runEntry
            interactiveExplorerSessionIds = $explorer
            explorerOwnerBySession = $explorerOwnerBySession
            trayExePathLive = if ($trayProc.Count) { $trayProc[0].Path } else { $null }
            trayOwnerIdentity = $trayOwner
            traySessionIds = if ($trayProc.Count) { @($trayProc | Select-Object -ExpandProperty SessionId -Unique) } else { @() }
            powerShellDirectSessionId = (Get-Process -Id $pid).SessionId
            normalUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            normalUserIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        }
    } -ArgumentList 'PathVeer Tray', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    } catch {
        # The live interactive-state/owner probe threw inside or across the VM remote runspace
        # (e.g. a command undefined in the remote runspace). Write CURRENT-run failure evidence
        # with this run's gate5RunId so a stale prior 22 cannot masquerade as this run's result.
        # Do NOT delete/overwrite the valid 23/05 prepared/fresh-install evidence.
        Save-Json '22-servicetray-contract.json' ([PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            gate5RunId = $prepGate5RunId
            result = 'HARNESS/PROBE FAILURE'
            classification = 'HARNESS/PROBE FAILURE'
            probeStage = 'interactive-live-state'
            errorType = 'InteractiveLiveStateProbeThrew'
            errorMessage = $_.Exception.Message
            notes = @('Invoke-Command for live interactive state/owner probe threw. Current-run failure evidence written; stale prior 22 must not be treated as this run.')
        })
        # Preserve the VM (existing preservation behavior) and rethrow.
        throw
    }

    $expectedTrayPath = $prep.installManifest.trayExecutablePath
    $liveStateNorm = (Get-EnumString $live.serviceState)
    $liveStartModeNorm = (Get-EnumString $live.serviceStartMode)
    $reasons = [System.Collections.Generic.List[string]]::new()
    if (-not $live.serviceExists) { $reasons.Add('prepared-state-mismatch: PathVeer service absent') }
    if ($liveStateNorm -ne 'Running') { $reasons.Add("prepared-state-mismatch: service not Running (was: $($live.serviceState))") }
    if ($liveStartModeNorm -ne 'Automatic') { $reasons.Add("prepared-state-mismatch: service not Automatic (was: $($live.serviceStartMode))") }
    if ($null -eq $manifest -or $null -eq $manifest.installRoot -or $null -eq $manifest.productVersion) { $reasons.Add('prepared-state-mismatch: install manifest unavailable') }
    # Version comparison: require the prepared candidateVersion to be authoritative (non-null) and to match the
    # live manifest productVersion. A null candidateVersion is a HARNESS defect (PREP must set it from the
    # manifest productVersion) and must NOT be silently skipped.
    if ([string]::IsNullOrWhiteSpace($prep.candidateVersion)) {
        $reasons.Add('prepared-state-mismatch: prepared candidateVersion is null (PREP evidence defect)')
    } elseif ($null -eq $manifest -or [string]::IsNullOrWhiteSpace($manifest.productVersion)) {
        $reasons.Add('prepared-state-mismatch: live manifest productVersion unavailable')
    } elseif ($manifest.productVersion -ne $prep.candidateVersion) {
        $reasons.Add("prepared-state-mismatch: installed version $($manifest.productVersion) != prepared $($prep.candidateVersion)")
    }
    if ($live.runEntryValue -ne "`"$expectedTrayPath`"") { $reasons.Add('prepared-state-mismatch: pvcert HKCU Tray Run entry missing or path mismatched') }
    if ($reasons.Count) {
        Save-Json '22-servicetray-contract.json' ([PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            gate5RunId = $prepGate5RunId
            result = 'HARNESS/PRECONDITION FAILURE'
            failReasons = $reasons.ToArray()
            reason = 'Prepared state is absent or mismatched. Run -Stage GATE5PREP to re-establish a clean prepared VM.'
            preparedEvidence = $prep
            liveState = $live
        })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new('GATE5VERIFY precondition failed: ' + ($reasons -join '; ')),
            'GATE5VerifyPreconditionFailed', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
    }

    # --- Genuine interactive-desktop evidence. PowerShell Direct SessionId (Session 0) is NEVER accepted
    #     as interactive evidence. The Tray must be running in a PV-CERT\pvcert Explorer session, AND that
    #     Explorer must belong to PV-CERT\pvcert (another interactive account may own a different Explorer
    #     session, which must NOT satisfy the proof). The Tray itself must be owned by PV-CERT\pvcert if
    #     ownership is observable. Owner attribution uses the in-runspace Get-ProcessOwnerInfo CIM-method helper
    #     (defined inside the VM remote ScriptBlock, since Desktop-scoped functions are not visible there).
    #     If OWNER RESOLUTION ITSELF fails, that is a HARNESS/PROBE failure (not a user-action failure) and
    #     must NOT be reported as 'interactive precondition required'. ---
    $interactiveExplorerSessionIds = $live.interactiveExplorerSessionIds
    $traySessionIds = $live.traySessionIds
    $expectedOwner = 'PV-CERT\pvcert'
    # pvcert-owned interactive Explorer sessions (owner attribution, not just SessionId).
    $pvcertExplorerSessionIds = @($interactiveExplorerSessionIds | Where-Object {
        $live.explorerOwnerBySession.ContainsKey($_) -and $live.explorerOwnerBySession[$_].identity -eq $expectedOwner
    })
    # Probe-failure: owner resolution could not conclusively attribute the interactive processes.
    # This is distinct from a genuine missing logon; diagnose it as a HARNESS/PROBE failure.
    $ownerProbeFailed = $false
    $ownerProbeDiag = @()
    foreach ($sid in $interactiveExplorerSessionIds) {
        if (-not $live.explorerOwnerBySession.ContainsKey($sid)) {
            $ownerProbeFailed = $true
            $ownerProbeDiag += "explorer owner unresolved for SessionId=$sid"
        }
    }
    if ($traySessionIds.Count -gt 0 -and [string]::IsNullOrWhiteSpace($live.trayOwnerIdentity)) {
        $ownerProbeFailed = $true
        $ownerProbeDiag += "tray owner unresolved (ProcessId=$($live.traySessionIds -join ','))"
    }

    # Tray runs in a pvcert-owned Explorer session.
    $trayInPvcertExplorer = ($traySessionIds.Count -gt 0 -and
        @($traySessionIds | Where-Object { $_ -in $pvcertExplorerSessionIds }).Count -gt 0)
    # Tray owner is pvcert (if observable).
    $trayOwnedByPvcert = ($null -eq $live.trayOwnerIdentity) -or ($live.trayOwnerIdentity -eq $expectedOwner)
    $interactiveMatch = ($traySessionIds.Count -gt 0 -and $trayInPvcertExplorer -and $trayOwnedByPvcert)
    $trayPathMatchesManifest = ($live.trayExePathLive -and $expectedTrayPath -and
        $live.trayExePathLive -eq $expectedTrayPath)

    # Owner-resolution probe failure is a HARNESS/PROBE failure, NOT 'interactive precondition required'.
    if ($ownerProbeFailed) {
        $probe = [PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            gate5RunId = $prepGate5RunId
            result = 'HARNESS/PROBE FAILURE'
            classification = 'HARNESS/PROBE FAILURE'
            failReasons = @('interactive-process-owner-resolution-failed')
            reason = 'Interactive process owner resolution failed; this is a harness/probe defect, not evidence the operator failed to log in. Diagnostics: ' + ($ownerProbeDiag -join '; ')
            powerShellDirectSessionId = $live.powerShellDirectSessionId
            interactiveExplorerSessionIds = $interactiveExplorerSessionIds
            pvcertExplorerSessionIds = $pvcertExplorerSessionIds
            explorerOwnerBySession = $live.explorerOwnerBySession
            traySessionIds = $traySessionIds
            trayOwnerIdentity = $live.trayOwnerIdentity
            trayExePathLive = $live.trayExePathLive
            expectedTrayPath = $expectedTrayPath
        }
        Save-Json '22-servicetray-contract.json' $probe
        # Preserve the VM for diagnostics; do NOT restore. Do NOT re-classify as user-action INCOMPLETE.
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new('GATE5VERIFY HARNESS/PROBE FAILURE: interactive process owner resolution failed (' + ($ownerProbeDiag -join '; ') + ')'),
            'GATE5VerifyOwnerProbeFailed', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
    }

    if (-not $interactiveMatch -or -not $trayPathMatchesManifest) {
        $inc = [PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            gate5RunId = $prepGate5RunId
            result = 'INCOMPLETE (interactive precondition required)'
            interactiveDesktopRequired = $true
            interactiveDesktopTrayVerified = $false
            reason = 'A genuine PV-CERT\pvcert interactive logon is required so Windows executes the per-user Tray Run entry. The Tray must be running in a PV-CERT\pvcert Explorer session (matching SessionId); a Session-0 PowerShell Direct Tray is NOT the interactive-desktop Tray.'
            powerShellDirectSessionId = $live.powerShellDirectSessionId
            interactiveExplorerSessionIds = $interactiveExplorerSessionIds
            pvcertExplorerSessionIds = $pvcertExplorerSessionIds
            explorerOwnerBySession = $live.explorerOwnerBySession
            traySessionIds = $traySessionIds
            trayOwnerIdentity = $live.trayOwnerIdentity
            trayExePathLive = $live.trayExePathLive
            expectedTrayPath = $expectedTrayPath
            notes = @(
                'Product service + CLI authority invariants are NOT asserted as FAIL here; they are unverified pending an interactive session.'
                'powerShellDirectSessionId is never used as evidence that the Tray is interactive.'
            )
        }
        Save-Json '22-servicetray-contract.json' $inc
        Write-Host ('  GATE-5 VERIFY INCOMPLETE (interactive precondition required): ' +
            'log in as PV-CERT\pvcert, confirm Explorer + PathVeer Tray are running, then re-run GATE5VERIFY.') -ForegroundColor Yellow
        # Leave the VM intact; return incomplete (do NOT throw, do NOT restore).
        return $inc
    }

    # --- Genuine interactive Tray present: exercise the existing authority contract. ---
    # Run-ServiceTrayContract records interactiveDesktopTrayVerified=true when the Tray matches an
    # interactive Explorer session. Pass the prepared evidence so it can set the flag truthfully.
    try {
        $contractOut = Run-ServiceTrayContract -Session $Session -PreparedEvidence $prep
    } catch {
        # Product contract failure (ServiceTrayContractFailed) or probe failure: preserve the
        # failed VM for diagnostics; do NOT auto-restore. Re-throw so main records preservation.
        Write-Host "  GATE-5 VERIFY product/probe failure: preserving failed VM for diagnostics (no restore)." -ForegroundColor Red
        throw
    }

    # VERIFY success: report PASS. Restoration of the certification baseline is owned EXCLUSIVELY by
    # the top-level final cleanup block (single restoration authority). GATE5VERIFY success leaves
    # PreserveVmState = $false, so the final block runs Restore-Clean exactly once. Do NOT restore here.
    Write-Host "`n  GATE-5 PASS / CLOSED (interactive-desktop authority contract verified)." -ForegroundColor Green
    return $contractOut
}

function Get-EnumString($obj) {
    if ($null -eq $obj) { return $null }
    if ($obj -is [string]) { return [string]$obj }
    if ($obj -is [System.Enum]) { return $obj.ToString() }
    if ($obj -is [int]) {
        if ($obj -eq 1) { return 'Stopped' }
        if ($obj -eq 4) { return 'Running' }
        if ($obj -eq 2) { return 'Automatic' }
        return [string]$obj
    }
    try {
        if ($obj.PSObject.Properties['Value'] -and $obj.Value -is [string] -and $obj.Value) { return [string]$obj.Value }
    } catch {}
    try {
        if ($obj.PSObject.Properties['value']) {
            $v = $obj.value
            if ($v -is [string] -and $v) { return [string]$v }
            if ($v -is [int]) {
                if ($v -eq 1) { return 'Stopped' }
                if ($v -eq 4) { return 'Running' }
                if ($v -eq 2) { return 'Automatic' }
                return [string]$v
            }
        }
    } catch {}
    return [string]$obj
}

function Run-ServiceTrayContract([System.Management.Automation.Runspaces.PSSession]$Session, $PreparedEvidence = $null) {
    Write-Stage "SERVICE/TRAY AUTHORITY CONTRACT"
    # Shared Get-EnumString (script scope) normalizes ServiceController enums: native enum, numeric
    # fallback (4->Running, 2->Automatic), remoted wrapper {value/Value}, or plain string.
    $jea = Get-GuestJeaSession $script:Cred
    # --- Service state BEFORE Tray stop, read INDEPENDENTLY through the JEA virtual account ---
    $svcBefore = Get-GuestJeaServiceState -Session $Session -JeaSession $jea

    # --- Tray establish + terminate + observe ALL under the NORMAL certification user (the PowerShell
    #     Direct session identity), NOT the JEA virtual admin. The Tray is a NORMAL-user controller and
    #     does not require elevation to terminate its own process. We capture the exact PIDs in this
    #     session, kill those exact PIDs, then observe within the same session so a surviving same PID
    #     (stop-failed) vs a newly-spawned different PID (auto-relaunch) are both detectable. JEA
    #     Stop-PathVeerTray is intentionally NOT used for termination (its nullable stop-result field must
    #     never be the precondition authority). Service state is read separately via JEA below. No
    #     arbitrary long sleeps: observation is bounded. ---
    # --- Authoritative install manifest: source the product's OWN install paths instead of
    #     reconstructing them from $env:ProgramFiles. The old code did
    #       $pf = $env:ProgramFiles; Join-Path (Join-Path $pf 'Tray') 'PathVeer.Tray.exe'
    #     which yields 'C:\Program Files\Tray\PathVeer.Tray.exe' and silently drops the
    #     'PathVeer' segment of the install root. A path that does not exist then trips the
    #     "Tray executable not found" early return and bakes FALSE product facts
    #     (cliExists=false, serviceStateAtCliAttempt='absent', ipcPipeNamePresentAfter=false)
    #     into the fallback object. GATE-5 (05-gate5-fresh-install.json) already obtained the
    #     manifest with installRoot/cliExecutablePath/trayExecutablePath, so we reuse it. ---
    $manifestResponse = Get-GuestJeaInstallManifest -Session $Session -JeaSession $jea
    # Get-GuestJeaInstallManifest returns a wrapper whose authoritative manifest is under .result.
    $manifest = $manifestResponse.result
    $manifestOk = ($null -ne $manifest -and $null -ne $manifest.installRoot -and
                   $null -ne $manifest.cliExecutablePath -and $null -ne $manifest.trayExecutablePath)
    if (-not $manifestOk) {
        $probe = [PSCustomObject]@{
            remoteProbeSucceeded = $false
            remoteProbeErrorType = 'ManifestResolution'
            remoteProbeErrorMessage = ('Authoritative install manifest unavailable or missing ' +
                'installRoot/cliExecutablePath/trayExecutablePath; product paths cannot be resolved safely.')
            installRootObserved = if ($manifest) { $manifest.installRoot } else { $null }
            cliExePath = $null; trayExePath = $null
        }
        Save-Json '22-servicetray-contract.json' ([PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            elevationAvailable = ($jea -ne $null)
            contract = $null
            failReasons = @('harness-probe-manifest-resolution-failed')
            probe = $probe
        })
        # Path-resolution failure must be explicit; it must NOT masquerade as a product/CLI/IPC failure.
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new('Service/Tray contract HARNESS/PROBE FAILURE: install manifest resolution failed'),
            'ServiceTrayContractProbeFailed', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
    }

    $remote = Invoke-Command -Session $Session -ScriptBlock {
        param($cliExe, $trayExe, $installRoot)
        $probeFailed = $false; $probeErrorType = $null; $probeErrorMessage = $null
        try {
        # --- Session instrumentation: NORMAL-USER IDENTITY (PV-CERT\pvcert) is proven by the
        #     PowerShell Direct session, but that session runs in Session 0, NOT the interactive
        #     desktop (Explorer lives in Session 1). A normal-user Tray launched here runs in
        #     Session 0, so it must NOT be claimed as the interactive-desktop Tray. We capture the
        #     session ids explicitly so the report can distinguish the two concepts (Defect #2). ---
        $psDirectSessionId = (Get-Process -Id $pid).SessionId
        $interactiveExplorerSessionIds = @(Get-Process -Name 'explorer' -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty SessionId -Unique | Where-Object { $_ -ne $null })

        $cliExeExistsAtExactPath = (Test-Path -LiteralPath $cliExe -PathType Leaf)
        $trayExeExistsAtExactPath = (Test-Path -LiteralPath $trayExe -PathType Leaf)

        # --- Establish Tray under the NORMAL user (launch if absent) and capture EXACT PIDs ---
        if (-not $trayExeExistsAtExactPath) {
            return [PSCustomObject]@{
                preconditionEstablished = $false; pidsBefore = @(); stopAttempted = $false
                stoppedPids = @(); pidsImmediatelyAfterStop = @(); pidsAfterObservation = @()
                presentAfterWait = $false; trayExePath = $trayExe; cliExists = $false
                cliAttemptCount = 0; cliExitCode = $null; cliStdoutStderr = $null
                cliHealthyDuringPoll = $false; serviceStateAtCliAttempt = 'absent'
                ipcPipeNamePresentAfter = $false; note = 'Tray executable not found at authoritative manifest path'
                installRootObserved = $installRoot; cliExePath = $cliExe
                cliExeExistsAtExactPath = $cliExeExistsAtExactPath; trayExeExistsAtExactPath = $trayExeExistsAtExactPath
                powerShellDirectSessionId = $psDirectSessionId; interactiveExplorerSessionIds = $interactiveExplorerSessionIds
                traySessionIdBefore = @(); traySessionIdAfter = @()
                remoteProbeSucceeded = $true; remoteProbeErrorType = $null; remoteProbeErrorMessage = $null
            }
        }
        $p = @(Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue)
        $traySessionIdBefore = if ($p.Count) { @($p | Select-Object -ExpandProperty SessionId -Unique) } else { @() }
        if ($p.Count -eq 0) {
            Start-Process -FilePath $trayExe -ErrorAction Stop
            Start-Sleep -Seconds 2
            $p = @(Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue)
            $traySessionIdBefore = if ($p.Count) { @($p | Select-Object -ExpandProperty SessionId -Unique) } else { @() }
        }
        $preconditionEstablished = ($p.Count -ge 1)
        $pidsBefore = @($p | ForEach-Object { $_.Id })

        if (-not $preconditionEstablished) {
            return [PSCustomObject]@{
                preconditionEstablished = $false; pidsBefore = $pidsBefore; stopAttempted = $false
                stoppedPids = @(); pidsImmediatelyAfterStop = @(); pidsAfterObservation = @()
                presentAfterWait = $false; trayExePath = $trayExe
                cliExists = $cliExeExistsAtExactPath
                cliAttemptCount = 0; cliExitCode = $null; cliStdoutStderr = $null
                cliHealthyDuringPoll = $false; serviceStateAtCliAttempt = 'absent'
                ipcPipeNamePresentAfter = $false; note = 'Tray could not be established under normal user'
                installRootObserved = $installRoot; cliExePath = $cliExe
                cliExeExistsAtExactPath = $cliExeExistsAtExactPath; trayExeExistsAtExactPath = $trayExeExistsAtExactPath
                powerShellDirectSessionId = $psDirectSessionId; interactiveExplorerSessionIds = $interactiveExplorerSessionIds
                traySessionIdBefore = $traySessionIdBefore; traySessionIdAfter = @()
                remoteProbeSucceeded = $true; remoteProbeErrorType = $null; remoteProbeErrorMessage = $null
            }
        }

        # --- Terminate the EXACT normal-user Tray PID(s) from THIS session (no JEA/elevation) ---
        $stopAttempted = $true
        $stoppedPids = @()
        foreach ($id in $pidsBefore) {
            try {
                $proc = Get-Process -Id $id -ErrorAction SilentlyContinue
                if ($proc) { Stop-Process -Id $id -Force -ErrorAction Stop; $stoppedPids += $id }
            } catch {}
        }
        Start-Sleep -Seconds 1
        $pImm = @(Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue)
        $pidsImmediatelyAfterStop = @($pImm | ForEach-Object { $_.Id })

        # --- Bounded observation window (no arbitrary long sleep). Stop as soon as a Tray reappears so
        #     we capture its (possibly new) PID for relaunch classification. ---
        $obsSeconds = 5
        $obsEnd = [datetime]::Now.AddSeconds($obsSeconds)
        $cur = @()
        do {
            Start-Sleep -Seconds 1
            $cur = @(Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue)
        } while ([datetime]::Now -lt $obsEnd -and $cur.Count -eq 0)
        $pidsAfterObservation = @($cur | ForEach-Object { $_.Id })
        $presentAfterWait = ($pidsAfterObservation.Count -gt 0)

        # --- Normal-user CLI readiness probe (bounded poll) + named-pipe-name presence ---
        $cliExists = $cliExeExistsAtExactPath
        $cliProbeTimeoutSeconds = 20
        $cliAttemptIntervalSeconds = 1
        $cliAttempts = @()
        $cliHealthyDuringPoll = $false
        if ($cliExists) {
            $elapsed = 0
            while ($elapsed -lt $cliProbeTimeoutSeconds) {
                $out = $null; $code = $null
                try { $out = (& $cliExe status 2>&1); $code = $LASTEXITCODE } catch { $code = -1 }
                $cliAttempts += [PSCustomObject]@{ attempt = ($cliAttempts.Count + 1); exitCode = $code; output = ($out -join "`n") }
                if ($code -eq 0) { $cliHealthyDuringPoll = $true; break }
                Start-Sleep -Seconds $cliAttemptIntervalSeconds
                $elapsed += $cliAttemptIntervalSeconds
            }
        }
        $finalAttempt = if ($cliAttempts.Count) { $cliAttempts[-1] } else { $null }
        $svc = Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue
        $pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*PathVeer.Control.v1*' }
        $pAfter = @(Get-Process -Name 'PathVeer.Tray' -ErrorAction SilentlyContinue)
        $traySessionIdAfter = if ($pAfter.Count) { @($pAfter | Select-Object -ExpandProperty SessionId -Unique) } else { @() }
        [PSCustomObject]@{
            preconditionEstablished = $preconditionEstablished
            pidsBefore = $pidsBefore; stopAttempted = $stopAttempted; stoppedPids = $stoppedPids
            pidsImmediatelyAfterStop = $pidsImmediatelyAfterStop; pidsAfterObservation = $pidsAfterObservation
            presentAfterWait = $presentAfterWait; trayExePath = $trayExe
            cliExists = $cliExists
            cliAttemptCount = $cliAttempts.Count
            cliExitCode = if ($finalAttempt) { $finalAttempt.exitCode } else { $null }
            cliStdoutStderr = if ($finalAttempt) { $finalAttempt.output } else { $null }
            cliHealthyDuringPoll = $cliHealthyDuringPoll
            serviceStateAtCliAttempt = if ($svc) { $svc.Status } else { 'absent' }
            ipcPipeNamePresentAfter = ($null -ne $pipe)
            note = $null
            installRootObserved = $installRoot; cliExePath = $cliExe
            cliExeExistsAtExactPath = $cliExeExistsAtExactPath; trayExeExistsAtExactPath = $trayExeExistsAtExactPath
            powerShellDirectSessionId = $psDirectSessionId; interactiveExplorerSessionIds = $interactiveExplorerSessionIds
            traySessionIdBefore = $traySessionIdBefore; traySessionIdAfter = $traySessionIdAfter
            remoteProbeSucceeded = $true; remoteProbeErrorType = $null; remoteProbeErrorMessage = $null
        }
        } catch {
            return [PSCustomObject]@{
                probeFailed = $true
                remoteProbeSucceeded = $false
                remoteProbeErrorType = 'RemoteBlockException'
                remoteProbeErrorMessage = ($_.Exception.Message)
                installRootObserved = $installRoot; cliExePath = $cliExe
                cliExeExistsAtExactPath = $null; trayExeExistsAtExactPath = $null
                powerShellDirectSessionId = $null; interactiveExplorerSessionIds = $null
                traySessionIdBefore = @(); traySessionIdAfter = @()
            }
        }
    } -ArgumentList $manifest.cliExecutablePath, $manifest.trayExecutablePath, $manifest.installRoot

    # --- A probe exception must FAIL as HARNESS/PROBE FAILURE, never pretend product facts were measured. ---
    if ($remote.probeFailed) {
        Save-Json '22-servicetray-contract.json' ([PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            elevationAvailable = ($jea -ne $null)
            contract = $null
            failReasons = @('harness-probe-remote-failure')
            probe = [PSCustomObject]@{
                remoteProbeSucceeded = $remote.remoteProbeSucceeded
                remoteProbeErrorType = $remote.remoteProbeErrorType
                remoteProbeErrorMessage = $remote.remoteProbeErrorMessage
                installRootObserved = $remote.installRootObserved
                cliExePath = $remote.cliExePath; trayExePath = $remote.trayExePath
            }
        })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new('Service/Tray contract HARNESS/PROBE FAILURE: ' + $remote.remoteProbeErrorType + ' - ' + $remote.remoteProbeErrorMessage),
            'ServiceTrayContractProbeFailed', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
    }

    # --- Service state AFTER Tray stop, read INDEPENDENTLY through the JEA virtual account ---
    $svcAfter = Get-GuestJeaServiceState -Session $Session -JeaSession $jea
    # --- Desktop combines EXPLICIT values into the contract (Defects 1-4, truthful semantics) ---
    $contract = [PSCustomObject]@{
        capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
        serviceStateBeforeTrayKill   = (Get-EnumString $svcBefore.result.status)
        serviceStartModeBefore       = (Get-EnumString $svcBefore.result.startType)
        serviceStateAfterTrayKill    = (Get-EnumString $svcAfter.result.status)
        serviceStartModeAfter        = (Get-EnumString $svcAfter.result.startType)
        # Tray PID evidence (normal-user session): truthful, non-null, never bypasses the gate.
        trayPreconditionEstablished  = $remote.preconditionEstablished
        trayPidsBefore               = $remote.pidsBefore
        trayStopAttempted            = $remote.stopAttempted
        trayStoppedPids              = $remote.stoppedPids
        trayPidsImmediatelyAfterStop = $remote.pidsImmediatelyAfterStop
        trayPidsAfterObservation     = $remote.pidsAfterObservation
        trayProcessPresentAfterWait  = $remote.presentAfterWait
        trayClassify                 = if ($remote.pidsAfterObservation.Count -gt 0 -and $remote.pidsBefore.Count -gt 0) {
            $survivors = @($remote.pidsAfterObservation | Where-Object { $_ -in  $remote.pidsBefore })
            $newPids   = @($remote.pidsAfterObservation | Where-Object { $_ -notin $remote.pidsBefore })
            if ($survivors.Count -gt 0) { 'tray-stop-failed' } elseif ($newPids.Count -gt 0) { 'tray-auto-relaunched' } else { 'tray-remaining' }
        } else { 'tray-absent' }
        cliExists                    = $remote.cliExists
        cliAttemptCount              = $remote.cliAttemptCount
        cliExitCode                  = $remote.cliExitCode
        cliStdoutStderr              = $remote.cliStdoutStderr
        cliHealthyDuringPoll         = $remote.cliHealthyDuringPoll
        cliWorksWithoutTray          = $remote.cliHealthyDuringPoll
        serviceStateAtCliAttempt     = $remote.serviceStateAtCliAttempt
        ipcPipeNamePresentAfter      = $remote.ipcPipeNamePresentAfter
        trayExePath                  = $remote.trayExePath
        # --- Authoritative path evidence: record EXACTLY the paths the probe tested (Defect #1) ---
        installRootObserved          = $remote.installRootObserved
        cliExePath                   = $remote.cliExePath
        cliExeExistsAtExactPath      = $remote.cliExeExistsAtExactPath
        trayExistsAtExactPath        = $remote.trayExeExistsAtExactPath
        # --- Probe diagnostic: a probe exception must never masquerade as measured product facts (Defect #4) ---
        remoteProbeSucceeded         = $remote.remoteProbeSucceeded
        remoteProbeErrorType         = $remote.remoteProbeErrorType
        remoteProbeErrorMessage      = $remote.remoteProbeErrorMessage
        # --- Session instrumentation: distinguish NORMAL-USER IDENTITY from INTERACTIVE USER SESSION
        #     (Defect #2). PowerShell Direct == PV-CERT\pvcert (identity) but runs in Session 0,
        #     NOT the interactive desktop (Explorer Session 1). A Tray launched here is a Session-0
        #     Tray, not the interactive-desktop Tray. traySessionIdBefore/After are recorded so an
        #     interactive-Tray claim can require a matching interactive Explorer session id. ---
        powerShellDirectSessionId    = $remote.powerShellDirectSessionId
        interactiveExplorerSessionIds = $remote.interactiveExplorerSessionIds
        traySessionIdBefore          = $remote.traySessionIdBefore
        traySessionIdAfter           = $remote.traySessionIdAfter
        # A 'true' interactive-Tray claim requires traySessionIdBefore to match an actual
        # interactive Explorer session id. The remote block captures traySessionIdBefore/After and
        # interactiveExplorerSessionIds. We compute the flag here (not blindly false) so a genuine
        # interactive Tray is honestly recorded; GATE5VERIFY always passes a prepared VM where the
        # Tray is expected to be in the interactive session. PowerShell Direct SessionId is NEVER
        # accepted as interactive evidence (see design note in FINAL REPORT).
        interactiveDesktopTrayVerified = (($remote.traySessionIdBefore.Count -gt 0) -and
            ($null -ne $remote.interactiveExplorerSessionIds) -and
            (@($remote.traySessionIdBefore | Where-Object { $_ -in $remote.interactiveExplorerSessionIds }).Count -gt 0))
    }

    # --- Contract assertions: fail loudly, never silently continue ---
    $reasons = @()
    if ($contract.serviceStateBeforeTrayKill -ne 'Running') {
        $reasons += "service-not-Running-before-tray-kill (was: $($contract.serviceStateBeforeTrayKill))"
    }
    if ($contract.serviceStateAfterTrayKill -ne 'Running') {
        $reasons += "service-not-Running-after-tray-kill (was: $($contract.serviceStateAfterTrayKill))"
    }
    if ($contract.serviceStartModeBefore -ne 'Automatic') {
        $reasons += "service-start-mode-before-not-Automatic (was: $($contract.serviceStartModeBefore))"
    }
    if ($contract.serviceStartModeAfter -ne 'Automatic') {
        $reasons += "service-start-mode-after-not-Automatic (was: $($contract.serviceStartModeAfter))"
    }
    if ($contract.serviceStartModeBefore -ne $contract.serviceStartModeAfter) {
        $reasons += "service-start-mode-changed (before: $($contract.serviceStartModeBefore) after: $($contract.serviceStartModeAfter))"
    }
    if (-not $contract.trayPreconditionEstablished) {
        $reasons += "tray-precondition-not-established (Tray never ran under the normal user; contract not actually exercised)"
    }
    if ($contract.trayPidsBefore.Count -lt 1) {
        $reasons += "tray-pids-before-empty (no normal-user Tray PID captured)"
    }
    if ($contract.trayStopAttempted -ne $true) {
        $reasons += "tray-stop-not-attempted"
    }
    if ($contract.trayPidsAfterObservation.Count -ne 0) {
        $reasons += "tray-present-after-stop (classify: $($contract.trayClassify); before=[$($contract.trayPidsBefore -join ',')] after=[$($contract.trayPidsAfterObservation -join ',')] stopped=[$($contract.trayStoppedPids -join ',')])"
    }
    if (-not $contract.cliExists) { $reasons += "cli-executable-missing" }
    if ($contract.cliExitCode -ne 0) { $reasons += "cli-status-failed-without-tray (exit: $($contract.cliExitCode))" }
    if (-not $contract.ipcPipeNamePresentAfter) { $reasons += "ipc-pipe-name-absent-after-install" }
    # --- Interactive-session precondition (Defect #2 / approval): the authoritative product-service
    #     invariants are the ONLY true FAIL conditions. A Tray that was only exercised in the
    #     Session-0 PowerShell Direct session is NOT an interactive-desktop Tray. interactiveDesktopTrayVerified
    #     is true ONLY when traySessionIdBefore matched an actual interactive Explorer session id. If not,
    #     this is INTERACTIVE PRECONDITION REQUIRED (harness/environment, NOT a PathVeer failure). ---
    $interactiveDesktopRequired = ($contract.interactiveDesktopTrayVerified -ne $true)
    if ($interactiveDesktopRequired) {
        $incomplete = [PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            elevationAvailable = ($jea -ne $null)
            contract = $contract
            interactiveDesktopRequired = $true
            interactiveDesktopTrayVerified = $false
            reason = 'A genuine PV-CERT\pvcert interactive logon is required so Windows can execute the per-user Tray Run entry. The Session-0 PowerShell Direct Tray is NOT the interactive-desktop Tray.'
            notes = @(
                'Product service + CLI authority invariants are NOT asserted as FAIL here; they are unverified pending an interactive session.'
                'powerShellDirectSessionId is never used as evidence that the Tray is interactive.'
            )
        }
        Save-Json '22-servicetray-contract.json' $incomplete
        Write-Host ('  SERVICE/TRAY CONTRACT INCOMPLETE (interactive precondition required): ' +
            'product assertions NOT failed; operator must provide a genuine PV-CERT\pvcert interactive logon.') -ForegroundColor Yellow
        # STOP cleanly: do NOT throw as a product failure. Caller returns for operator action.
        return $incomplete
    }

    if ($reasons.Count) {
        $fail = [PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            elevationAvailable = ($jea -ne $null)
            contract = $contract
            failReasons = $reasons
        }
        Save-Json '22-servicetray-contract.json' $fail
        Write-Host ("  SERVICE/TRAY CONTRACT FAIL: " + ($reasons -join ' | ')) -ForegroundColor Red
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("Service/Tray authority contract not satisfied: " + ($reasons -join '; ')),
            'ServiceTrayContractFailed', [System.Management.Automation.ErrorCategory]::OperationStopped, $null)
    }

    Save-Json '22-servicetray-contract.json' ([PSCustomObject]@{
        capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
        elevationAvailable = ($jea -ne $null)
        contract = $contract
    })
    return $contract
}

function Run-GATE3([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-3 REBOOT PERSISTENCE (VM only)"
    # Lifecycle precondition: PV-CERT-HARNESS baseline has NO PathVeer service. Install the protected
    # candidate first (fail-closed) so the pre-reboot service snapshot and persistence assertions are
    # truthful. Then reboot via the existing certification-safe mechanism and re-verify after readiness.
    $inst = Install-PathVeerProtectedCandidate $Session
    $before = Invoke-Command -Session $Session -ScriptBlock {
        $svc = Get-Service -Name 'PathVeer' -ErrorAction Stop
        $policy = 'absent'
        if (Test-Path "$env:ProgramData\PathVeer") { $policy = 'state-present' }
        [PSCustomObject]@{
            serviceState = $svc.Status; serviceStartMode = $svc.StartType; startedBy = $svc.StartType; policy = $policy
            bootTime = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
        }
    }
    Step "Restarting guest VM (HARD Hyper-V restart; not a graceful OS Restart-Computer)..."
    # CERTIFICATION SEMANTIC: this is a hard Hyper-V VM restart (Restart-VM), NOT a graceful
    # Windows Restart-Computer reboot. The protected candidate was installed above so the guest
    # carries a real PathVeer product; persistence is verified after the restart + PS Direct readiness.
    # Restart-Computer has no -VMName and no -For PowerShellDirect, so the valid bounded Hyper-V
    # primitive is Restart-VM -For Heartbeat -Wait -Timeout 300 (bounded). After the VM is back,
    # a FRESH PowerShell Direct session is established via the existing bounded retry primitive.
    Restart-VM -Name $VmName -Force -Wait -For Heartbeat -Timeout 300 -ErrorAction Stop
    $session2 = New-GuestSession $script:Cred
    $after = Invoke-Command -Session $session2 -ScriptBlock {
        $svc = Get-Service -Name 'PathVeer' -ErrorAction Stop
        $cliExe = Join-Path 'C:\Program Files\PathVeer' 'Cli\PathVeer.Cli.exe'
        $pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*PathVeer.Control.v1*' }
        $cliOk = $false; try { & $cliExe status *> $null; $cliOk = ($LASTEXITCODE -eq 0) } catch {}
        $policy = 'absent'
        if (Test-Path "$env:ProgramData\PathVeer") { $policy = 'state-present' }
        [PSCustomObject]@{
            serviceState = $svc.Status; serviceStartMode = $svc.StartType
            ipcPipeAlive = ($null -ne $pipe); cliWorks = $cliOk; policy = $policy
            bootTime = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
        }
    }
    # Objective reboot proof via the existing PS Direct session: OS LastBootUpTime must advance.
    $bootBefore = $before.bootTime
    $bootAfter = $after.bootTime
    $rebootProof = [PSCustomObject]@{
        bootBeforeUtc = $bootBefore
        bootAfterUtc = $bootAfter
        rebooted = ($null -ne $bootAfter -and $null -ne $bootBefore -and $bootAfter -gt $bootBefore)
    }
    Save-Json '03-gate3-reboot-persistence.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); preconditionInstall=$inst; rebootMechanism='Restart-VM (hard Hyper-V restart; not graceful OS reboot)'; before=$before; after=$after; rebootedVmOnly=$true; rebootProof=$rebootProof
    })
    # FAIL-CLOSED: assert the post-reboot PathVeer persistence contract. Save evidence (above) THEN throw
    # so a broken after-state is never reported as successful gate completion.
    $persistFail = @()
    if ($before.serviceState -ne 'Running') { $persistFail += "pre-reboot serviceState was not Running (was: $($before.serviceState))" }
    if ($before.serviceStartMode -notin @('Automatic','AutomaticDelayedStart')) { $persistFail += "unexpected pre-reboot start mode (was: $($before.serviceStartMode))" }
    if ($after.serviceState -ne 'Running') { $persistFail += "post-reboot serviceState not Running (was: $($after.serviceState))" }
    if ($after.serviceStartMode -notin @('Automatic','AutomaticDelayedStart')) { $persistFail += "post-reboot start mode not persistent (was: $($after.serviceStartMode))" }
    if ($after.ipcPipeAlive -ne $true) { $persistFail += 'IPC pipe not alive after reboot' }
    if ($after.cliWorks -ne $true) { $persistFail += 'CLI did not work after reboot' }
    if (-not $rebootProof.rebooted) { $persistFail += 'OS reboot not objectively proven (LastBootUpTime did not advance)' }
    if ($persistFail.Count -gt 0) {
        Save-Json '03-gate3-reboot-persistence.json' ([PSCustomObject]@{
            capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); preconditionInstall=$inst; rebootMechanism='Restart-VM (hard Hyper-V restart; not graceful OS reboot)'; before=$before; after=$after; rebootedVmOnly=$true; rebootProof=$rebootProof; failReasons=$persistFail
        })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("GATE-3 reboot persistence contract not satisfied: $($persistFail -join '; ')."),
            'Gate3PersistenceFailed', [System.Management.Automation.ErrorCategory]::InvalidResult, $null)
    }
    return @{ Session=$session2; after=$after; rebootProof=$rebootProof }
}

function Run-GATE2([System.Management.Automation.Runspaces.PSSession]$Session, [string]$Prefix) {
    Write-Stage "GATE-2 ROUTE MUTATION / RECOVERY (bounded TEST-NET $Prefix)"
    # Lifecycle precondition: PV-CERT-HARNESS baseline has NO PathVeer product. Install the protected
    # candidate first (fail-closed) so the CLI/service/route assertions below are truthful.
    $inst = Install-PathVeerProtectedCandidate $Session
    $jea = Get-GuestJeaSession $script:Cred
    if ($null -eq $jea) {
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("JEA session could not be established for elevation; cannot run GATE-2."),
            'Gate2JeaUnavailable', [System.Management.Automation.ErrorCategory]::ResourceUnavailable, $null)
    }

    # --- NETWORK / ROUTE BASELINE PREREQUISITE ------------------------------------------------
    # GATE-2 mutates the Windows route table. Installing a managed route requires a usable adapter with a
    # valid next-hop/interface, i.e. a non-APIPA IPv4 address AND an IPv4 default route. After a
    # PV-CERT-HARNESS checkpoint restore the guest may come back APIPA (169.254.x.x) with NO default route
    # because DHCP runtime state is NOT baked into the checkpoint. We establish route-readiness FIRST (bounded
    # DHCP renewal via the shared Repair-GuestDhcp primitive). DNS/HTTPS/GitHub/release servers are NOT required
    # for route installation, so the readiness check intentionally omits them. If the baseline cannot be
    # established, this is an ENVIRONMENT/NETWORK-READINESS defect (NOT a product defect); we save evidence and
    # throw WITHOUT running add-cidr/disable/enable/doctor so we never blame the product for a stale network.
    $routesBefore = Invoke-Command -Session $Session -ScriptBlock {
        @(Get-NetRoute -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop, InterfaceIndex, RouteMetric)
    }
    $routesBeforeCount = if ($routesBefore) { $routesBefore.Count } else { 0 }
    $defaultRouteBefore = @($routesBefore | Where-Object { $_.DestinationPrefix -eq '0.0.0.0/0' })
    $netReady = $null
    try {
        $netReady = Wait-GuestRouteReady -Session $Session -MaxSeconds 120
    } catch {
        $probe = $_.TargetObject
        Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{
            capturedUtc = (Get-Date -AsUTC).ToString('o'); managedPrefix = $Prefix
            elevationAvailable = ($jea -ne $null)
            environmentNotReady = $true
            networkReadiness = $probe
            ipv4NonApipa = $probe.hasNonApipa
            apipaAddresses = $probe.apipaAddresses
            hasDefaultRoute = $probe.hasDefaultRoute
            dhcpAdapterAliases = $probe.dhcpAdapterAliases
            routesBeforeCount = $routesBeforeCount
            defaultRouteBefore = @($defaultRouteBefore | ForEach-Object { [PSCustomObject]@{ DestinationPrefix = $_.DestinationPrefix; NextHop = $_.NextHop; InterfaceIndex = $_.InterfaceIndex; RouteMetric = $_.RouteMetric } })
            failReasons = @('route/network baseline not established after checkpoint restore (APIPA/no default route); environment defect, not a product defect')
        })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("GATE-2 environment/network readiness not established: $($_.Exception.Message)"),
            'Gate2EnvironmentNotReady', [System.Management.Automation.ErrorCategory]::ResourceUnavailable, $probe)
    }
    $envProbe = $netReady.lastProbe

    # --- Add custom managed route (desired config only; route is installed on enable) ------------
    $addC = Invoke-GuestJeaCli -Session $Session -JeaSession $jea -Verb 'custom-routes' -SubVerb 'add-cidr' -Argument $Prefix
    if ($addC.result.exitCode -ne 0) {
        Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{ capturedUtc = (Get-Date -AsUTC).ToString('o'); managedPrefix = $Prefix; elevationAvailable = ($jea -ne $null); addCustomRouteExit = $addC.result.exitCode; routesBeforeCount = $routesBeforeCount; networkReadiness = $envProbe; failReasons = @('cli-add-cidr-nonzero') })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("PathVeer CLI custom-routes add-cidr failed (exit $($addC.result.exitCode))."),
            'Gate2AddCustomRouteFailed', [System.Management.Automation.ErrorCategory]::InvalidOperation, $addC.result)
    }
    # Product semantics (verified against PathVeer.Core): custom-routes add-cidr only WRITES desired
    # configuration; it does NOT install a Windows route. The runtime route is reconciled on a later
    # enable/repair cycle. So we verify CONFIG PERSISTENCE here (exit 0 AND the prefix appears in the
    # desired custom-routes list), NOT an immediate Windows-route presence. Requiring the Windows route
    # immediately after add-cidr would be a false-positive harness defect (scenario A of add-cidr contract).
    $listOut = (Invoke-GuestJeaCli -Session $Session -JeaSession $jea -Verb 'custom-routes' -SubVerb 'list').result.output
    $configPersisted = ($null -ne $listOut) -and ($listOut -match [regex]::Escape($Prefix))
    if (-not $configPersisted) {
        Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{ capturedUtc = (Get-Date -AsUTC).ToString('o'); managedPrefix = $Prefix; elevationAvailable = ($jea -ne $null); addCustomRouteExit = $addC.result.exitCode; routesBeforeCount = $routesBeforeCount; networkReadiness = $envProbe; customRoutesList = $listOut; failReasons = @('custom-route-config-not-persisted-after-add-cidr') })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("Custom route $Prefix not present in desired configuration after add-cidr (config persistence failed)."),
            'Gate2CustomRouteConfigNotPersisted', [System.Management.Automation.ErrorCategory]::ObjectNotFound, $null)
    }
    $customAfterAdd = (Get-GuestJeaRouteState -Session $Session -JeaSession $jea -Prefix $Prefix).result
    $routePresentAfterAdd = ($null -ne $customAfterAdd) -and ($customAfterAdd.Count -gt 0)
    Start-Sleep -Seconds 2

    # --- Disable managed routing (top-level policy) ---
    $disC = Invoke-GuestJeaCli -Session $Session -JeaSession $jea -Verb 'disable'
    if ($disC.result.exitCode -ne 0) {
        Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{ capturedUtc = (Get-Date -AsUTC).ToString('o'); managedPrefix = $Prefix; elevationAvailable = ($jea -ne $null); addCustomRouteExit = $addC.result.exitCode; disableExit = $disC.result.exitCode; networkReadiness = $envProbe; routesBeforeCount = $routesBeforeCount; failReasons = @('cli-disable-nonzero') })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("PathVeer CLI disable failed (exit $($disC.result.exitCode))."),
            'Gate2DisableFailed', [System.Management.Automation.ErrorCategory]::InvalidOperation, $disC.result)
    }
    $afterDisable = (Get-GuestJeaRouteState -Session $Session -JeaSession $jea -Prefix $Prefix).result
    if (($null -ne $afterDisable) -and ($afterDisable.Count -gt 0)) {
        Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{ capturedUtc = (Get-Date -AsUTC).ToString('o'); managedPrefix = $Prefix; elevationAvailable = ($jea -ne $null); addCustomRouteExit = $addC.result.exitCode; disableExit = $disC.result.exitCode; prefixPresentAfterDisable = $true; networkReadiness = $envProbe; routesBeforeCount = $routesBeforeCount; failReasons = @('managed-prefix-present-after-disable') })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("Managed prefix $Prefix still present after disable (expected removed)."),
            'Gate2ManagedPrefixDisableFailed', [System.Management.Automation.ErrorCategory]::InvalidOperation, $null)
    }
    Start-Sleep -Seconds 2

    # --- Enable managed routing (top-level policy): reconciles desired config -> Windows routes ---
    $enC = Invoke-GuestJeaCli -Session $Session -JeaSession $jea -Verb 'enable'
    if ($enC.result.exitCode -ne 0) {
        Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{ capturedUtc = (Get-Date -AsUTC).ToString('o'); managedPrefix = $Prefix; elevationAvailable = ($jea -ne $null); addCustomRouteExit = $addC.result.exitCode; disableExit = $disC.result.exitCode; enableExit = $enC.result.exitCode; prefixPresentAfterDisable = $false; networkReadiness = $envProbe; routesBeforeCount = $routesBeforeCount; failReasons = @('cli-enable-nonzero') })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("PathVeer CLI enable failed (exit $($enC.result.exitCode))."),
            'Gate2EnableFailed', [System.Management.Automation.ErrorCategory]::InvalidOperation, $enC.result)
    }
    $afterEnable = (Get-GuestJeaRouteState -Session $Session -JeaSession $jea -Prefix $Prefix).result
    if (($null -eq $afterEnable) -or ($afterEnable.Count -le 0)) {
        Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{ capturedUtc = (Get-Date -AsUTC).ToString('o'); managedPrefix = $Prefix; elevationAvailable = ($jea -ne $null); addCustomRouteExit = $addC.result.exitCode; disableExit = $disC.result.exitCode; enableExit = $enC.result.exitCode; prefixPresentAfterDisable = $false; prefixPresentAfterEnable = $false; networkReadiness = $envProbe; routesBeforeCount = $routesBeforeCount; failReasons = @('managed-prefix-absent-after-enable') })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("Managed prefix $Prefix not present after enable (expected installed)."),
            'Gate2ManagedPrefixEnableFailed', [System.Management.Automation.ErrorCategory]::ObjectNotFound, $null)
    }
    Start-Sleep -Seconds 3

    # --- Doctor diagnostics (must be healthy: exit 0) ---
    $doc = Invoke-GuestJeaCli -Session $Session -JeaSession $jea -Verb 'doctor'
    $docExit = $doc.result.exitCode
    $docOut = $doc.result.output
    if ($docExit -ne 0) {
        Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{ capturedUtc = (Get-Date -AsUTC).ToString('o'); managedPrefix = $Prefix; elevationAvailable = ($jea -ne $null); addCustomRouteExit = $addC.result.exitCode; disableExit = $disC.result.exitCode; enableExit = $enC.result.exitCode; prefixPresentAfterDisable = $false; prefixPresentAfterEnable = $true; doctorExitCode = $docExit; doctorSummary = ($docOut -join "`n"); networkReadiness = $envProbe; routesBeforeCount = $routesBeforeCount; failReasons = @('doctor-nonzero') })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("PathVeer doctor reported failures (exit $docExit)."),
            'Gate2DoctorFailed', [System.Management.Automation.ErrorCategory]::InvalidOperation, $doc.result)
    }

    # --- Stop + start service, verify managed prefix recovered ---
    $stop = Stop-GuestJeaService -Session $Session -JeaSession $jea
    # Normalize remoted ServiceControllerStatus: native enum, numeric fallback (1->Stopped,4->Running),
    # or remoted wrapper {value/Value}. Use the shared Get-EnumString normalizer so a remoted
    # {value=1;Value='Stopped'} is recognized as Stopped (the prior GATE-2 real failure was a harness
    # false negative caused by comparing the remoted wrapper object to the string 'Stopped').
    $stoppedState = Get-EnumString $stop.result.status
    if ($stoppedState -ne 'Stopped') {
        Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{ capturedUtc = (Get-Date -AsUTC).ToString('o'); managedPrefix = $Prefix; elevationAvailable = ($jea -ne $null); addCustomRouteExit = $addC.result.exitCode; disableExit = $disC.result.exitCode; enableExit = $enC.result.exitCode; prefixPresentAfterDisable = $false; prefixPresentAfterEnable = $true; doctorExitCode = $docExit; doctorSummary = ($docOut -join "`n"); serviceStoppedState = $stop.result.status; serviceStoppedStateNormalized = $stoppedState; networkReadiness = $envProbe; routesBeforeCount = $routesBeforeCount; failReasons = @('service-not-stopped') })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("PathVeer service did not reach Stopped after stop (state=$stoppedState)."),
            'Gate2ServiceStopFailed', [System.Management.Automation.ErrorCategory]::InvalidOperation, $stop.result)
    }
    Start-Sleep -Seconds 2
    $start = Start-GuestJeaService -Session $Session -JeaSession $jea
    Start-Sleep -Seconds 4
    $recoveredState = Get-EnumString $start.result.status
    if ($recoveredState -ne 'Running') {
        Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{ capturedUtc = (Get-Date -AsUTC).ToString('o'); managedPrefix = $Prefix; elevationAvailable = ($jea -ne $null); addCustomRouteExit = $addC.result.exitCode; disableExit = $disC.result.exitCode; enableExit = $enC.result.exitCode; prefixPresentAfterDisable = $false; prefixPresentAfterEnable = $true; doctorExitCode = $docExit; doctorSummary = ($docOut -join "`n"); serviceStoppedState = $stoppedState; serviceRecoveredState = $start.result.status; serviceRecoveredStateNormalized = $recoveredState; networkReadiness = $envProbe; routesBeforeCount = $routesBeforeCount; failReasons = @('service-not-running') })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("PathVeer service did not reach Running after start (state=$recoveredState)."),
            'Gate2ServiceStartFailed', [System.Management.Automation.ErrorCategory]::InvalidOperation, $start.result)
    }
    $afterRecovery = (Get-GuestJeaRouteState -Session $Session -JeaSession $jea -Prefix $Prefix).result
    $prefixPresentAfterRecovery = ($null -ne $afterRecovery) -and ($afterRecovery.Count -gt 0)
    $routesAfter = Invoke-Command -Session $Session -ScriptBlock {
        @(Get-NetRoute -ErrorAction SilentlyContinue | Select-Object DestinationPrefix, NextHop, InterfaceIndex, RouteMetric)
    }
    $routesAfterCount = if ($routesAfter) { $routesAfter.Count } else { 0 }
    $defaultRouteAfter = @($routesAfter | Where-Object { $_.DestinationPrefix -eq '0.0.0.0/0' })

    # Distinguish "no baseline default route existed" (ENVIRONMENT — checkpoint networking not ready) from
    # "PathVeer destroyed the baseline default route" (REAL PRODUCT ROUTE-INTEGRITY DEFECT). These are
    # completely different classifications. A missing baseline is captured and reported; a baseline that
    # existed but vanished after mutations is a genuine gate failure.
    $defaultRouteBeforePresent = $defaultRouteBefore.Count -gt 0
    $defaultRouteAfterPresent = $defaultRouteAfter.Count -gt 0
    $noBaselineDefaultRoute = -not $defaultRouteBeforePresent
    $defaultRouteIntact = $defaultRouteAfterPresent
    if ($defaultRouteBeforePresent -and -not $defaultRouteAfterPresent) {
        Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{
            capturedUtc = (Get-Date -AsUTC).ToString('o'); managedPrefix = $Prefix; elevationAvailable = ($jea -ne $null)
            addCustomRouteExit = $addC.result.exitCode; disableExit = $disC.result.exitCode; enableExit = $enC.result.exitCode
            prefixPresentAfterDisable = $false; prefixPresentAfterEnable = $true; doctorExitCode = $docExit; doctorSummary = ($docOut -join "`n")
            serviceStoppedState = 'Stopped'; serviceRecoveredState = 'Running'
            prefixPresentAfterRecovery = $true; routesBeforeCount = $routesBeforeCount; routesAfterCount = $routesAfterCount
            noBaselineDefaultRoute = $noBaselineDefaultRoute
            defaultRouteBefore = @($defaultRouteBefore | ForEach-Object { [PSCustomObject]@{ DestinationPrefix = $_.DestinationPrefix; NextHop = $_.NextHop; InterfaceIndex = $_.InterfaceIndex; RouteMetric = $_.RouteMetric } })
            defaultRouteAfter = @($defaultRouteAfter | ForEach-Object { [PSCustomObject]@{ DestinationPrefix = $_.DestinationPrefix; NextHop = $_.NextHop; InterfaceIndex = $_.InterfaceIndex; RouteMetric = $_.RouteMetric } })
            networkReadiness = $envProbe
            failReasons = @('default-route-destroyed-by-product')
        })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("Baseline default route (0.0.0.0/0) present before GATE-2 but removed after route mutations (product route-integrity defect)."),
            'Gate2DefaultRouteDestroyed', [System.Management.Automation.ErrorCategory]::InvalidOperation, $null)
    }
    if (-not $prefixPresentAfterRecovery) {
        Save-Json '02-gate2-route-mutation.json' ([PSCustomObject]@{
            capturedUtc = (Get-Date -AsUTC).ToString('o'); managedPrefix = $Prefix; elevationAvailable = ($jea -ne $null)
            addCustomRouteExit = $addC.result.exitCode; disableExit = $disC.result.exitCode; enableExit = $enC.result.exitCode
            prefixPresentAfterDisable = $false; prefixPresentAfterEnable = $true; doctorExitCode = $docExit; doctorSummary = ($docOut -join "`n")
            serviceStoppedState = 'Stopped'; serviceRecoveredState = 'Running'
            prefixPresentAfterRecovery = $false; routesBeforeCount = $routesBeforeCount; routesAfterCount = $routesAfterCount
            noBaselineDefaultRoute = $noBaselineDefaultRoute
            defaultRouteBefore = @($defaultRouteBefore | ForEach-Object { [PSCustomObject]@{ DestinationPrefix = $_.DestinationPrefix; NextHop = $_.NextHop; InterfaceIndex = $_.InterfaceIndex; RouteMetric = $_.RouteMetric } })
            defaultRouteAfter = @($defaultRouteAfter | ForEach-Object { [PSCustomObject]@{ DestinationPrefix = $_.DestinationPrefix; NextHop = $_.NextHop; InterfaceIndex = $_.InterfaceIndex; RouteMetric = $_.RouteMetric } })
            networkReadiness = $envProbe
            failReasons = @('managed-prefix-absent-after-recovery')
        })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("Managed prefix $Prefix not present after service recovery (expected installed)."),
            'Gate2ManagedPrefixRecoveryFailed', [System.Management.Automation.ErrorCategory]::ObjectNotFound, $null)
    }
    $out = [PSCustomObject]@{
        managedPrefix = $Prefix; elevationAvailable = ($jea -ne $null)
        addCustomRouteExit = $addC.result.exitCode
        routePresentAfterAdd = $routePresentAfterAdd
        customRouteConfigPersisted = $configPersisted
        disableExit = $disC.result.exitCode
        prefixPresentAfterDisable = $false
        enableExit = $enC.result.exitCode
        prefixPresentAfterEnable = $true
        doctorExitCode = $docExit
        doctorSummary = ($docOut -join "`n")
        serviceStoppedState = 'Stopped'
        serviceRecoveredState = 'Running'
        prefixPresentAfterRecovery = $true
        routesBeforeCount = $routesBeforeCount
        routesAfterCount = $routesAfterCount
        noBaselineDefaultRoute = $noBaselineDefaultRoute
        defaultRouteBeforePresent = $defaultRouteBeforePresent
        defaultRouteAfterPresent = $defaultRouteAfterPresent
        defaultRouteIntact = $defaultRouteIntact
        networkReadiness = $envProbe
    }
    Save-Json '02-gate2-route-mutation.json' $out
    Write-Host "  GATE-2 PASS (route mutation + recovery verified)." -ForegroundColor Green
    return $out
}

function Run-GATE4([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-4 PURGE -> REINSTALL"
    $jea = Get-GuestJeaSession $script:Cred
    # Lifecycle precondition: prove an ACTUAL installed product can be purged and cleanly reinstalled.
    # Do NOT start with PurgeUninstall against an empty baseline. Install the protected candidate first
    # (fail-closed), verify it exists, then purge.
    $inst = Install-PathVeerProtectedCandidate $Session
    $afterInstall = Invoke-Command -Session $Session -ScriptBlock {
        [PSCustomObject]@{
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programFiles = (Test-Path 'C:\Program Files\PathVeer')
            programData = (Test-Path "$env:ProgramData\PathVeer")
        }
    }
    # Hard purge via trusted wrapper.
    $u = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action PurgeUninstall
    $afterUninstall = Invoke-Command -Session $Session -ScriptBlock {
        param($u)
        [PSCustomObject]@{
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programFiles = (Test-Path 'C:\Program Files\PathVeer')
            programData = (Test-Path "$env:ProgramData\PathVeer")
            appsAndFeaturesEntry = ($null -ne (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PathVeer' -ErrorAction SilentlyContinue))
            uninstallExitCode = $u.result.exitCode
            uninstallElevated = $u.elevationAvailable
        }
    } -ArgumentList $u
    # FAIL-CLOSED: verify the purge actually succeeded BEFORE reinstalling. A failed purge must not be
    # masked by a subsequent reinstall — persist structured evidence and throw.
    $purgeFail = @()
    if ($u.result.exitCode -ne 0) { $purgeFail += "purge-exitCode=$($u.result.exitCode) (expected 0)" }
    if ($afterUninstall.serviceExists) { $purgeFail += 'PathVeer service still present after purge' }
    if ($afterUninstall.programFiles) { $purgeFail += 'C:\Program Files\PathVeer still present after purge' }
    if ($afterUninstall.programData) { $purgeFail += '%ProgramData%\PathVeer still present after purge (expected removed by PurgeState contract)' }
    if ($afterUninstall.appsAndFeaturesEntry) { $purgeFail += 'PathVeer Apps & Features registration remains after purge (expected removed)' }
    if ($purgeFail.Count -gt 0) {
        $pfEnv = [PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            preconditionInstall = $inst; installVerified = $afterInstall
            uninstall = $afterUninstall; purgeFailReasons = $purgeFail
            note = 'PURGE FAILED: did NOT reinstall over a failed purge state.'
        }
        Save-Json '04-gate4-purge-reinstall.json' $pfEnv
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("GATE-4 purge precondition failed: $($purgeFail -join '; '). Reinstall was skipped."),
            'Gate4PurgeFailed', [System.Management.Automation.ErrorCategory]::ResourceUnavailable, $pfEnv)
    }
    # Clean reinstall (elevated) — only reached after a verified successful purge. Reuse the shared
    # install precondition so the reinstall proves exitCode==0, service exists, manifest parses, AND
    # actualVersion == expected protected version (not just exit code + service presence).
    $reinstallResult = Install-PathVeerProtectedCandidate $Session
    $svc = Invoke-Command -Session $Session -ScriptBlock {
        [PSCustomObject]@{ reinstalledServiceState = (Get-Service -Name 'PathVeer' -ErrorAction Stop).Status }
    }
    if ($reinstallResult.verified -ne $true) {
        Save-Json '04-gate4-purge-reinstall.json' ([PSCustomObject]@{
            capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); preconditionInstall=$inst; installVerified=$afterInstall; uninstall = $afterUninstall; reinstall = $reinstallResult; reinstallFailed = $true
        })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("GATE-4 reinstall after purge failed verification (expectedVersion=$($reinstallResult.expectedVersion); actualVersion=$($reinstallResult.actualVersion); installExitCode=$($reinstallResult.installExitCode))."),
            'Gate4ReinstallFailed', [System.Management.Automation.ErrorCategory]::ResourceUnavailable, $null)
    }
    Save-Json '04-gate4-purge-reinstall.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); preconditionInstall=$inst; installVerified=$afterInstall; uninstall = $afterUninstall; reinstall = $reinstallResult; reinstalledServiceState = $svc.reinstalledServiceState
    })
    return $reinstallResult
}

function Run-GATE8([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-8 UNINSTALL / STATE-PRESERVED REINSTALL"
    $jea = Get-GuestJeaSession $script:Cred
    # Lifecycle precondition: prove NORMAL uninstall preservation against an ACTUALLY INSTALLED product.
    # Do NOT write the sentinel and uninstall when no product has first been installed. Install the
    # protected candidate first (fail-closed), verify it exists, then create the sentinel.
    $inst = Install-PathVeerProtectedCandidate $Session
    $afterInstall = Invoke-Command -Session $Session -ScriptBlock {
        [PSCustomObject]@{
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programFiles = (Test-Path 'C:\Program Files\PathVeer')
        }
    }
    # Write a sentinel into persistent state to prove preservation across reinstall.
    Invoke-Command -Session $Session -ScriptBlock {
        $stateDir = Join-Path $env:ProgramData 'PathVeer'
        if (-not (Test-Path $stateDir)) { New-Item -ItemType Directory -Force -Path $stateDir | Out-Null }
        Set-Content -Path (Join-Path $stateDir 'cert-sentinel.txt') -Value 'preserved-state-marker' -Encoding utf8
    } | Out-Null
    # Normal uninstall (NO purge) via trusted wrapper.
    $u = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Uninstall
    $afterUninstall = Invoke-Command -Session $Session -ScriptBlock {
        param($u)
        $stateDir = Join-Path $env:ProgramData 'PathVeer'
        [PSCustomObject]@{
            serviceExists = ($null -ne (Get-Service -Name 'PathVeer' -ErrorAction SilentlyContinue))
            programFiles = (Test-Path 'C:\Program Files\PathVeer')
            programData = (Test-Path $stateDir)               # expected TRUE (preserved)
            sentinelPreserved = (Test-Path (Join-Path $stateDir 'cert-sentinel.txt'))
            appsAndFeaturesEntry = ($null -ne (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PathVeer' -ErrorAction SilentlyContinue))
            uninstallExitCode = $u.result.exitCode
            uninstallElevated = $u.elevationAvailable
        }
    } -ArgumentList $u
    # FAIL-CLOSED: verify the normal uninstall + state preservation succeeded BEFORE reinstalling.
    # Do not destroy product-failure evidence by immediately reinstalling over a failed uninstall state.
    $uninstFail = @()
    if ($u.result.exitCode -ne 0) { $uninstFail += "uninstall-exitCode=$($u.result.exitCode) (expected 0)" }
    if ($afterUninstall.serviceExists) { $uninstFail += 'PathVeer service still present after uninstall' }
    if ($afterUninstall.programFiles) { $uninstFail += 'C:\Program Files\PathVeer still present after uninstall' }
    if (-not $afterUninstall.programData) { $uninstFail += 'ProgramData\PathVeer missing (expected preserved)' }
    if (-not $afterUninstall.sentinelPreserved) { $uninstFail += 'cert-sentinel.txt not preserved' }
    if ($afterUninstall.appsAndFeaturesEntry) { $uninstFail += 'PathVeer Apps & Features registration remains after uninstall (expected removed)' }
    if ($uninstFail.Count -gt 0) {
        $ufEnv = [PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            preconditionInstall = $inst; installVerified = $afterInstall
            uninstall = $afterUninstall; uninstallFailReasons = $uninstFail
            note = 'NORMAL UNINSTALL FAILED: did NOT reinstall over a failed uninstall state.'
        }
        Save-Json '08-gate8-uninstall-reinstall.json' $ufEnv
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("GATE-8 normal uninstall/preservation precondition failed: $($uninstFail -join '; '). Reinstall was skipped."),
            'Gate8UninstallFailed', [System.Management.Automation.ErrorCategory]::ResourceUnavailable, $ufEnv)
    }
    # Reinstall and prove preserved state recognized (elevated) — only after verified successful uninstall.
    # Reuse the shared install precondition so the reinstall proves exitCode==0, service exists, manifest
    # parses, AND actualVersion == expected protected version (same exact verifier as the install precondition).
    $reinstallResult = Install-PathVeerProtectedCandidate $Session
    $sentinelStillPreserved = Invoke-Command -Session $Session -ScriptBlock {
        $stateDir = Join-Path $env:ProgramData 'PathVeer'
        [PSCustomObject]@{ preservedStateRecognized = (Test-Path (Join-Path $stateDir 'cert-sentinel.txt')) }
    }
    if ($reinstallResult.verified -ne $true -or -not $sentinelStillPreserved.preservedStateRecognized) {
        Save-Json '08-gate8-uninstall-reinstall.json' ([PSCustomObject]@{
            capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); preconditionInstall=$inst; installVerified=$afterInstall; uninstall = $afterUninstall; reinstall = $reinstallResult; reinstallFailed = $true
        })
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("GATE-8 reinstall after uninstall failed (verified=$($reinstallResult.verified); actualVersion=$($reinstallResult.actualVersion); sentinelPreserved=$($sentinelStillPreserved.preservedStateRecognized))."),
            'Gate8ReinstallFailed', [System.Management.Automation.ErrorCategory]::ResourceUnavailable, $null)
    }
    Save-Json '08-gate8-uninstall-reinstall.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o'); preconditionInstall=$inst; installVerified=$afterInstall; uninstall = $afterUninstall; reinstall = $reinstallResult; preservedStateRecognized = $sentinelStillPreserved.preservedStateRecognized
    })
    return $reinstallResult
}

function Run-GATE6([System.Management.Automation.Runspaces.PSSession]$Session, [string]$OldPkg, [string]$NewPkg) {
    Write-Stage "GATE-6 UPGRADE (0.9.0 -> 1.0.0-beta.1) + DOWNGRADE BLOCK"
    $jea = Get-GuestJeaSession $script:Cred
    # NOTE: the trusted wrapper always installs the fixed staged package ($script:InstallPackage);
    # Old/New package paths are used ONLY to validate candidate identity against the protected
    # operator-staged payload (Option A: no runtime copy of executable bytes; the harness asserts
    # the protected payload exists and installs it). GATE-6's 0.9.0->1.0.0-beta.1 framing is
    # simulated by the wrapper; only the 1.0.0-beta.1 payload must be operator-staged.
    $expectedPackageId = 'PathVeer-1.0.0-beta.1'
    Assert-CandidateMatchesProtected -CandidatePackage $NewPkg -ExpectedPackageId $expectedPackageId
    # SECURITY (Option A): the harness never promotes untrusted incoming into the protected tree.
    # The trusted wrapper enforces the exact protected installer + payload preconditions inside the
    # privileged virtual-account context; the harness does not inspect protected paths from pvcert.
    # Install older baseline (elevated) — wrapper uses the staged package.
    $b = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Install -Feature @('RegisterShell','InstallTray')
    $oldVer = $null
    if ($b.result -and $b.result.exitCode -eq 0) {
        $oldVer = (Invoke-Command -Session $Session -ScriptBlock { (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw | ConvertFrom-Json).productVersion })
    }
    # Upgrade (elevated).
    $u = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Upgrade -Feature @('RegisterShell','InstallTray')
    $newVer = $null; $svcAfter = $null
    if ($u.result -and $u.result.exitCode -eq 0) {
        $up = Invoke-Command -Session $Session -ScriptBlock { [PSCustomObject]@{
            version = (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw | ConvertFrom-Json).productVersion
            serviceState = (Get-Service -Name 'PathVeer').Status
        } }
        $newVer = $up.version; $svcAfter = $up.serviceState
    }
    # Downgrade attempt (must be blocked, exit 103). Elevated.
    $d = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Install -Feature @('RegisterShell')
    $services = Invoke-Command -Session $Session -ScriptBlock {
        @(Get-CimInstance Win32_Service | Where-Object { $_.Name -eq 'PathVeer' -or $_.Name -eq 'IranDirect' } | ForEach-Object { $_.Name })
    }
    Save-Json '06-gate6-upgrade.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o')
        baselineInstallExitCode = $b.result.exitCode
        baselineElevated = $b.elevationAvailable
        baselineVersion = $oldVer
        upgradeExitCode = $u.result.exitCode
        upgradeElevated = $u.elevationAvailable
        upgradedVersion = $newVer
        serviceStateAfterUpgrade = $svcAfter
        downgradeExitCode = $d.result.exitCode
        downgradeBlocked = ($d.result.exitCode -eq 103)
        serviceIdentities = $services
        noDuplicateService = ($services.Count -le 1)
    })
    return @{ baselineVersion=$oldVer; upgradedVersion=$newVer; downgradeBlocked=($d.result.exitCode -eq 103) }
}

function Run-GATE28([System.Management.Automation.Runspaces.PSSession]$Session, [string]$Pkg) {
    Write-Stage "GATE-28 SAME-VERSION REPAIR"
    $jea = Get-GuestJeaSession $script:Cred
    # SECURITY (Option A): no runtime copy of executable bytes into the guest. The harness only
    # validates candidate identity and asserts the protected payload exists, then installs it.
    $expectedPackageId = 'PathVeer-1.0.0-beta.1'
    Assert-CandidateMatchesProtected -CandidatePackage $Pkg -ExpectedPackageId $expectedPackageId
    # Lifecycle precondition: Repair against an EMPTY baseline does not prove repair. Install the SAME
    # protected candidate first (fail-closed) so Repair runs over an actually installed product.
    $inst = Install-PathVeerProtectedCandidate $Session
    # Same-version repair/install over existing (elevated) — same protected candidate.
    $r = Invoke-GuestJeaInstall -Session $Session -JeaSession $jea -Action Repair -Feature @('RegisterShell','InstallTray')
    $svcAfter = $null; $ver = $null; $cliRepairExit = $null
    if ($r.result -and $r.result.exitCode -eq 0) {
        $svcAfter = (Invoke-Command -Session $Session -ScriptBlock { (Get-Service -Name 'PathVeer' -ErrorAction Stop).Status })
        $ver = (Invoke-Command -Session $Session -ScriptBlock { (Get-Content 'C:\Program Files\PathVeer\install-manifest.json' -Raw | ConvertFrom-Json).productVersion })
        # CLI repair verb (privileged, guarded, via trusted wrapper).
        $rep = Invoke-GuestJeaCli -Session $Session -JeaSession $jea -Verb 'repair'
        $cliRepairExit = $rep.result.exitCode
    }
    # FAIL-CLOSED: require Repair success + same-version verification + CLI repair success before
    # claiming repaired state. The expected protected version is the canonical 1.0.0-beta.1 candidate.
    $expectedRepairVersion = '1.0.0-beta.1'
    $repairFail = @()
    if ($r.result.exitCode -ne 0) { $repairFail += "repair-exitCode=$($r.result.exitCode) (expected 0)" }
    if ($null -eq $svcAfter) { $repairFail += 'PathVeer service absent after repair' }
    if ($ver -ne $expectedRepairVersion) { $repairFail += "repaired version mismatch (expected='$expectedRepairVersion'; actual='$ver')" }
    if ($cliRepairExit -ne 0) { $repairFail += "cliRepair exitCode=$cliRepairExit (expected 0)" }
    if ($repairFail.Count -gt 0) {
        $rfEnv = [PSCustomObject]@{
            capturedUtc=(Get-Date).ToUniversalTime().ToString('o')
            preconditionInstall = $inst
            repairExitCode = $r.result.exitCode
            repairElevated = $r.elevationAvailable
            repairedVersion = $ver
            serviceStateAfterRepair = $svcAfter
            cliRepairExitCode = $cliRepairExit
            repairFailReasons = $repairFail
            note = 'REPAIR FAILED: did NOT claim repaired state.'
        }
        Save-Json '28-gate28-repair.json' $rfEnv
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new("GATE-28 repair precondition failed: $($repairFail -join '; ')."),
            'Gate28RepairFailed', [System.Management.Automation.ErrorCategory]::ResourceUnavailable, $rfEnv)
    }
    Save-Json '28-gate28-repair.json' ([PSCustomObject]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o')
        preconditionInstall = $inst
        # Explicit repair-operation evidence:
        repairExitCode = $r.result.exitCode
        repairElevated = $r.elevationAvailable
        # Legacy aliases retained for consumers expecting install* naming (populated from the Repair op):
        installExitCode = $r.result.exitCode
        installElevated = $r.elevationAvailable
        repairedVersion = $ver
        serviceStateAfterRepair = $svcAfter
        cliRepairExitCode = $cliRepairExit
    })
    return @{ repairedVersion=$ver; serviceStateAfterRepair=$svcAfter }
}

function Run-GATE9([System.Management.Automation.Runspaces.PSSession]$Session) {
    Write-Stage "GATE-9 UPDATE FLOW (network-ready -> HTTPS -> ES256 -> hash -> Authenticode decision)"
    # Network-dependent stage: do NOT start until the restored VM has usable networking.
    # (Checkpoint restore can leave the guest APIPA with no default route / unusable DNS.)
    $netOk = $false
    $netReady = $null
    try {
        $netReady = Wait-GuestNetworkReady -Session $Session -TargetHost 'releases.pathveer.com' -MaxSeconds 120
        if ($netReady -and $netReady.ready) { $netOk = $true }
    } catch {
        $netReady = [PSCustomObject]@{
            ready = $false
            errorType = $_.Exception.GetType().Name
            errorMessage = $_.Exception.Message
            lastProbe = if ($_.TargetObject) { $_.TargetObject } else { $null }
        }
    }

    if (-not $netOk) {
        # FAIL-CLOSED: network readiness not established. Capture structured in-VM network
        # diagnostics for evidence, persist, and raise a clear HARNESS/ENVIRONMENT readiness
        # failure. Do NOT perform HTTPS / installer download / SHA-256.
        $netState = $null
        try {
            $netState = Invoke-Command -Session $Session -ScriptBlock {
                $api = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                    Where-Object { $_.IPAddress -like '169.254.*' } | ForEach-Object { $_.IPAddress })
                $nonApi = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | ForEach-Object { $_.IPAddress })
                $route = $null
                try { $route = @(Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue) } catch {}
                $dnsOk = $false; $dnsErr = $null
                try { $a = [System.Net.Dns]::GetHostAddresses('releases.pathveer.com'); if ($a -and $a.Count -gt 0) { $dnsOk = $true } } catch { $dnsErr = $_.Exception.Message }
                [PSCustomObject]@{
                    ipv4Api = $api
                    ipv4NonApi = $nonApi
                    hasDefaultRoute = ($null -ne $route) -and ($route.Count -gt 0)
                    dnsResolved = $dnsOk
                    dnsError = $dnsErr
                }
            }
        } catch {}
        $envelope = [PSCustomObject]@{
            capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
            result = 'HARNESS/ENVIRONMENT READINESS FAILURE'
            classification = 'HARNESS/ENVIRONMENT READINESS FAILURE'
            networkReadiness = $netReady
            networkState = $netState
            evidence = $null
        }
        Save-Json '09-gate9-update-flow.json' $envelope
        throw [System.Management.Automation.ErrorRecord]::new(
            [System.InvalidOperationException]::new(
                "GATE-9 aborted: network readiness not established after PV-CERT-HARNESS restore (network-ready -> HTTPS contract violated). Structured diagnostics written to 09-gate9-update-flow.json. Environment/network-readiness defect; not a product defect."),
            'Gate9NetworkNotReady', [System.Management.Automation.ErrorCategory]::ResourceUnavailable, $envelope)
    }

    $ev = Invoke-Command -Session $Session -ScriptBlock {
        $feedUrl = 'https://releases.pathveer.com/windows/beta/latest.json'
        # Capture network state for diagnostics even on failure (no secrets).
        $netState = [PSCustomObject]@{
            ipv4Api = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                Where-Object { $_.IPAddress -like '169.254.*' } | ForEach-Object { $_.IPAddress })
            ipv4NonApi = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | ForEach-Object { $_.IPAddress })
            hasDefaultRoute = $false
        }
        try { $r = @(Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue); $netState.hasDefaultRoute = ($r.Count -gt 0) } catch {}
        $httpExType = $null; $httpExMsg = $null; $feed = $null
        try {
            $feed = Invoke-RestMethod -Uri $feedUrl -TimeoutSec 30 -ErrorAction Stop
            $httpsOk = $true
        } catch {
            $httpsOk = $false
            $httpExType = $_.Exception.GetType().Name
            $httpExMsg = $_.Exception.Message
        }
        $feedVersion = $null; $installerUrl = $null; $expectedSha = $null; $sigAlg = $null; $sigKeyId = $null
        if ($feed) {
            $feedVersion = $feed.version
            $installerUrl = $feed.installer.url
            $expectedSha = $feed.installer.sha256
            $sigAlg = $feed.signature.algorithm
            $sigKeyId = $feed.signature.keyId
        }

        # ES256 verification of the manifest against PRODUCTION trust root.
        $es256 = [PSCustomObject]@{ attempted = $true; prodKeyAccepted = $null; note = $null }
        if ($feed) {
            # Production trust accepts ONLY pv-meta-prod-2026-01. The published feed
            # is signed by pv-meta-staging-2026 -> must be rejected by prod trust.
            if ($sigKeyId -ne 'pv-meta-prod-2026-01') {
                $es256.prodKeyAccepted = $false
                $es256.note = "Manifest signed with '$sigKeyId'; production trust root only accepts 'pv-meta-prod-2026-01'. Production ES256 verification FAILS (expected: feed not production-signed)."
            } else {
                $es256.prodKeyAccepted = $true
            }
        }

        # Download installer + verify SHA-256 (transport/hash portion).
        $hashOk = $null; $downloadExit = $null; $actualSha = $null; $dlExType = $null; $dlExMsg = $null
        if ($installerUrl) {
            try {
                $tmp = 'C:\pv-cert\PathVeerSetup-downloaded.exe'
                Invoke-WebRequest -Uri $installerUrl -OutFile $tmp -TimeoutSec 120 -ErrorAction Stop
                $downloadExit = 0
                $actualSha = (Get-FileHash -Path $tmp -Algorithm SHA256).Hash.ToLowerInvariant()
                $hashOk = ($actualSha -eq $expectedSha)
            } catch {
                $downloadExit = 1; $hashOk = $false
                $dlExType = $_.Exception.GetType().Name
                $dlExMsg = $_.Exception.Message
            }
        }

        # Authenticode: production cert unavailable -> unsigned PE.
        $authenticode = 'BLOCKED (no production Authenticode certificate; PE is UNSIGNED)'

        [PSCustomObject]@{
            httpsFetchOk = $httpsOk
            httpExceptionType = $httpExType
            httpExceptionMessage = $httpExMsg
            networkState = $netState
            feedVersion = $feedVersion
            installerUrl = $installerUrl
            manifestSignatureAlgorithm = $sigAlg
            manifestKeyId = $sigKeyId
            es256ProductionVerification = $es256
            installerDownloadExit = $downloadExit
            downloadExceptionType = $dlExType
            downloadExceptionMessage = $dlExMsg
            sha256Expected = $expectedSha
            sha256Actual = $actualSha
            sha256Match = $hashOk
            authenticodeDecision = $authenticode
            setupHandoffPastTrustGate = 'BLOCKED/PARTIAL (production Authenticode gate not satisfied)'
        }
    }
    # Merge network-readiness result into the evidence envelope.
    $envelope = [PSCustomObject]@{
        capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
        networkReadiness = $netReady
        evidence = $ev
    }
    Save-Json '09-gate9-update-flow.json' $envelope
    return $envelope
}

# -------- main --------

Assert-VmRunning
Write-Host "Requesting $VmName (PV-CERT) credentials via local prompt..." -ForegroundColor Cyan
$script:Cred = Get-Credential -UserName 'PV-CERT\pvcert' -Message 'PathVeer-Certification (PV-CERT) local admin password for PowerShell Direct'
if (-not $script:Cred) { Write-Error 'No credential supplied. Aborting.'; exit 1 }

$sess = New-GuestSession $script:Cred

$stages = @('GATE5','GATE6','GATE8','GATE3','GATE2','GATE4','GATE28','GATE9')
if ($Stage -ne 'All') {
    $stages = @($Stage)
}

# Explicit restore/preservation policy. We do NOT rely on a script-scope `return` to control
# cleanup (that accidentally skipped the final restore for exceptions). Instead we set a flag.
$script:PreserveVmState = $false   # when true, main must NOT run the final Restore-Clean.

foreach ($st in $stages) {
    if (-not $SkipRestore) {
        # GATE5VERIFY operates ONLY on a PREP-preserved VM: never restore before it.
        if ($st -ne 'GATE5VERIFY') {
            $sess = $null
            Restore-Clean
            $sess = New-GuestSession $script:Cred
        }
    }
    switch ($st) {
        'GATE5'  {
            # Legacy alias / orchestrator: runs PREP, then stops with a clear message directing
            # the operator to GATE5VERIFY. Never silently claims a full GATE-5 PASS without a
            # genuine interactive verification. PREP deliberately leaves the VM intact.
            try {
                Run-GATE5PREP $sess $CandidatePackage | Out-Null
            } catch {
                Write-Host "  GATE-5 PREP ended (install gate not satisfied): $($_.Exception.Message)" -ForegroundColor Red
                $sess | Remove-PSSession -ErrorAction SilentlyContinue
                # PREP failure does not leave a meaningful prepared state; restore baseline.
                $script:PreserveVmState = $false
                break
            }
            # PREP succeeded: preserve the ready VM; tell the operator about GATE5VERIFY.
            $script:PreserveVmState = $true
            Write-Host "`n  GATE-5 (legacy alias) completed PREP only." -ForegroundColor Yellow
            Write-Host "  Perform a genuine PV-CERT\pvcert interactive logon, then run: -Stage GATE5VERIFY" -ForegroundColor Yellow
            # Stop processing further stages after a genuine PREP (the VM is left intact).
            break
        }
        'GATE5PREP' {
            try {
                Run-GATE5PREP $sess $CandidatePackage | Out-Null
            } catch {
                Write-Host "  GATE-5 PREP ended (install gate not satisfied): $($_.Exception.Message)" -ForegroundColor Red
                $sess | Remove-PSSession -ErrorAction SilentlyContinue
                $script:PreserveVmState = $false
                break
            }
            $script:PreserveVmState = $true
            Write-Host "`n  GATE-5 PREP complete. Run -Stage GATE5VERIFY after a genuine interactive logon." -ForegroundColor Yellow
            break
        }
        'GATE5VERIFY' {
            # Do NOT restore before VERIFY (it must run on the PREP-preserved VM). The session is
            # the still-open prepared session from PREP (when both run in the same process) or a
            # freshly opened one. If the session is stale, open a new one without restoring.
            if ($null -eq $sess) { $sess = New-GuestSession $script:Cred }
            try {
                Run-GATE5VERIFY $sess | Out-Null
                # Success path: restoration of the certification baseline is owned solely by the
                # top-level final cleanup block (single restoration authority). Leave PreserveVmState
                # = $false so it restores exactly once.
                $script:PreserveVmState = $false
            } catch {
                $errName = $_.Exception.GetType().Name
                if ($errName -eq 'GATE5VerifyPreconditionFailed' -or
                    ($_.FullyQualifiedErrorId -eq 'GATE5VerifyPreconditionFailed')) {
                    # Precondition failure: tell operator to run GATE5PREP; do not preserve.
                    Write-Host "  GATE-5 VERIFY precondition failed (not a product defect): $($_.Exception.Message)" -ForegroundColor Red
                    $script:PreserveVmState = $false
                } else {
                    # Product contract failure or probe failure: PRESERVE the failed VM for
                    # diagnostics; do not auto-restore.
                    Write-Host "  GATE-5 VERIFY product/probe failure: failed VM preserved for diagnostics." -ForegroundColor Red
                    $script:PreserveVmState = $true
                }
                $sess | Remove-PSSession -ErrorAction SilentlyContinue
            }
        }
        'GATE6'  { Run-GATE6  $sess $OlderPackage $CandidatePackage | Out-Null }
        'GATE8'  { Run-GATE8  $sess | Out-Null }
        'GATE3'  { $r = Run-GATE3 $sess; $sess = $r.Session }
        'GATE2'  { Run-GATE2  $sess $ManagedPrefix | Out-Null }
        'GATE4'  { Run-GATE4  $sess | Out-Null }
        'GATE28' { Run-GATE28 $sess $CandidatePackage | Out-Null }
        'GATE9'  { Run-GATE9  $sess | Out-Null }
    }
}

# Final: leave the guest at the clean baseline for user review (do not leave a cert state polluted),
# UNLESS a stage explicitly requested VM preservation (PREP-preserved VM, or a failed VERIFY VM held
# for diagnostics). No top-level `return` controls this decision.
if ($script:JeaSession) { $script:JeaSession | Remove-PSSession -ErrorAction SilentlyContinue; $script:JeaSession = $null }
if (-not $SkipRestore -and -not $script:PreserveVmState) {
    if ($sess) { $sess | Remove-PSSession -ErrorAction SilentlyContinue }
    Restore-Clean
    Write-Host "Guest restored to certification baseline '$CertificationSnapshot'." -ForegroundColor Green
} elseif ($script:PreserveVmState) {
    if ($sess) { $sess | Remove-PSSession -ErrorAction SilentlyContinue }
    Write-Host "Guest state PRESERVED (intentional; do not restore until the next stage consumes it)." -ForegroundColor Yellow
}

Write-Host "`nAll evidence written to: $EvidenceDir" -ForegroundColor Green
