<# PathVeer observability — restore script (Phase 33.5).
#
# Restores configuration (and optional state) from a backup archive produced by
# backup.ps1. It NEVER overwrites the active stack's live volumes unless you
# explicitly point -StackRoot at a disposable project.
#
# Safe-by-default: this restores CONFIGURATION into -StackRoot (default: the
# repo's deployment/observability). It does not stop/start containers and does
# not touch running databases unless -RestoreState is given AND you pass a
# disposable -StackRoot with fresh volumes.
#
# Usage:
#   .\restore.ps1 -Archive .\backups\pathveer-obs-backup-20260806-120000.tar.gz -StackRoot C:\deploy\pathveer-obs
#   .\restore.ps1 -Archive ... -StackRoot C:\deploy\pathveer-obs -RestoreState
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Archive,
    [Parameter(Mandatory=$true)][string]$StackRoot,
    [switch]$RestoreState
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $Archive)) { throw "Archive not found: $Archive" }
New-Item -ItemType Directory -Force -Path $StackRoot | Out-Null

# Safety: refuse to restore state into a path that looks like the live repo.
if ($RestoreState -and ($StackRoot -eq (Resolve-Path (Join-Path $PSScriptRoot "..")).Path)) {
    throw "Refusing -RestoreState into the live repo root. Use a disposable -StackRoot."
}

Write-Host "Extracting configuration to $StackRoot ..."
& tar -xzf $Archive -C $StackRoot
if ($LASTEXITCODE -ne 0) { throw "tar extract exited with $LASTEXITCODE" }

# Verify: required files present after extract.
$required = @(
    "docker-compose.yml",
    "collector/otel-collector.yaml",
    "prometheus/prometheus.yml",
    "prometheus/rules/pathveer-recording-rules.yml",
    "prometheus/rules/pathveer-alert-rules.yml",
    "alertmanager/alertmanager.yml",
    "grafana/provisioning/dashboards/dashboards.yaml"
)
$missing = $required | Where-Object { -not (Test-Path (Join-Path $StackRoot $_)) }
if ($missing) { throw "Restore incomplete; missing: $($missing -join ', ')" }

Write-Host "Configuration restored to: $StackRoot"
if (-not $RestoreState) {
    Write-Host "State NOT restored (default). Bring the stack up with:"
    Write-Host "  docker compose -f $StackRoot/docker-compose.yml up -d"
}
else {
    Write-Host "RestoreState requested: copy Grafana/Alertmanager volumes from the"
    Write-Host "  archive's grafana-data/ alertmanager-data/ using 'docker cp' into the"
    Write-Host "  disposable stack's containers, then start them."
}
Write-Host "Verify after start: docker compose ps ; curl localhost:9090/api/v1/rules"
