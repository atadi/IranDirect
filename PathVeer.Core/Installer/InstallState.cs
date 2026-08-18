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
        return CompareVersionParts(current).CompareTo(CompareVersionParts(target));
    }

    /// <summary>
    /// Single deterministic installed-version contract shared by the GUI
    /// classifier and the install engine (Compare-VersionOrder in
    /// Install-PathVeer.ps1). A version is decomposed into:
    ///   * base  : numeric major.minor.build
    ///   * rank  : 1 = stable (no prerelease), 0 = prerelease (has '-' tag)
    ///             so a stable release outranks any prerelease of the same base
    ///   * seq   : the trailing integer of the prerelease tag
    ///             (devsign.5 -> 5, beta.1 -> 1); 0 when absent
    /// Ordering: base wins; then a stable release outranks any prerelease of the
    /// same base; then prereleases are ordered by their sequence number. This
    /// makes 1.0.0-devsign.4 &lt; 1.0.0-devsign.5 (Upgrade) and
    /// 1.0.0-devsign.6 &gt; 1.0.0-devsign.5 (Downgrade), while still treating a
    /// bare 1.0.0 as newer than 1.0.0-beta.1.
    /// </summary>
    private static (Version Base, int Rank, int Seq) CompareVersionParts(string? v)
    {
        string raw = string.IsNullOrWhiteSpace(v) ? "0.0.0" : v.Trim();
        string baseStr = raw;
        int rank = 0;
        int seq = 0;

        int dash = raw.IndexOf('-');
        if (dash >= 0)
        {
            baseStr = raw[..dash];
            rank = 0; // prerelease ranks BELOW stable
            // Trailing integer of the prerelease tag, if any.
            string tag = raw[(dash + 1)..];
            int lastDigits = 0;
            foreach (char c in tag)
            {
                if (c >= '0' && c <= '9') { lastDigits = lastDigits * 10 + (c - '0'); }
                else { lastDigits = 0; }
            }
            seq = lastDigits;
        }
        else
        {
            rank = 1; // stable outranks any prerelease of the same base
        }

        if (!Version.TryParse(baseStr, out var va)) va = new Version(0, 0);
        return (va, rank, seq);
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
