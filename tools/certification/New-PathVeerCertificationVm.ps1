<# .SYNOPSIS
    Phase 37.6 — scaffold a disposable Hyper-V certification VM (guarded).

.DESCRIPTION
    Creates a NEW Gen2 Hyper-V VM with a clean checkpoint slot for certification.
    DESTRUCTIVE/RESOURCE operation: requires a real Windows 11 x64 ISO and
    -Apply. It does NOT start installation; it only provisions the VM + switch
    wiring so the certification matrix (GATE-1..12) can run on a disposable machine.

    Safe to re-run: if the VM already exists it is left untouched (no overwrite).

    ISO SOURCE (no blind download): by default the caller supplies -IsoPath.
    Optionally pass -DownloadIso to fetch the OFFICIAL Microsoft Evaluation Center
    Windows 11 Enterprise image from its fixed Microsoft fwlink
    (https://www.microsoft.com/en-us/evalcenter/evaluate-windows-11-enterprise).
    -DownloadIso still requires -Apply and writes to -IsoOutPath. The download
    URL is source-pinned to Microsoft; this is the legitimate, license-clean,
    free evaluation image for certification. Verify the downloaded file's SHA-256
    against Microsoft's published checksum before use.

.PARAMETER VmName
    VM name (default: PathVeer-Cert).
.PARAMETER IsoPath
    Path to a Windows 11 x64 ISO. Required unless -DownloadIso is used.
.PARAMETER DownloadIso
    Fetch the official Microsoft Evaluation Center Windows 11 Enterprise ISO.
    Requires -IsoOutPath and -Apply. Source-pinned to Microsoft; no arbitrary URL.
.PARAMETER IsoOutPath
    Destination path when using -DownloadIso.
.PARAMETER MemoryGB
    Assigned RAM in GB (default: 4).
.PARAMETER CheckpointName
    Clean snapshot name (default: cert-baseline).
.PARAMETER Apply
    Required switch to actually create the VM / download the ISO.
#>
param(
    [string]$VmName = 'PathVeer-Cert',
    [Parameter(Mandatory = $false)][string]$IsoPath = '',
    [switch]$DownloadIso,
    [string]$IsoOutPath = (Join-Path $env:TEMP 'Windows11-Enterprise-Eval.iso'),
    [int]$MemoryGB = 4,
    [string]$CheckpointName = 'cert-baseline',
    [switch]$Apply
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Official Microsoft Evaluation Center Windows 11 Enterprise ISO (source-pinned).
$MsEvalFwLink = 'https://go.microsoft.com/fwlink/p/?linkid=2195682&clcid=0x409&culture=en-us&country=us'
$MsEvalPage  = 'https://www.microsoft.com/en-us/evalcenter/evaluate-windows-11-enterprise'

if (-not (Get-Command New-VM -ErrorAction SilentlyContinue)) {
    throw "Hyper-V PowerShell module not available on this host. Verify Hyper-V role is installed."
}

# Resolve the ISO path: download if requested, else require a supplied path.
if ($DownloadIso) {
    if (-not $Apply) {
        Write-Host "DRY RUN: would download the official Microsoft Evaluation Center Windows 11 Enterprise ISO to '$IsoOutPath'. Re-run with -Apply to proceed." -ForegroundColor Cyan
        exit 0
    }
    Write-Host "Downloading official Windows 11 Enterprise evaluation ISO from Microsoft..." -ForegroundColor Cyan
    Write-Host "  Source page: $MsEvalPage" -ForegroundColor DarkGray
    try {
        Invoke-WebRequest -Uri $MsEvalFwLink -OutFile $IsoOutPath -UseBasicParsing -ErrorAction Stop
    } catch {
        throw "ISO download failed: $($_.Exception.Message). Verify network access and that the Microsoft eval link is current ($MsEvalPage)."
    }
    if (-not (Test-Path $IsoOutPath) -or ((Get-Item $IsoOutPath).Length -lt 1GB)) {
        throw "Downloaded ISO is missing or suspiciously small; aborting. Verify $MsEvalPage manually."
    }
    Write-Host "ISO downloaded to '$IsoOutPath'. VERIFY its SHA-256 against Microsoft's published checksum before creating the VM." -ForegroundColor Yellow
    $IsoPath = $IsoOutPath
} else {
    if ([string]::IsNullOrWhiteSpace($IsoPath)) { throw "Supply -IsoPath (a Windows 11 x64 ISO) or use -DownloadIso." }
    if (-not (Test-Path $IsoPath)) { throw "IsoPath not found: $IsoPath" }
}

$existing = Get-VM -Name $VmName -ErrorAction SilentlyContinue
if ($existing) { Write-Host "VM '$VmName' already exists; leaving untouched." -ForegroundColor Yellow; exit 0 }

if (-not $Apply) {
    Write-Host "DRY RUN: would create VM '$VmName' from '$IsoPath' ($MemoryGB GB). Re-run with -Apply to proceed." -ForegroundColor Cyan
    exit 0
}

# --- past this point -Apply was given; perform the resource operations ---
$switch = Get-VMSwitch -SwitchType External -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $switch) { $switch = Get-VMSwitch -SwitchType Internal -ErrorAction SilentlyContinue | Select-Object -First 1 }
if (-not $switch) { throw "No Hyper-V virtual switch available; create one first (e.g. New-VMSwitch -Name External -NetAdapterName <if> -AllowManagementOS $true)." }

$vhdx = Join-Path (Split-Path $IsoPath) "$VmName.vhdx"
New-VM -Name $VmName -MemoryStartupBytes ([int64]$MemoryGB * 1GB) -Generation 2 -BootDevice VHD -Path (Split-Path $IsoPath) | Out-Null
$dvd = Add-VMDvdDrive -VMName $VmName -Path $IsoPath -Passthru
Set-VMFirmware -VMName $VmName -FirstBootDevice $dvd -EnableSecureBoot On -SecureBootTemplate 'MicrosoftWindows'
Get-VMNetworkAdapter -VMName $VmName | Connect-VMNetworkAdapter -VMSwitch $switch
Set-VM -VMName $VmName -CheckpointType Production
Write-Host "VM '$VmName' created. Install Windows 11, then run:" -ForegroundColor Green
Write-Host "  Checkpoint-VM -Name $VmName -SnapshotName $CheckpointName" -ForegroundColor DarkGray
Write-Host "Re-run this script is a no-op once the VM exists." -ForegroundColor DarkGray
