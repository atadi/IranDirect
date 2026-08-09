using System.Text.Json;
using System.Text.Json.Serialization;

namespace PathVeer.Core.Installer;

/// <summary>
/// Raw installation-state facts produced by Install-PathVeer.ps1 -Action
/// statejson. The bootstrapper UI consumes these to choose a flow; it does not
/// infer state from folder existence alone.
/// </summary>
public sealed class InstallState
{
    [JsonPropertyName("productInstalled")]
    public bool ProductInstalled { get; set; }

    [JsonPropertyName("installedVersion")]
    public string? InstalledVersion { get; set; }

    [JsonPropertyName("serviceInstalled")]
    public bool ServiceInstalled { get; set; }

    [JsonPropertyName("legacyIranDirectInstalled")]
    public bool LegacyIranDirectInstalled { get; set; }

    [JsonPropertyName("legacyStatePresent")]
    public bool LegacyStatePresent { get; set; }

    [JsonPropertyName("pathVeerStatePresent")]
    public bool PathVeerStatePresent { get; set; }

    [JsonPropertyName("installRootPresent")]
    public bool InstallRootPresent { get; set; }

    [JsonPropertyName("manifestPresent")]
    public bool ManifestPresent { get; set; }

    [JsonPropertyName("partialInstallation")]
    public bool PartialInstallation { get; set; }
}

/// <summary>The user-facing installation scenario the UI should present.</summary>
public enum InstallScenario
{
    NotInstalled,
    SameVersion,
    Upgrade,
    Downgrade,
    SupportedLegacyMigration,
    PartialOrBroken,
    ConflictingAuthority
}

/// <summary>
/// Classifies a raw InstallState + a target package version into the scenario
/// the consumer UI should present. No install semantics are implemented here;
/// this is decision logic only.
/// </summary>
public static class InstallStateClassifier
{
    public static int CompareVersions(string? current, string target)
    {
        var a = Normalize(current);
        var b = Normalize(target);
        if (!Version.TryParse(a, out var va)) va = new Version(0, 0);
        if (!Version.TryParse(b, out var vb)) vb = new Version(0, 0);
        return va.CompareTo(vb);
    }

    private static string Normalize(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "0.0.0";
        int dash = v.IndexOf('-');
        return (dash >= 0 ? v[..dash] : v).Trim();
    }

    public static InstallScenario Classify(InstallState state, string targetVersion)
    {
        if (state.LegacyIranDirectInstalled && !state.ProductInstalled)
            return InstallScenario.SupportedLegacyMigration;

        if (state.LegacyIranDirectInstalled && state.ProductInstalled && state.ServiceInstalled)
            return InstallScenario.ConflictingAuthority;

        if (!state.ProductInstalled)
            return state.PartialInstallation ? InstallScenario.PartialOrBroken : InstallScenario.NotInstalled;

        int cmp = CompareVersions(state.InstalledVersion, targetVersion);
        if (cmp < 0) return InstallScenario.Upgrade;
        if (cmp > 0) return InstallScenario.Downgrade;
        return InstallScenario.SameVersion;
    }

    public static InstallState ParseStateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new InstallState();
        try
        {
            return JsonSerializer.Deserialize<InstallState>(json) ?? new InstallState();
        }
        catch (JsonException)
        {
            return new InstallState();
        }
    }
}
