namespace IranDirect.Core.Testing.FaultInjection;

public enum FaultInjectionPoint
{
    FileRead,
    FileWrite,
    FileMove,
    JsonLoad,
    JsonSave,
    HttpRequest,
    DnsLookup,
    NamedPipeSend,
    RouteEnumeration,
    RouteCreate,
    RouteDelete,
    SnapshotCapture,
    DiagnosticsRun
}
