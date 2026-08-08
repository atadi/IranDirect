namespace PathVeer.Core.ServiceLifecycle;

/// <summary>
/// Windows Service identity for the PathVeer Service (Phase 36.4).
///
/// The service was previously named <c>IranDirect</c> / <c>IranDirect Service</c>.
/// This type holds the new external identity. The legacy <c>IranDirect</c> service
/// name is intentionally retained only as a migration input (see
/// <see cref="LegacyServiceNames"/>) so the installer can stop/remove the old
/// service during the single-authority upgrade.
/// </summary>
public static class PathVeerServiceNames
{
    public const string ServiceName = "PathVeer";

    public const string DisplayName = "PathVeer Service";

    public const string Description =
        "Routes direct IPv4 prefixes through the ISP gateway while " +
        "protecting VPN endpoint connectivity.";
}

/// <summary>
/// Legacy Windows Service identity retained only for the single-authority
/// migration: the installer stops and removes the old <c>IranDirect</c> service
/// so it can never run concurrently with the new PathVeer Service.
/// </summary>
public static class LegacyServiceNames
{
    public const string ServiceName = "IranDirect";

    public const string DisplayName = "IranDirect Service";
}
