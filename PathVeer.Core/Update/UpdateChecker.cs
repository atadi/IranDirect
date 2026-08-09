namespace PathVeer.Core.Update;

/// <summary>
/// Client-side update check/selection. Orchestrates source -> parse -> validate
/// -> signature -> version selection. Never downloads or executes anything.
/// </summary>
public sealed class UpdateChecker
{
    private readonly IReleaseSource _source;
    private readonly ReleaseManifestParser _parser;
    private readonly ReleaseSignatureVerifier _verifier;
    private readonly InstalledVersionSource _installed;
    private readonly string _architecture;

    public UpdateChecker(
        IReleaseSource source,
        ReleaseSignatureVerifier verifier,
        InstalledVersionSource installed,
        ReleaseManifestParser? parser = null,
        string architecture = "x64")
    {
        _source = source;
        _parser = parser ?? new ReleaseManifestParser();
        _verifier = verifier;
        _installed = installed;
        _architecture = architecture;
    }

    public async Task<UpdateCheckResult> CheckAsync(UpdateChannel channel, CancellationToken ct = default)
    {
        string? json;
        try
        {
            json = await _source.FetchManifestAsync(channel, ReleaseManifestParser.ExpectedPlatform, _architecture, ct)
                .ConfigureAwait(false);
        }
        catch (UpdateSourceException ex)
        {
            return UpdateCheckResult.Failure(UpdateCheckState.InvalidManifest, ex.Message);
        }
        catch (Exception ex)
        {
            // Network/unreachable -> non-fatal; routing continues normally.
            return UpdateCheckResult.Failure(UpdateCheckState.NetworkUnavailable, ex.Message);
        }

        if (string.IsNullOrWhiteSpace(json))
            return UpdateCheckResult.Failure(UpdateCheckState.NetworkUnavailable, "No manifest returned by source.");

        var parsed = _parser.Parse(json, _architecture);
        if (!parsed.Success || parsed.Manifest is null)
            return UpdateCheckResult.Failure(UpdateCheckState.InvalidManifest, parsed.Error ?? "Invalid manifest.");

        var manifest = parsed.Manifest;

        var sig = _verifier.Verify(manifest, json);
        if (!sig.IsValid)
            return UpdateCheckResult.Failure(UpdateCheckState.InvalidSignature,
                sig.IsUnsigned ? "Manifest signature missing (production requires signing)." : (sig.Error ?? "Invalid manifest signature."),
                manifest);

        if (!UpdateChannelNames.TryParse(manifest.Channel, out var manifestChannel))
            return UpdateCheckResult.Failure(UpdateCheckState.WrongChannel, $"Unrecognized channel '{manifest.Channel}'.", manifest);

        // Installed version.
        var installedVersion = _installed.ReadVersion();
        if (string.IsNullOrWhiteSpace(installedVersion) || !SemanticVersion.TryParse(installedVersion, out var installed))
            return UpdateCheckResult.Failure(UpdateCheckState.UnsupportedInstalledVersion,
                "Installed version unavailable or malformed; cannot safely compare.", manifest);
        var installedSem = installed;

        // Channel eligibility (does not consider downgrade).
        if (!ReleaseChannelPolicy.IsEligible(channel, manifest))
            return UpdateCheckResult.Failure(UpdateCheckState.WrongChannel, $"Release channel '{manifest.Channel}' not offered to this client channel.", manifest);

        // Minimum upgrade floor.
        if (!string.IsNullOrWhiteSpace(manifest.MinimumUpgradeVersion) &&
            SemanticVersion.TryParse(manifest.MinimumUpgradeVersion, out var floor) &&
            installedSem < floor)
            return UpdateCheckResult.Failure(UpdateCheckState.MinimumUpgradeNotMet,
                $"Installed {installedSem} is below the minimum upgrade floor {floor}.", manifest);

        // Version ordering (downgrade prevention).
        if (!SemanticVersion.TryParse(manifest.Version, out var available))
            return UpdateCheckResult.Failure(UpdateCheckState.InvalidManifest, "Manifest version unparseable.", manifest);

        if (available > installedSem)
            return UpdateCheckResult.UpdateAvailable(manifest, available, installedSem);
        if (available < installedSem)
            return UpdateCheckResult.CurrentNewer(manifest, available, installedSem);
        return UpdateCheckResult.NoUpdate(installedSem);
    }
}

public static class UpdateCheckResultExtensions
{
    public static UpdateCheckResult WrongChannelResult(this UpdateCheckResult _, ReleaseManifest m, SemanticVersion installed) =>
        UpdateCheckResult.Failure(UpdateCheckState.WrongChannel, $"Release channel '{m.Channel}' not offered to this client channel.", m);
}
