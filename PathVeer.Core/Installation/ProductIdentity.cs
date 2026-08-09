using System.Reflection;

namespace PathVeer.Core.Installation;

/// <summary>
/// The product identity strings PathVeer presents to the outside world.
///
/// The version is read from the executing assembly rather than hardcoded, so
/// the HTTP User-Agent tracks real builds instead of freezing at a fictional
/// <c>1.0</c> (which is what the legacy <c>IranDirect/1.0</c> string did).
/// </summary>
public static class ProductIdentity
{
    /// <summary>Product name used in user-visible and protocol identity.</summary>
    public const string ProductName = "PathVeer";

    /// <summary>
    /// Fallback version used only when assembly metadata is unavailable.
    /// </summary>
    public const string FallbackVersion = "1.0.0";

    private static readonly Lazy<string> LazyVersion =
        new(ResolveVersion);

    private static readonly Lazy<string> LazyUserAgent =
        new(() => $"{ProductName}/{Version}");

    /// <summary>Informational product version, e.g. <c>1.0.0</c>.</summary>
    public static string Version => LazyVersion.Value;

    /// <summary>HTTP User-Agent, e.g. <c>PathVeer/1.0.0</c>.</summary>
    public static string UserAgent => LazyUserAgent.Value;

    private static string ResolveVersion()
    {
        Assembly assembly = typeof(ProductIdentity).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip source-revision metadata ("1.0.0+abc123") so the
            // User-Agent stays a stable, bounded token and never leaks a
            // build hash to remote prefix sources.
            int plus = informational.IndexOf('+');

            return plus >= 0
                ? informational[..plus]
                : informational;
        }

        Version? assemblyVersion = assembly.GetName().Version;

        return assemblyVersion is null
            ? FallbackVersion
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}."
              + $"{assemblyVersion.Build}";
    }
}
