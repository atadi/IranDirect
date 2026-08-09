using System.Text.Json.Serialization;

namespace PathVeer.Core.Installation;

/// <summary>
/// Bounded installer metadata written to
/// <c>%ProgramFiles%\PathVeer\install-manifest.json</c>.
///
/// Deliberately excluded: secrets, runtime configuration, machine identifiers,
/// user names. This records only what the upgrader needs in order to reason
/// about an existing installation.
/// </summary>
public sealed record InstallManifest
{
    /// <summary>Manifest schema version, independent of the product version.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Installed PathVeer product version, e.g. <c>1.0.0</c>.</summary>
    public string ProductVersion { get; init; } = string.Empty;

    /// <summary>Install root, e.g. <c>C:\Program Files\PathVeer</c>.</summary>
    public string InstallRoot { get; init; } = string.Empty;

    public string ServiceExecutablePath { get; init; } = string.Empty;

    public string CliExecutablePath { get; init; } = string.Empty;

    public string TrayExecutablePath { get; init; } = string.Empty;

    /// <summary>UTC timestamp of the install/upgrade that wrote this manifest.</summary>
    public DateTimeOffset InstalledAtUtc { get; init; }

    /// <summary>
    /// Marker recording that this installation completed the legacy
    /// IranDirect → PathVeer service migration, so a repeat run can converge
    /// instead of re-running legacy teardown.
    /// </summary>
    public bool LegacyServiceMigrationCompleted { get; init; }

    /// <summary>
    /// Product version this installation was upgraded from, when it replaced an
    /// earlier PathVeer install. Null on a fresh install.
    /// </summary>
    public string? UpgradedFromVersion { get; init; }

    public const int CurrentSchemaVersion = 1;

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(ProductVersion)
        && string.IsNullOrWhiteSpace(InstallRoot);
}
