using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PathVeer.Core.Cloud;
using PathVeer.Core.Installation;

namespace PathVeer.Service.Cloud;

/// <summary>
/// Background worker that periodically heartbeats PathVeer Cloud using the
/// enrolled device credential.
///
/// DESIGN SAFETY (per D2):
/// - Best-effort only. A Cloud outage, auth failure, or any exception MUST
///   NOT block Service startup, stop routing, restart networking, or change
///   desired configuration. This worker is fully isolated from the
///   runtime/reconcile cycle.
/// - The only telemetry sent is appVersion + protocolVersion. No IPs, routes,
///   hostnames, usernames, machine identifiers, or hardware data.
/// - On a revoked credential (401), it stops heartbeating permanently and
///   marks local state Revoked. It never creates a new Cloud device.
/// - On transient failure it backs off with full jitter and respects a max
///   consecutive-failure ceiling so a prolonged outage doesn't produce a
///   retry storm. A successful heartbeat resets the counter.
/// - Jittered interval avoids synchronized fleet heartbeat storms.
/// - If the machine is not enrolled, the worker does nothing (no
///   authenticated heartbeat is ever sent from an un-enrolled install).
/// </summary>
public sealed class CloudHeartbeatService : BackgroundService
{
    private static readonly Random Jitter = new();

    private readonly CloudRegistrationStore _store;
    private readonly PathVeerCloudClient _client;
    private readonly CloudOptions _options;
    private readonly ILogger<CloudHeartbeatService> _logger;
    private readonly string _appVersion;

    private volatile bool _stopHeartbeating;

    public CloudHeartbeatService(
        CloudRegistrationStore store,
        PathVeerCloudClient client,
        CloudOptions options,
        ILogger<CloudHeartbeatService> logger)
    {
        _store = store;
        _client = client;
        _options = options;
        _logger = logger;
        _appVersion = ProductIdentity.Version;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            // Cloud not configured. PathVeer remains fully operational
            // offline. The heartbeat worker simply idles.
            _logger.LogInformation(
                "PathVeer Cloud base URL is not configured; heartbeat " +
                "is disabled. PathVeer remains fully operational offline.");
            return;
        }

        // First beat happens after one jittered interval so startup is never
        // blocked waiting on Cloud.
        while (!stoppingToken.IsCancellationRequested
               && !_stopHeartbeating)
        {
            try
            {
                await Task.Delay(
                    JitteredInterval(),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (_stopHeartbeating)
            {
                break;
            }

            await BeatOnceAsync(stoppingToken);
        }
    }

    public async Task BeatOnceAsync(
        CancellationToken cancellationToken)
    {
        CloudRegistrationRecord record =
            await _store.LoadRegistrationAsync(cancellationToken);

        if (!record.IsEnrolled || record.CredentialRevoked)
        {
            // Nothing to heartbeat (not enrolled, or already revoked).
            if (record.CredentialRevoked)
            {
                _stopHeartbeating = true;
            }
            return;
        }

        if (record.ConsecutiveHeartbeatFailures
            >= _options.MaxConsecutiveHeartbeatFailures)
        {
            // Quiet period: stop hammering Cloud during a prolonged outage.
            // A fresh enrollment or reset clears this counter.
            return;
        }

        string? credential =
            await _store.GetCredentialAsync(cancellationToken);
        if (credential is null)
        {
            return;
        }

        try
        {
            HeartbeatResponse response =
                await _client.HeartbeatAsync(
                    credential,
                    _appVersion,
                    cancellationToken);

            await _store.RecordHeartbeatSuccessAsync(
                DateTimeOffset.UtcNow,
                cancellationToken);

            _logger.LogDebug(
                "PathVeer Cloud heartbeat succeeded. Device={DeviceId}",
                response.DeviceId);
        }
        catch (CloudHeartbeatAuthFailedException ex)
        {
            if (ex.Revoked)
            {
                await _store.MarkRevokedAsync(cancellationToken);
                _stopHeartbeating = true;
                _logger.LogWarning(
                    "PathVeer Cloud device credential revoked. Local " +
                    "functionality is preserved; re-enrollment is required.");
            }
            else
            {
                await _store.RecordHeartbeatFailureAsync(cancellationToken);
                _logger.LogWarning(
                    "PathVeer Cloud heartbeat rejected the credential. " +
                    "Stopping heartbeat for this credential.");
                _stopHeartbeating = true;
            }
        }
        catch (CloudHeartbeatException ex)
        {
            await _store.RecordHeartbeatFailureAsync(cancellationToken);
            _logger.LogWarning(
                ex,
                "PathVeer Cloud heartbeat failed (transient). " +
                "Will retry with backoff.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _store.RecordHeartbeatFailureAsync(cancellationToken);
            _logger.LogWarning(
                ex,
                "PathVeer Cloud heartbeat failed. Will retry with backoff.");
        }
    }

    private TimeSpan JitteredInterval()
    {
        // Full jitter: interval * [0.5, 1.5) around the configured cadence.
        double baseMs = _options.HeartbeatInterval.TotalMilliseconds;
        double jitterFactor = 0.5 + Jitter.NextDouble();
        return TimeSpan.FromMilliseconds(baseMs * jitterFactor);
    }
}
