<# .SYNOPSIS
    Phase 37.6 — scaffold a disposable Hyper-V certification VM (guarded).

.DESCRIPTION
    Creates a NEW Gen2 Hyper-V VM with a clean checkpoint named for certification.
    DESTRUCTIVE/RESOURCE operation: requires -IsoPath (a real Windows 11 x64 ISO
    the caller supplies) and -Confirm. It does NOT auto-download an ISO and does
    NOT start installation; it only provisions the VM + checkpoint slot so the
    certification matrix (GATE-1..12) can run on a disposable machine.

    Safe to re-run: if the VM already exists it is left untouched (no overwrite).

.PARAMETER VmName
    VM name (default: PathVeer-Cert).
.PARAMETER IsoPath
    Path to a Windows 11 x64 ISO. Required.
.PARAMETER MemoryGB
    Assigned RAM in GB (default: 4).
.PARAMETER CheckpointName
    Clean snapshot name (default: cert-baseline).
.PARAMETER Confirm
    Required switch to actually create the VM.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]
param(
    [string]$VmName = 'PathVeer-Cert',
    [Parameter(Mandatory = $true)][string]$IsoPath,
    [int]$MemoryGB = 4,
    [string]$CheckpointName = 'cert-baseline',
    [switch]$Confirm
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command New-VM -ErrorAction SilentlyContinue)) {
    throw "Hyper-V PowerShell module not available on this host. Verify Hyper-V role is installed."
}
if (-not (Test-Path $IsoPath)) { throw "IsoPath not found: $IsoPath" }

$existing = Get-VM -Name $VmName -ErrorAction SilentlyContinue
if ($existing) { Write-Host "VM '$VmName' already exists; leaving untouched." -ForegroundColor Yellow; exit 0 }

if (-not $Confirm) {
    Write-Host "DRY RUN: would create VM '$VmName' from '$IsoPath' ($MemoryGB GB). Re-run with -Confirm to proceed." -ForegroundColor Cyan
    exit 0
}

if ($PSCmdlet.ShouldProcess($VmName, "Create Hyper-V certification VM")) {
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
}
