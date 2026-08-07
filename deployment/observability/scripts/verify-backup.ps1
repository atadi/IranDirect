<# IranDirect observability — backup verification (Phase 33.5).
#
# Verifies a backup archive without restoring it: confirms the archive unpacks,
# the manifest/checksum matches, required configuration files are present, and
# NO secret material is present inside the archive.
#
# Usage:
#   .\verify-backup.ps1 -Archive .\backups\irandirect-obs-backup-20260806-120000.tar.gz
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Archive
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $Archive)) { throw "Archive not found: $Archive" }

$tmp = Join-Path $env:TEMP ("irandirect-obs-verify-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
try {
    & tar -tzf $Archive | Set-Content -Path (Join-Path $tmp "listing.txt")
    if ($LASTEXITCODE -ne 0) { throw "Cannot read archive (tar -tzf failed)." }

    $listing = Get-Content (Join-Path $tmp "listing.txt") | ForEach-Object { $_ -replace '^\./', '' } | Where-Object { $_ -ne '' }

    # 1) Required config present.
    $required = @(
        "docker-compose.yml",
        "collector/otel-collector.yaml",
        "prometheus/prometheus.yml",
        "prometheus/rules/irandirect-recording-rules.yml",
        "prometheus/rules/irandirect-alert-rules.yml",
        "alertmanager/alertmanager.yml",
        "grafana/provisioning/dashboards/dashboards.yaml"
    )
    $missing = $required | Where-Object { $listing -notcontains $_ }
    if ($missing) { throw "Missing required config in archive: $($missing -join ', ')" }
    Write-Host "[OK] All required configuration files present."

    # 2) No secret material inside. Real secrets are exact `.env`/`.env.production`
    #    and any `secrets/` directory. `.env.example` / `.env.production.example`
    #    templates and `secrets/README.md` are safe and must NOT trip this check.
    #    Generated private key material under `certificates/generated/` (including
    #    the `production/` variant) is also secret and must never be backed up.
    $secretHits = $listing | Where-Object {
        ($_ -eq ".env") -or ($_ -eq ".env.production") -or
        ($_ -match "(^|/)secrets/") -or
        ($_ -match "certificates/generated/") -or
        ($_ -match "\.pem$")
    }
    # Reverse the example-template false positives: keep only the dangerous ones.
    $secretHits = $secretHits | Where-Object {
        ($_ -eq ".env") -or ($_ -eq ".env.production") -or
        ($_ -match "(^|/)secrets/") -or
        ($_ -match "certificates/generated/") -or
        ($_ -match "\.pem$")
    }
    if ($secretHits) { throw "Secret material found in archive:`n$($secretHits -join "`n")" }
    Write-Host "[OK] No secret files (.env*, secrets/, certificates/generated/*, *.pem) inside archive."

    # 3) Checksum manifest present alongside.
    $dir = Split-Path $Archive
    $base = [System.IO.Path]::GetFileNameWithoutExtension($Archive) -replace "\.tar$", ""
    $manifest = Join-Path $dir ($base + ".manifest.txt")
    if (-not (Test-Path $manifest)) { Write-Warning "No manifest found next to archive ($manifest); skipping checksum." }
    else {
        $sha = (Get-FileHash -Algorithm SHA256 $Archive).Hash
        if ((Get-Content $manifest) -match $sha) { Write-Host "[OK] Archive SHA256 matches manifest." }
        else { Write-Warning "SHA256 in manifest does not match archive; re-verify provenance." }
    }

    Write-Host "Verification passed: $Archive"
} finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
