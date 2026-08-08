namespace PathVeer.Core.Ipc;

/// <summary>
/// Centralized named-pipe IPC identity for the PathVeer control channel.
///
/// Phase 36.4 introduced the PathVeer primary pipe while retaining the legacy
/// <c>IranDirect.Control.v1</c> pipe for a compatibility window so old clients
/// (or a temporarily mixed-version fleet) can still reach the service. Both
/// pipes dispatch into the SAME command server and the SAME
/// OperationCoordinator; they are two front doors into one process, never two
/// authorities.
/// </summary>
public static class PathVeerPipeNames
{
    /// <summary>Primary control pipe (current PathVeer identity).</summary>
    public const string PrimaryPipeName = "PathVeer.Control.v1";

    /// <summary>
    /// Legacy control pipe retained for compatibility. Clients may fall back to
    /// this endpoint when the primary pipe is unavailable (staged upgrades).
    /// </summary>
    public const string LegacyPipeName = "IranDirect.Control.v1";

    /// <summary>
    /// Both pipe names the PathVeer Service listens on during the
    /// compatibility window, in dispatch priority order.
    /// </summary>
    public static IReadOnlyList<string> AllListenNames =>
        [PrimaryPipeName, LegacyPipeName];
}
