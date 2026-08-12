<#
.SYNOPSIS
    *** RETIRED — replaced by Test-PathVeerCertGuestJea.ps1 ***

.DESCRIPTION
    This probe exercised the Scheduled-Task (RunLevel Highest) elevation primitive, which real-VM
    Windows REJECTED with:
        Scheduled task registration/start failed: Access is denied.

    The filtered PowerShell Direct parent (PV-CERT\pvcert, UAC-filtered token) cannot register an
    elevated scheduled task, so that design is circular and cannot work on this certification VM.

    Run the replacement JEA control-plane proof instead:

        cd C:\codespace\PathVeer
        pwsh -NoProfile -File tools\certification\Test-PathVeerCertGuestJea.ps1

    Prerequisite (operator performs ONCE, genuinely elevated in the guest):
        & 'C:\Program Files\PathVeerCertificationJea\Enable-PathVeerCertificationJea.ps1'
    (or copy tools/certification/jea\Enable-PathVeerCertificationJea.ps1 into the guest and run it
     from an elevated PowerShell.)
#>

Write-Host 'RETIRED: Test-PathVeerCertGuestElevation.ps1' -ForegroundColor Yellow
Write-Host 'The Scheduled-Task (RunLevel Highest) elevation design was rejected by real-VM Windows (Access is denied).' -ForegroundColor Red
Write-Host 'Use Test-PathVeerCertGuestJea.ps1, which proves the JEA certification control plane instead:' -ForegroundColor Cyan
Write-Host '    pwsh -NoProfile -File tools\certification\Test-PathVeerCertGuestJea.ps1' -ForegroundColor White
exit 2
