@{
    RootModule        = 'PathVeerCertificationJea.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = 'a1b2c3d4-0000-4000-8000-000000000001'
    Description        = 'PathVeer certification JEA trusted module (narrow, validated wrappers).'
    PowerShellVersion  = '5.1'
    FunctionsToExport  = @(
        'Test-PathVeerCertificationAdmin',
        'Get-PathVeerServiceState',
        'Start-PathVeerService',
        'Stop-PathVeerService',
        'Stop-PathVeerTray',
        'Get-PathVeerInstallManifest',
        'Get-PathVeerInstalledFiles',
        'Get-PathVeerRouteState',
        'Invoke-PathVeerCertificationInstall',
        'Invoke-PathVeerCli',
        'Get-PathVeerProgramDataState',
        'Get-PathVeerCertificationBoundary',
        'Publish-PathVeerCertificationPayload'
    )
    CmdletsToExport    = @()
    VariablesToExport  = @()
    AliasesToExport    = @()
}
