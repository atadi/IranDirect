namespace PathVeer.Core.Cloud;

/// <summary>
/// Configuration for the PathVeer Cloud client.
///
/// The base URL is configuration-only and defaults to nothing. Production is
/// NOT deployed yet; normal production builds must explicitly set this in
/// configuration. Development/test may point at staging or a local endpoint.
///
/// REQUIREMENT: the URL must be https://. The client rejects anything else,
/// so a misconfiguration can never silently fall back to cleartext or to the
/// staging endpoint in production.
/// </summary>
public sealed class CloudOptions
{
    /// <summary>
    /// Absolute https:// base URL of PathVeer Cloud. Empty means "cloud
    /// disabled / not configured" — PathVeer remains fully operational
    /// offline. No default to staging.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Per-request timeout for Cloud calls.</summary>
    public TimeSpan RequestTimeout { get; set; } =
        TimeSpan.FromSeconds(20);

    /// <summary>
    /// Heartbeat cadence. Kept modest; randomized jitter is applied by the
    /// heartbeat worker to avoid synchronized storms across a fleet.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } =
        TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum consecutive heartbeat failures before the loop goes quiet and
    /// stops trying (transient-outage backoff). A successful heartbeat resets
    /// the counter. An auth failure (401) is terminal and bypasses this.
    /// </summary>
    public int MaxConsecutiveHeartbeatFailures { get; set; } = 10;
}
