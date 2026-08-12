<# PathVeer.Certification.Elevation.ps1
   *** RETIRED — DO NOT DOT-SOURCE OR INVOKE FROM THE HARNESS ***

   This file is preserved as HISTORICAL DIAGNOSTIC EVIDENCE of a rejected elevation design,
   not as active tooling. The Scheduled-Task (RunLevel Highest) bootstrap implemented here
   was rejected by real-VM Windows:
       error = Scheduled task registration/start failed: Access is denied.
   The filtered PowerShell Direct parent (PV-CERT\pvcert, UAC-filtered token) cannot register
   an elevated task, so the design is circular and cannot work on this certification VM.

   The replacement is the JEA certification control plane:
     - tools/certification/PathVeer.Certification.Jea.ps1     (host bridge: New-GuestJeaSession /
       Invoke-GuestJeaElevated / Invoke-GuestJeaScriptElevated)
     - tools/certification/jea/PathVeer.Certification.pssc     (session config)
     - tools/certification/jea/PathVeerCertificationRole.psrc   (narrow role capability)
     - tools/certification/jea/Enable-PathVeerCertificationJea.ps1   (operator bootstrap, elevated)
     - tools/certification/jea/Disable-PathVeerCertificationJea.ps1  (reversible teardown)
     - tools/certification/Test-PathVeerCertGuestJea.ps1        (harmless proof probe)

   The content below remains for audit reference only. The functions Invoke-GuestElevated and
   Invoke-GuestScriptElevated are no longer referenced by Invoke-PathVeerCertification.ps1.

   --- original design rationale (superseded) ---
   Why a Scheduled Task (not Start-Process -Verb RunAs / ProcessStartInfo):
   ----------------------------------------------------------------------
   The PowerShell Direct session runs as 'pvcert' (a member of Administrators but with a
   UAC-FILTERED token, so guestIsAdministrator reports False). A plain '&' spawn there is
   therefore non-elevated and the installer's Assert-Administrator aborts before any payload
   is written.

   Start-Process -Verb RunAs -Credential (i.e. System.Diagnostics.ProcessStartInfo with
   Verb=RunAs + UserName/Password) performs a CreateProcessWithLogonW-style logon. That does
   NOT elevate: it yields the same filtered (non-admin) token. So it cannot satisfy the
   requirement "child runs with an actually elevated administrative token".

   The correct non-interactive mechanism for a logged-on admin-group user is a Scheduled Task
   registered with RunLevel=Highest and LogonType=Interactive (running as the current user).
   The Task Scheduler service (SYSTEM) launches the task with the user's FULL (elevated) token
   -- no UAC consent dialog, and NO stored/passed password (Interactive reuses the existing
   interactive logon). UAC / Secure Boot policy is left fully intact.

   The elevated child captures its OWN identity and admin-role evidence and writes it to a
   result JSON. Callers MUST assert childIsAdministrator before treating any privileged
   operation as successful. If elevation cannot be obtained, the helper returns
   elevationAvailable=$false and NEVER executes the command non-elevated.
#>

function Assert-GuestResultDir {
    [CmdletBinding()]
    param(
        [System.Management.Automation.Runspaces.PSSession]$Session,
        [string]$ResultDir
    )
    # Runs INSIDE the guest. Ensures the temporary workspace exists before any file is written.
    # Idempotent: safe if the directory already exists (e.g. a gate staged C:\pv-cert earlier).
    Invoke-Command -Session $Session -ScriptBlock {
        param($d)
        if (-not (Test-Path -LiteralPath $d -PathType Container)) {
            New-Item -ItemType Directory -Path $d -Force -ErrorAction Stop | Out-Null
        }
    } -ArgumentList $ResultDir | Out-Null
}

function Invoke-GuestElevated {
    [CmdletBinding()]
    param(
        [System.Management.Automation.Runspaces.PSSession]$Session,
        [System.Management.Automation.PSCredential]$Cred,   # accepted for API parity; NOT used (no password persisted/exposed)
        [string]$Command,                                   # command line to run elevated
        [string]$ResultDir = 'C:\pv-cert',
        [int]$TimeoutSeconds = 900
    )
    $marker     = [guid]::NewGuid().ToString('N')
    $wrap       = "$ResultDir\elevated-$marker.ps1"
    $log        = "$ResultDir\elevated-$marker.log"
    $resultJson = "$ResultDir\elevated-$marker.result.json"

    # Wrapper runs INSIDE the elevated child. Captures the command's exit code, stdout/stderr,
    # and REAL elevation evidence (current identity + admin role).
    $wrapContent = @"
`$result = [PSCustomObject]@{
    started = `$true
    completed = `$false
    exitCode = `$null
    logFile = '$log'
    childUser = `$null
    childIsAdministrator = `$false
    elevationSucceeded = `$false
    error = `$null
}
try {
    `$id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    `$wp = New-Object System.Security.Principal.WindowsPrincipal(`$id)
    `$result.childUser = `$id.Name
    `$result.childIsAdministrator = `$wp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    `$result.elevationSucceeded = `$result.childIsAdministrator
    & $Command *> '$log'
    `$result.exitCode = `$LASTEXITCODE
    `$result.completed = `$true
} catch {
    `$result.error = `$_.Exception.Message
}
`$result | ConvertTo-Json -Depth 4 | Set-Content -Path '$resultJson' -Encoding utf8
exit ([int]`$result.exitCode)
"@

    $out = [PSCustomObject]@{
        started=$false; completed=$false; exitCode=$null
        logFile=$log; logContent=$null
        childUser=$null; childIsAdministrator=$false; elevationSucceeded=$false
        elevationAvailable=$false; error=$null
    }

    try {
        # 0) Ensure the guest working directory exists (clean snapshot may not have C:\pv-cert).
        Assert-GuestResultDir -Session $Session -ResultDir $ResultDir

        # 1) Write the wrapper (non-elevated; pvcert can write C:\pv-cert).
        Invoke-Command -Session $Session -ScriptBlock { param($f,$c) Set-Content -Path $f -Value $c -Encoding UTF8 } -ArgumentList $wrap, $wrapContent | Out-Null

        # 2) Register + start an ELEVATED scheduled task (pvcert, RunLevel Highest, Interactive).
        $taskName = "PV-Cert-Elevated-$marker"
        $reg = Invoke-Command -Session $Session -ScriptBlock {
            param($taskName, $wrap)
            try {
                $action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$wrap`""
                $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
                $null = Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force -ErrorAction Stop
                Start-ScheduledTask -TaskName $taskName -ErrorAction Stop
                return [PSCustomObject]@{ registered=$true; started=$true; error=$null }
            } catch {
                return [PSCustomObject]@{ registered=$false; started=$false; error=$_.Exception.Message }
            }
        } -ArgumentList $taskName, $wrap

        if (-not $reg.registered) {
            # Elevation unavailable: record structured failure. NEVER run the command non-elevated.
            $out.error = "Scheduled task registration/start failed: $($reg.error)"
            return $out
        }
        $out.started = $true

        # 3) Poll for completion.
        $done = $false
        for ($i = 0; $i -lt $TimeoutSeconds; $i++) {
            Start-Sleep -Seconds 1
            $info = Invoke-Command -Session $Session -ScriptBlock {
                param($tn)
                try {
                    $t = Get-ScheduledTask -TaskName $tn -ErrorAction Stop
                    return [PSCustomObject]@{
                        State      = $t.State.ToString()
                        LastResult = (Get-ScheduledTaskInfo -TaskName $tn -ErrorAction SilentlyContinue).LastTaskResult
                    }
                } catch { return $null }
            } -ArgumentList $taskName
            if ($null -eq $info) { $done = $true; break }
            if ($info.State -ne 'Running') { $done = $true; break }
        }

        # 4) Read structured result + log.
        $res = Invoke-Command -Session $Session -ScriptBlock {
            param($rj)
            if (Test-Path $rj) { try { return Get-Content $rj -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json } catch {} }
        } -ArgumentList $resultJson
        if ($res) {
            $out.completed             = $res.completed
            $out.exitCode              = $res.exitCode
            $out.childUser             = $res.childUser
            $out.childIsAdministrator  = $res.childIsAdministrator
            $out.elevationSucceeded    = $res.elevationSucceeded
            $out.error                 = $res.error
        }
        $out.logContent = Invoke-Command -Session $Session -ScriptBlock {
            param($l)
            if (Test-Path $l) { Get-Content $l -Raw -ErrorAction SilentlyContinue }
        } -ArgumentList $log

        # Elevation is "available" ONLY if the child actually held an elevated token.
        $out.elevationAvailable = ($out.elevationSucceeded -eq $true)
    } catch {
        $out.error = $_.Exception.Message
    } finally {
        # 5) Deterministic cleanup of the task + temporary wrapper/log/result files ONLY.
        #    Never delete C:\pv-cert itself (it may hold the product package / installer / evidence).
        try { Invoke-Command -Session $Session -ScriptBlock { param($tn) Unregister-ScheduledTask -TaskName $tn -Confirm:$false -ErrorAction SilentlyContinue } -ArgumentList $taskName | Out-Null } catch {}
        try { Invoke-Command -Session $Session -ScriptBlock { param($f) Remove-Item $f -Force -ErrorAction SilentlyContinue } -ArgumentList $wrap | Out-Null } catch {}
        try { Invoke-Command -Session $Session -ScriptBlock { param($f) Remove-Item $f -Force -ErrorAction SilentlyContinue } -ArgumentList $log | Out-Null } catch {}
        try { Invoke-Command -Session $Session -ScriptBlock { param($f) Remove-Item $f -Force -ErrorAction SilentlyContinue } -ArgumentList $resultJson | Out-Null } catch {}
    }
    return $out
}

<#
.SYNOPSIS
    Runs an entire guest SCRIPTBLOCK elevated and reads back a structured result object the
    script writes to a JSON file. Used for gates whose whole body is privileged (e.g. GATE-2
    route mutation + service control, GATE-22 contract checks) so we avoid scattering
    per-command elevation calls.
#>
function Invoke-GuestScriptElevated {
    [CmdletBinding()]
    param(
        [System.Management.Automation.Runspaces.PSSession]$Session,
        [System.Management.Automation.PSCredential]$Cred,
        [scriptblock]$ScriptBlock,
        [hashtable]$ArgumentList = @{},
        [string]$ResultDir = 'C:\pv-cert',
        [int]$TimeoutSeconds = 900
    )
    $marker     = [guid]::NewGuid().ToString('N')
    $scriptFile = "$ResultDir\elevated-script-$marker.ps1"
    $argJson    = "$ResultDir\elevated-script-$marker.args.json"
    $resultJson = "$ResultDir\elevated-script-$marker.result.json"

    $guestScript = @'
$argsIn = Get-Content '__ARGJSON__' -Raw | ConvertFrom-Json -AsHashtable
$result = & {
    param($a)
__BODY__
} $argsIn
$result | ConvertTo-Json -Depth 8 | Set-Content -Path '__RESULTJSON__' -Encoding utf8
'@
    $bodyText    = $ScriptBlock.ToString()
    $guestScript = $guestScript.Replace('__ARGJSON__', $argJson).Replace('__RESULTJSON__', $resultJson).Replace('__BODY__', $bodyText)

    # Ensure the guest working directory exists (clean snapshot may not have C:\pv-cert).
    Assert-GuestResultDir -Session $Session -ResultDir $ResultDir

    try {
        # Write the guest script + args (non-elevated).
        Invoke-Command -Session $Session -ScriptBlock {
            param($f, $c, $aj, $a)
            Set-Content -Path $f -Value $c -Encoding UTF8
            $a | ConvertTo-Json -Depth 8 | Set-Content -Path $aj -Encoding utf8
        } -ArgumentList $scriptFile, $guestScript, $argJson, $ArgumentList | Out-Null

        # Elevate `powershell.exe -File <script>`; the script writes its own result JSON.
        $run = Invoke-GuestElevated -Session $Session -Cred $Cred -Command "powershell.exe -File '$scriptFile'" -ResultDir $ResultDir -TimeoutSeconds $TimeoutSeconds

        $rb = Invoke-Command -Session $Session -ScriptBlock {
            param($rj)
            if (Test-Path $rj) { try { return Get-Content $rj -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json } catch {} }
        } -ArgumentList $resultJson

        return [PSCustomObject]@{ elevation = $run; result = $rb }
    } finally {
        # Ownership-aware cleanup: remove ONLY the temporary files this helper created.
        # Never delete C:\pv-cert itself (it may hold the product package / installer / evidence).
        try { Invoke-Command -Session $Session -ScriptBlock { param($f) Remove-Item $f -Force -ErrorAction SilentlyContinue } -ArgumentList $scriptFile | Out-Null } catch {}
        try { Invoke-Command -Session $Session -ScriptBlock { param($f) Remove-Item $f -Force -ErrorAction SilentlyContinue } -ArgumentList $argJson | Out-Null } catch {}
        try { Invoke-Command -Session $Session -ScriptBlock { param($f) Remove-Item $f -Force -ErrorAction SilentlyContinue } -ArgumentList $resultJson | Out-Null } catch {}
    }
}
