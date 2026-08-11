<# .SYNOPSIS
    PathVeer VM certification - read-only installer trace capture (Phase 5 evidence).

    Inspects the FAILED GATE-5 guest WITHOUT restoring PV-CLEAN-WINDOWS, to recover the
    ACTUAL installer exit/result/log data that the harness captured in memory but never
    persisted. This is the decisive evidence for classifying the GATE-5 failure.

    SECRETS: none. Prompts the operator locally for the guest 'pvcert' credential via the
    native Windows credential dialog (never logged, never passed as an argument).

    OUTPUT: artifacts/certification/guest-state/<ts>-installer-trace.json on the HOST.
#>
[CmdletBinding()]
param(
    [string]$VMName = 'PathVeer-Certification',
    [string]$GuestUser = 'pvcert'
)

$ErrorActionPreference = 'Stop'

$cred = Get-Credential -UserName $GuestUser -Message "Enter the PathVeer-Certification guest password for '$GuestUser'"
if (-not $cred) { Write-Error 'No credential supplied. Aborting.'; exit 1 }

$session = $null
for ($i = 1; $i -le 12; $i++) {
    try {
        $session = New-PSSession -VMName $VMName -Credential $cred -ErrorAction Stop
        break
    } catch {
        if ($i -eq 12) { throw "PowerShell Direct failed after retries: $_" }
        Start-Sleep -Seconds 5
    }
}

try {
    $trace = Invoke-Command -Session $session -ScriptBlock {
        # The two files the harness copied into C:\pv-cert.
        $pvCertFiles = @(Get-ChildItem 'C:\pv-cert' -Force -ErrorAction SilentlyContinue |
            Select-Object Name, Length, LastWriteTime,
                @{n='SHA256'; e={ try { (Get-FileHash $_.FullName -Algorithm SHA256 -ErrorAction SilentlyContinue).Hash } catch { $null } }})

        # Installer structured output the harness requested.
        $resultFile    = $null
        $progressFile  = $null
        $progressLines = @()
        if (Test-Path 'C:\pv-cert\install-result.json') {
            try { $resultFile = Get-Content 'C:\pv-cert\install-result.json' -Raw -ErrorAction SilentlyContinue } catch {}
        }
        if (Test-Path 'C:\pv-cert\install-progress.json') {
            try { $progressFile = Get-Content 'C:\pv-cert\install-progress.json' -Raw -ErrorAction SilentlyContinue } catch {}
            try { $progressLines = @(Get-Content 'C:\pv-cert\install-progress.json' -ErrorAction SilentlyContinue) } catch {}
        }

        # Surviving PathVeer/install logs anywhere reasonable (read-only, scoped).
        $logRoots = @('C:\pv-cert', $env:TEMP, 'C:\Windows\Temp', "$env:LOCALAPPDATA\Temp", "$env:ProgramData")
        $logHits = @()
        foreach ($r in $logRoots) {
            if (-not (Test-Path $r)) { continue }
            $logHits += @(Get-ChildItem $r -Recurse -Force -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match 'PathVeer|install|setup' -and $_.Extension -match '\.(log|txt|json)$' } |
                Select-Object @{n='Root'; e={$r}}, FullName, Length, LastWriteTime)
        }

        # Any PathVeer files anywhere under Program Files (should be none on clean fail).
        $anyProgramFiles = @(Get-ChildItem 'C:\Program Files', 'C:\Program Files (x86)' `
            -Filter 'PathVeer*' -File -Recurse -ErrorAction SilentlyContinue | Select-Object FullName)

        # Is the current session user a member of the Administrators group?
        $ident    = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($ident)
        $isAdmin  = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

        [PSCustomObject]@{
            guestUser            = $env:USERNAME
            guestIsAdministrator = $isAdmin
            pvCertFileCount      = $pvCertFiles.Count
            pvCertFiles          = $pvCertFiles
            installResultFile    = $resultFile
            installProgressFile  = $progressFile
            installProgressLines = $progressLines
            survivingLogHits     = $logHits
            anyPathVeerInProgramFiles = $anyProgramFiles
            installRootExists    = (Test-Path 'C:\Program Files\PathVeer')
        }
    }

    $outDir = Join-Path $PSScriptRoot '..\..\artifacts\certification\guest-state'
    $outDir = [System.IO.Path]::GetFullPath($outDir)
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    $stamp  = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $outFile = Join-Path $outDir "$stamp-installer-trace.json"
    $trace | ConvertTo-Json -Depth 10 | Set-Content -Path $outFile -Encoding UTF8

    Write-Host ''
    Write-Host "Installer trace captured -> $outFile" -ForegroundColor Green
    Write-Host ''
    Write-Host ($trace | Format-List | Out-String)
} finally {
    if ($session) { Remove-PSSession $session -ErrorAction SilentlyContinue }
}
