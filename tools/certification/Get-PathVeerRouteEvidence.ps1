<# .SYNOPSIS
    Phase 37.6 — capture focused Windows route evidence for certification gates.

.DESCRIPTION
    Read-only capture of only the routes relevant to certification: a bounded set
    of PathVeer/IranDirect-managed prefixes plus any explicitly-listed external
    control prefixes. Intended for GATE-2 (native route mutation/recovery) before/
    after comparison. Does not mutate routing.

.PARAMETER ManagedPrefixes
    One or more prefixes PathVeer is expected to own (e.g. 10.20.30.0/24).
.PARAMETER ExternalControlPrefix
    An external/control prefix that must remain untouched by PathVeer.
.PARAMETER OutFile
    Path for the route evidence JSON (default: ./route-evidence.json).
#>
[CmdletBinding()]
param(
    [string[]]$ManagedPrefixes = @(),
    [string]$ExternalControlPrefix = '',
    [string]$OutFile = './route-evidence.json'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$routes = @(Get-NetRoute -ErrorAction SilentlyContinue)
$managed = @()
foreach ($p in $ManagedPrefixes) {
    $m = $routes | Where-Object { $_.DestinationPrefix -eq $p }
    $managed += [ordered]@{ prefix = $p; present = ($null -ne $m); ifIndex = if($m){$m.InterfaceIndex}else{$null} }
}
$ext = $null
if ($ExternalControlPrefix) {
    $e = $routes | Where-Object { $_.DestinationPrefix -eq $ExternalControlPrefix }
    $ext = [ordered]@{ prefix = $ExternalControlPrefix; present = ($null -ne $e); ifIndex = if($e){$e.InterfaceIndex}else{$null} }
}

$evidence = [ordered]@{
    capturedUtc   = (Get-Date).ToUniversalTime().ToString('o')
    managedRoutes = $managed
    externalControl = $ext
    note          = 'Read-only. Compare before/after upgrade to prove managed ownership preserved and external untouched.'
}
$evidence | ConvertTo-Json -Depth 4 | Set-Content -Path $OutFile -Encoding utf8
Write-Host "Route evidence -> $OutFile" -ForegroundColor Cyan
$evidence | Format-List
