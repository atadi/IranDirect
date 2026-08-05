namespace IranDirect.Core.Observability.Telemetry;

/// <summary>
/// Exact, constant span names approved by the Phase 32.1 observability
/// architecture. Span names are NEVER built dynamically and MUST NOT contain
/// identities, prefixes, domains, paths, commands, or IDs.
/// </summary>
public static class IranDirectActivityNames
{
    // Root spans
    public const string RuntimeCycle = "IranDirect.RuntimeCycle";
    public const string PrefixUpdateCheck = "IranDirect.PrefixUpdateCheck";
    public const string CustomRouteRefresh = "IranDirect.CustomRouteRefresh";
    public const string IpcRequest = "IranDirect.IpcRequest";
    public const string SupportBundleExport = "IranDirect.SupportBundleExport";

    // Child spans
    public const string RuntimeObserve = "Runtime.Observe";
    public const string RuntimeBuildDecision = "Runtime.BuildDecision";
    public const string RuntimeBuildPreview = "Runtime.BuildPreview";
    public const string RuntimePlanChanges = "Runtime.PlanChanges";
    public const string RuntimeExecute = "Runtime.Execute";
    public const string RuntimePersistInventory = "Runtime.PersistInventory";

    public const string RoutesEnumerate = "Routes.Enumerate";
    public const string RoutesCreate = "Routes.Create";
    public const string RoutesDelete = "Routes.Delete";

    public const string PrefixHttpHead = "Prefix.HttpHead";
    public const string PrefixHttpGet = "Prefix.HttpGet";
    public const string PrefixCompare = "Prefix.Compare";
    public const string PrefixPersistMetadata = "Prefix.PersistMetadata";

    public const string DnsCacheRead = "Dns.CacheRead";
    public const string DnsResolve = "Dns.Resolve";
    public const string DnsCacheWrite = "Dns.CacheWrite";

    public const string IpcConnect = "Ipc.Connect";
    public const string IpcSend = "Ipc.Send";
    public const string IpcDispatch = "Ipc.Dispatch";
    public const string IpcReceive = "Ipc.Receive";

    public const string SupportCaptureSnapshot = "Support.CaptureSnapshot";
    public const string SupportSerialize = "Support.Serialize";
    public const string SupportWriteJson = "Support.WriteJson";
    public const string SupportCreateZip = "Support.CreateZip";
}
