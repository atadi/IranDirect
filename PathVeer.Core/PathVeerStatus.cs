namespace PathVeer.Core;

using PathVeer.Core.Runtime.Execution;

public sealed record PathVeerStatus
{
    public bool Enabled { get; init; }

    public bool? DesiredEnabled { get; init; }

    public RuntimeOperationSnapshot? Operation { get; init; }

    public string? Gateway { get; init; }

    public uint InterfaceIndex { get; init; }

    public string? InterfaceName { get; init; }

    public int PrefixCount { get; init; }

    public int InstalledRouteCount { get; init; }

    public DateTimeOffset? PrefixesUpdatedAt { get; init; }

    public string? LastError { get; init; }

    public int VpnEndpointCount { get; init; }

    public int ProtectedVpnEndpointCount { get; init; }

    public bool VpnEndpointsProtected { get; init; }

    /// <summary>
    /// The requested direct-country routing policy (ISO alpha-2 code), or
    /// null when the authoritative configuration could not be read. Diagnostic
    /// only: it surfaces "requested vs effective" without exposing the prefix
    /// list or adding telemetry dimensions.
    /// </summary>
    public string? RequestedCountryCode { get; init; }
}
