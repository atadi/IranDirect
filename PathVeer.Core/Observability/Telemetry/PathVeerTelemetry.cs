namespace PathVeer.Core.Observability.Telemetry;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

/// <summary>
/// Central, process-wide telemetry definitions for PathVeer.
///
/// This type defines exactly one <see cref="ActivitySource"/> and one
/// <see cref="Meter"/>, both static and read-only. It contains no exporters,
/// no listeners, no instrumentation of workflows, and no dependency injection.
/// Later implementation phases attach listeners to these instances from the
/// composition root only.
///
/// Naming convention: source and meter name are both the stable constant
/// <c>PathVeer.Core</c>. The version is derived from the Core assembly so
/// it stays in lock-step with the shipping binary and never diverges from a
/// hand-maintained string.
/// </summary>
public static class PathVeerTelemetry
{
    /// <summary>The canonical OpenTelemetry source and meter name.</summary>
    public const string SourceName = "PathVeer.Core";

    /// <summary>
    /// Assembly-derived version. Uses the informational/assembly version of the
    /// Core assembly; falls back to a non-empty stable string if, for some
    /// build configuration, the version cannot be resolved.
    /// </summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>Single process-wide activity source. No listeners here.</summary>
    public static ActivitySource ActivitySource { get; } =
        new(SourceName, Version);

    /// <summary>Single process-wide meter. No instruments created here.</summary>
    public static Meter Meter { get; } = new(SourceName, Version);

    private static string ResolveVersion()
    {
        try
        {
            Assembly assembly = typeof(PathVeerTelemetry).Assembly;
            string? version = assembly.GetName().Version?.ToString();
            if (!string.IsNullOrEmpty(version))
                return version;

            AssemblyInformationalVersionAttribute? attr =
                assembly.GetCustomAttribute<
                    AssemblyInformationalVersionAttribute>();
            if (!string.IsNullOrEmpty(attr?.InformationalVersion))
                return attr.InformationalVersion;
        }
        catch (Exception)
        {
            // Telemetry must never fail the host. Fall through to the stable
            // fallback below.
        }

        return "1.0.0";
    }
}
