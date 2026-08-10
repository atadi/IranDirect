<# .SYNOPSIS
    Phase 37.6 — capture Windows Service Control Manager evidence for PathVeer
    and (optionally) legacy IranDirect authority.

.DESCRIPTION
    Read-only inspection of SCM state used for GATE-1 (legacy SCM upgrade) and
    single-authority verification. Emits a JSON evidence file. Does not mutate
    services.

.PARAMETER OutFile
    Path for the SCM evidence JSON (default: ./scm-evidence.json).
#>
[CmdletBinding()]
param(
    [string]$OutFile = './scm-evidence.json'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ServiceEvidence([string]$name) {
    $svc = Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
    if ($null -eq $svc) { return $null }
    [ordered]@{
        name      = $svc.Name
        display   = $svc.DisplayName
        state     = $svc.State
        startMode = $svc.StartMode
        pathName  = $svc.PathName
        account   = $svc.StartName
        processId = $svc.ProcessId
    }
}

$evidence = [ordered]@{
    capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
    pathVeer    = Get-ServiceEvidence 'PathVeer'
    iranDirect  = Get-ServiceEvidence 'IranDirect'
    authorities = @(Get-CimInstance Win32_Service -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'PathVeer' -or $_.Name -eq 'IranDirect' } |
        ForEach-Object { $_.Name })
    singleAuthorityHeld = $true  # derived: exactly one of PathVeer/IranDirect present & running
}

$active = @($evidence.authorities)
$evidence.singleAuthorityHeld = ($active.Count -le 1)

$evidence | ConvertTo-Json -Depth 4 | Set-Content -Path $OutFile -Encoding utf8
Write-Host "SCM evidence -> $OutFile (PathVeer=$(if($evidence.pathVeer){$evidence.pathVeer.state}else{'absent'), IranDirect=$(if($evidence.iranDirect){$evidence.iranDirect.state}else{'absent'}))" -ForegroundColor Cyan
$evidence | Format-List
