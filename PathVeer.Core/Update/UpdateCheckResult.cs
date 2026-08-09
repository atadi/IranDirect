namespace PathVeer.Core.Update;

/// <summary>
/// Typed outcome of an update check. Normal states (NoUpdate, CurrentNewer)
/// are NOT exceptions; only exceptional transport/parse failures surface as
/// exceptions or NetworkUnavailable/InvalidManifest.
/// </summary>
public sealed class UpdateCheckResult
{
    public UpdateCheckState State { get; init; }
    public ReleaseManifest? Manifest { get; init; }
    public SemanticVersion? AvailableVersion { get; init; }
    public SemanticVersion? InstalledVersion { get; init; }
    public string? Detail { get; init; }

    public static UpdateCheckResult NoUpdate(SemanticVersion installed) =>
        new() { State = UpdateCheckState.NoUpdate, InstalledVersion = installed };

    public static UpdateCheckResult UpdateAvailable(ReleaseManifest m, SemanticVersion available, SemanticVersion installed) =>
        new() { State = UpdateCheckState.UpdateAvailable, Manifest = m, AvailableVersion = available, InstalledVersion = installed };

    public static UpdateCheckResult CurrentNewer(ReleaseManifest m, SemanticVersion available, SemanticVersion installed) =>
        new() { State = UpdateCheckState.CurrentVersionNewer, Manifest = m, AvailableVersion = available, InstalledVersion = installed };

    public static UpdateCheckResult Failure(UpdateCheckState state, string detail, ReleaseManifest? m = null) =>
        new() { State = state, Detail = detail, Manifest = m };
}

public enum UpdateCheckState
{
    NoUpdate,
    UpdateAvailable,
    CurrentVersionNewer,
    UnsupportedInstalledVersion,
    InvalidManifest,
    InvalidSignature,
    IncompatibleArchitecture,
    WrongProduct,
    WrongChannel,
    NetworkUnavailable,
    MinimumUpgradeNotMet
}
