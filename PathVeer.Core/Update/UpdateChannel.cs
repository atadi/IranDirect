namespace PathVeer.Core.Update;

/// <summary>Release channels supported by the update model (centralized).</summary>
public enum UpdateChannel
{
    Stable,
    Beta
}

public static class UpdateChannelNames
{
    public const string Stable = "stable";
    public const string Beta = "beta";

    public static bool TryParse(string? value, out UpdateChannel channel)
    {
        channel = UpdateChannel.Stable;
        if (string.IsNullOrWhiteSpace(value)) return false;
        switch (value!.Trim().ToLowerInvariant())
        {
            case Stable: channel = UpdateChannel.Stable; return true;
            case Beta: channel = UpdateChannel.Beta; return true;
            default: return false;
        }
    }

    public static string ToString(UpdateChannel channel) =>
        channel == UpdateChannel.Beta ? Beta : Stable;
}

/// <summary>
/// Channel selection policy (centralized so the semantics live in one place).
///
///   stable: only stable (no-prerelease) releases.
///   beta:   the highest of {stable releases, beta/rc releases}; a beta client
///           may receive a beta OR a stable newer than its current version,
///           but never a downgrade.
///
/// This never silently promotes stable users to beta, and never recommends a
/// version lower than the installed one.
/// </summary>
public static class ReleaseChannelPolicy
{
    /// <summary>
    /// Returns true if <paramref name="candidate"/> may be offered to a client
    /// on <paramref name="channel"/>. Does NOT consider installed-version
    /// ordering (callers apply downgrade prevention separately).
    /// </summary>
    public static bool IsEligible(UpdateChannel channel, ReleaseManifest candidate)
    {
        if (!UpdateChannelNames.TryParse(candidate.Channel, out var candChannel))
            return false; // unrecognized channel string -> reject (fail safe)
        if (channel == UpdateChannel.Stable)
            return candChannel == UpdateChannel.Stable;
        // Beta channel accepts beta/rc and stable alike.
        return candChannel == UpdateChannel.Beta || candChannel == UpdateChannel.Stable;
    }
}
