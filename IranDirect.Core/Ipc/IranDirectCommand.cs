namespace IranDirect.Core.Ipc;

public enum IranDirectCommand
{
    Status,
    UpdatePrefixes,
    Enable,
    Disable,
    Repair,
    VpnEndpoints,
    Diagnostics,
    GetConfiguration,
    SetConfigurationEnabled,
    SetConfigurationProfilePath,
    SetConfigurationDirectCountry,
    RuntimePlan,
    CustomRoutesList,
    CustomRoutesAddDomain,
    CustomRoutesAddIp,
    CustomRoutesAddCidr,
    CustomRoutesEnable,
    CustomRoutesDisable,
    CustomRoutesRemove,
    CustomRoutesResolve,
    CustomRoutesCacheStatus,
    CustomRoutesInvalidateCache,
    CustomRoutesInvalidateAllCaches,
    RuntimeSnapshot,
    PrefixUpdateCheckNow,
    ExecutionPreview,
    SupportBundleExport
}