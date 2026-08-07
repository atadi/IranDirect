<# IranDirect observability — backup script (Phase 33.5).
#
# Philosophy: configuration reproducibility first. Telemetry data (Prometheus
# TSDB, Tempo blocks) is DISPOSABLE by default and is NOT backed up unless
# -IncludeData is given AND the services expose a safe snapshot path.
#
# What is backed up (always):
#   * Compose files, collector/prometheus/tempo/alertmanager/grafana configs
#   * recording + alert rules, production overlay, runbooks
#   * Grafana provisioning + dashboards (source of truth)
# Optional (-IncludeState):
#   * Grafana sqlite DB, Alertmanager silences (copied live; safe, small)
#
# Secrets are EXCLUDED by default (secrets/ and .env*). Pass -IncludeSecrets
# ONLY when writing to an encrypted, access-controlled destination.
#
# Usage:
#   .\backup.ps1 -Destination \\backup\irandirect-obs
#   .\backup.ps1 -Destination D:\backups -IncludeState
#   .\backup.ps1 -Destination D:\backups -IncludeState -IncludeSecrets
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Destination,
    [switch]$IncludeState,
    [switch]$IncludeSecrets
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ts   = (Get-Date -Format "yyyyMMdd-HHmmss")
$archive = Join-Path $Destination "irandirect-obs-backup-$ts.tar.gz"
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

# Build an exclusion list for tar (Git-style patterns via --exclude).
$excl = @(
    "--exclude=./.env", "--exclude=./.env.production",
    "--exclude=./secrets", "--exclude=./production/secrets",
    "--exclude=./certificates/generated",
    "--exclude=./production/certificates/generated",
    "--exclude=./backups", "--exclude=./node-exporter-textfiles",
    "--exclude=*/bin", "--exclude=*/obj"
)
if (-not $IncludeSecrets) { $excl += "--exclude=./secrets" }

# Paths to include. Configuration is always included.
$include = @(
    "./docker-compose.yml",
    "./docker-compose.production.yml",
    "./.env.example",
    "./production/.env.production.example",
    "./collector",
    "./prometheus",
    "./tempo",
    "./alertmanager",
    "./grafana",
    "./production",
    "./runbooks"
)

if ($IncludeState) {
    # Grafana + Alertmanager persist small state volumes; copy them live.
    # These are safe to copy while running (sqlite + silence files).
    $include += "./grafana-data"   # requires the volume mapped; see note below
    $include += "./alertmanager-data"
}

# tar on Windows (Git/MSYS) — run from the repo root.
$tarArgs = @("-czf", $archive) + $excl + $include
Write-Host "Archiving configuration to $archive ..."
Push-Location $repo
try {
    & tar @tarArgs
    if ($LASTEXITCODE -ne 0) { throw "tar exited with $LASTEXITCODE" }
} finally {
    Pop-Location
}

# Manifest + checksum.
$manifest = Join-Path $Destination "irandirect-obs-backup-$ts.manifest.txt"
$sha = (Get-FileHash -Algorithm SHA256 $archive).Hash
@"
IranDirect observability backup
Timestamp : $ts
Archive    : $(Split-Path $archive -Leaf)
SHA256     : $sha
IncludeState  : $IncludeState
IncludeSecrets: $IncludeSecrets
Included paths:
$($include | ForEach-Object { "  $_" })
Excluded patterns:
$($excl | ForEach-Object { "  $_" })
"@ | Set-Content -Path $manifest

Write-Host "Backup complete."
Write-Host "  archive : $archive"
Write-Host "  sha256  : $sha"
Write-Host "  manifest: $manifest"
Write-Host "NOTE: Grafana/Alertmanager live state is only included if -IncludeState AND the"
Write-Host "      volumes are bind-mounted at ./grafana-data ./alertmanager-data. The default"
Write-Host "      named volumes are not directly tar-able here; copy them with 'docker cp'"
Write-Host "      from the running container when -IncludeState is used."
